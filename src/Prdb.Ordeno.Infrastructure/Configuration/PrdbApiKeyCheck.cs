using Microsoft.Extensions.Logging;
using Microsoft.Kiota.Abstractions;

using Prdb.Ordeno.Core.Configuration;
using Prdb.Sdk;

namespace Prdb.Ordeno.Infrastructure.Configuration;

/// <summary>
/// Asks prdb who a key belongs to. <c>GET /user-identity</c> is the cheapest
/// call that needs authentication and returns nothing about the corpus, so it
/// answers "does this key work" without pretending to be a search.
/// </summary>
/// <remarks>
/// The key being checked is the one the user just typed, not the one in the
/// database, so a client is built per check — through
/// <see cref="PrdbClientFactory"/>, never by hand, because it is the factory
/// that refuses a redirect to another origin while <c>X-Api-Key</c> is on the
/// request. Only the transport is shared, from <c>IHttpClientFactory</c>, so
/// checking a key twice does not cost two connections.
/// <para>
/// Nothing here logs the key. What is logged is what prdb said about it.
/// </para>
/// </remarks>
public sealed class PrdbApiKeyCheck(
    IHttpMessageHandlerFactory handlers,
    ILogger<PrdbApiKeyCheck> logger)
    : IPrdbApiKeyCheck
{
    /// <summary>
    /// Someone is waiting in front of a form. A minute of retries would be worse
    /// than being told that prdb did not answer.
    /// </summary>
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(15);

    public async Task<ApiKeyCheck> CheckAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new ApiKeyCheck(ApiKeyStatus.Refused, "Enter the API key from your prdb account.");
        }

        var client = PrdbClientFactory.Create(
            apiKey.Trim(),
            transport: handlers.CreateHandler(PrdbTransport.HttpClientName),
            // The SDK's retry would turn one refused check into three requests
            // with the same answer, and a rate limit into a wait nobody asked for.
            retry: PrdbRetryOptions.Disabled,
            timeout: CheckTimeout);

        try
        {
            var identity = await client.UserIdentity.GetAsync(cancellationToken: cancellationToken);

            if (identity?.UserHash is null)
            {
                logger.LogWarning("prdb accepted the API key but answered without an identity.");

                return new ApiKeyCheck(
                    ApiKeyStatus.Unreachable,
                    "prdb answered something the tool did not understand, so the key was not checked. "
                    + "Try again in a moment.");
            }

            logger.LogInformation("The prdb API key was checked and accepted.");

            return new ApiKeyCheck(ApiKeyStatus.Valid, null);
        }
        catch (ApiException exception)
        {
            logger.LogWarning(
                "prdb refused the API key check with status {Status}.",
                exception.ResponseStatusCode);

            return FromStatus(exception.ResponseStatusCode);
        }
        catch (CrossOriginRedirectException exception)
        {
            logger.LogError(
                exception,
                "The API key check was redirected off the prdb host and stopped before the key was sent.");

            return new ApiKeyCheck(
                ApiKeyStatus.Unreachable,
                "Something answered for prdb and tried to send the tool somewhere else. The key was "
                + "not handed over, and it was not checked — a proxy between this container and "
                + "api.prdb.net is the usual explanation.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "prdb could not be reached to check the API key.");

            return new ApiKeyCheck(
                ApiKeyStatus.Unreachable,
                "prdb could not be reached, so the key was not checked. The container needs to reach "
                + "api.prdb.net over https — check its network and any DNS or proxy in front of it.");
        }
    }

    private static ApiKeyCheck FromStatus(int status) => status switch
    {
        401 => new ApiKeyCheck(
            ApiKeyStatus.Refused,
            "prdb does not know this key. Copy it again from your account page on prdb.net — it is "
            + "the API key, not the password you sign in with."),

        403 => new ApiKeyCheck(
            ApiKeyStatus.Refused,
            "prdb knows this key but will not let it in. Check that the account it belongs to still "
            + "has an active subscription."),

        429 => new ApiKeyCheck(
            ApiKeyStatus.Unreachable,
            "prdb is rate-limiting this key at the moment, so it could not be checked. Wait a minute "
            + "and try again."),

        >= 500 => new ApiKeyCheck(
            ApiKeyStatus.Unreachable,
            "prdb is having trouble answering, so the key was not checked. Try again in a moment."),

        _ => new ApiKeyCheck(
            ApiKeyStatus.Unreachable,
            $"prdb answered the check with {status}, which the tool did not expect. The key was not "
            + "checked."),
    };
}

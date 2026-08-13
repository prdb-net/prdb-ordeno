using Microsoft.Extensions.Logging;
using Microsoft.Kiota.Abstractions;

using Prdb.Hashing;
using Prdb.Ordeno.Core.Identification;
using Prdb.Ordeno.Infrastructure.Configuration;
using Prdb.Sdk;
using Prdb.Sdk.Generated.Models;

namespace Prdb.Ordeno.Infrastructure.Identification;

/// <summary>
/// Asks prdb what a batch of files is, over <c>POST /videos/identify</c>.
/// </summary>
/// <remarks>
/// <para>
/// One request per batch of up to two hundred, with the whole ladder walked on
/// prdb's side — ADR 0001. Nothing here reimplements a rung, and nothing here
/// turns a refusal into a guess: every failure comes back as a status the caller
/// has to stop on.
/// </para>
/// <para>
/// The client is built per call through <see cref="PrdbClientFactory"/>, never by
/// hand, because that is what refuses a redirect to another origin while
/// <c>X-Api-Key</c> is on the request. The key comes from the database and can
/// change while the container runs, so it is passed in rather than captured.
/// </para>
/// </remarks>
public sealed class PrdbVideoIdentification(
    IHttpMessageHandlerFactory handlers,
    ILogger<PrdbVideoIdentification> logger)
    : IVideoIdentification
{
    /// <summary>
    /// Two hundred files with their video documents is a large answer over a
    /// domestic connection, and nobody is waiting in front of it — this runs on
    /// a timer. Long enough to finish, short enough that a stalled connection is
    /// noticed within one interval.
    /// </summary>
    private static readonly TimeSpan BatchTimeout = TimeSpan.FromSeconds(90);

    /// <summary>How long to wait when prdb refuses without saying how long.</summary>
    private static readonly TimeSpan DefaultBackoff = TimeSpan.FromMinutes(15);

    public async Task<IdentificationAnswer> IdentifyAsync(
        string apiKey,
        IReadOnlyList<FileToIdentify> files,
        CancellationToken cancellationToken = default)
    {
        if (files.Count == 0)
        {
            return IdentificationAnswer.From([]);
        }

        if (files.Count > IdentificationSchedule.MaxBatch)
        {
            throw new ArgumentException(
                $"The endpoint takes {IdentificationSchedule.MaxBatch} files at a time, not {files.Count}.",
                nameof(files));
        }

        var client = PrdbClientFactory.Create(
            apiKey,
            transport: handlers.CreateHandler(PrdbTransport.HttpClientName),
            // The SDK's own retry would cost us the error body, which is the
            // difference between "the key has no API plan" and a shrug. Coming
            // back on the next tick is the retry this tool wants anyway.
            retry: PrdbRetryOptions.Disabled,
            timeout: BatchTimeout);

        var request = new IdentifyVideosRequest
        {
            // The answer has to be readable months later on a screen, and asking
            // again for two hundred titles would be a second request against the
            // quota every time somebody opens the page — or an empty screen
            // whenever prdb is down.
            IncludeVideoDetails = true,
            Files = [.. files.Select(Sent)],
        };

        var limits = new RateLimitOption();

        try
        {
            var response = await client.Videos.Identify.PostAsync(
                request,
                configuration => configuration.Options.Add(limits),
                cancellationToken);

            if (response?.Results is null)
            {
                logger.LogWarning("prdb answered the identification without any results in it.");

                return IdentificationAnswer.Stopped(
                    IdentificationStatus.Unreachable,
                    "prdb answered something the tool did not understand, so nothing was identified.");
            }

            return IdentificationAnswer.From([.. response.Results.Select(Read)], Reading(limits));
        }
        catch (ApiException exception)
        {
            return Refused(exception, limits);
        }
        catch (CrossOriginRedirectException exception)
        {
            logger.LogError(
                exception,
                "The identification was redirected off the prdb host and stopped before the key was sent.");

            return IdentificationAnswer.Stopped(
                IdentificationStatus.Unreachable,
                "Something answered for prdb and tried to send the tool somewhere else. Nothing was "
                + "identified, and the API key was not handed over — a proxy between this container "
                + "and api.prdb.net is the usual explanation.",
                DefaultBackoff);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "prdb could not be reached to identify {Count} files.", files.Count);

            return IdentificationAnswer.Stopped(
                IdentificationStatus.Unreachable,
                "prdb could not be reached, so nothing was identified. The files are untouched and "
                + "the tool tries again in a few minutes.");
        }
    }

    private static IdentifyVideoFileDto Sent(FileToIdentify file) => new()
    {
        Ref = file.Ref,
        Filename = file.FileName,
        Filesize = file.SizeBytes,

        // Uppercase on the way out. prdb normalises either casing, but sending
        // the form it stores keeps the match on its side byte for byte rather
        // than a favour granted by a collation setting.
        OsHash = file.OsHash is null ? null : FileHashes.ForPrdbLookup(file.OsHash),
        PHash = file.PerceptualHash is null ? null : FileHashes.ForPrdbLookup(file.PerceptualHash),
    };

    private static RecognisedFile Read(IdentifyVideoResultDto result) => new(
        result.Ref ?? string.Empty,
        Confidence(result.Confidence),
        Rung(result.MatchedBy),
        result.VideoId,
        result.Video?.Title,
        result.Video?.ReleaseDate is { } date ? (DateOnly)date : null,

        // The site comes with the video when one was named, and on its own when
        // the site rung was as far as the ladder got.
        result.Video?.Site?.Id ?? result.Site?.Id,
        result.Video?.Site?.Title ?? result.Site?.Title,
        [.. (result.Candidates ?? []).OfType<Guid>()]);

    /// <summary>
    /// The numbers the endpoint documents, read one by one rather than cast. A
    /// value this build has no name for is a newer prdb, and it must not become
    /// an exception in a container somebody left running.
    /// </summary>
    private static MatchConfidence Confidence(int? confidence) => confidence switch
    {
        1 => MatchConfidence.Partial,
        2 => MatchConfidence.Probable,
        3 => MatchConfidence.Strong,
        4 => MatchConfidence.Exact,
        5 => MatchConfidence.Ambiguous,
        _ => MatchConfidence.None,
    };

    private static MatchRung? Rung(int? matchedBy) => matchedBy switch
    {
        0 => MatchRung.OsHash,
        1 => MatchRung.PerceptualHash,
        2 => MatchRung.FileName,
        3 => MatchRung.ReleaseName,
        4 => MatchRung.Site,
        _ => null,
    };

    private static RateLimitReading? Reading(RateLimitOption limits) =>
        limits.Hour is { } hour
            ? new RateLimitReading(hour.Remaining, TimeSpan.FromSeconds(hour.ResetInSeconds))
            : null;

    private IdentificationAnswer Refused(ApiException exception, RateLimitOption limits)
    {
        logger.LogWarning(
            "prdb refused the identification with status {Status}.",
            exception.ResponseStatusCode);

        return exception.ResponseStatusCode switch
        {
            401 => IdentificationAnswer.Stopped(
                IdentificationStatus.Refused,
                "prdb does not know the stored API key any more, so nothing is being identified. "
                + "Put the current key in under Settings.",
                DefaultBackoff),

            403 => IdentificationAnswer.Stopped(
                IdentificationStatus.Refused,
                "prdb knows the API key but will not let it in. Check that the account it belongs "
                + "to still has an active subscription.",
                DefaultBackoff),

            429 => IdentificationAnswer.Stopped(
                IdentificationStatus.RateLimited,
                "prdb's quota for this key is spent for now. Nothing is wrong — the tool waits and "
                + "carries on with the rest afterwards.",
                Wait(limits)),

            >= 500 => IdentificationAnswer.Stopped(
                IdentificationStatus.Unreachable,
                "prdb is having trouble answering, so nothing was identified. The tool tries again "
                + "in a few minutes."),

            _ => IdentificationAnswer.Stopped(
                IdentificationStatus.Unreachable,
                $"prdb answered the identification with {exception.ResponseStatusCode}, which the "
                + "tool did not expect. Nothing was identified.",
                DefaultBackoff),
        };
    }

    /// <summary>
    /// How long a refused request asked to be left alone. A <c>429</c> carries
    /// only the window that refused it, so whichever of the two is set is the one
    /// that matters.
    /// </summary>
    private static TimeSpan Wait(RateLimitOption limits)
    {
        var seconds = limits.Hour?.ResetInSeconds ?? limits.Month?.ResetInSeconds;

        return seconds is > 0 ? TimeSpan.FromSeconds(seconds.Value) : DefaultBackoff;
    }
}

using System.Net;

namespace Prdb.Ordeno.Host.Tests;

/// <summary>
/// prdb, as far as a test is concerned: the socket the SDK would have opened,
/// answering the way the real API does.
/// </summary>
/// <remarks>
/// This is not a stand-in for anything the application composes — ADR 0015 rules
/// that out and it is the reason these tests are worth having. The SDK, the
/// client the key check builds, and the mapping from a status code to a sentence
/// the user reads all run for real; what is replaced is the network under them,
/// because a test that talks to prdb is a test that fails when a subscription
/// lapses.
/// </remarks>
internal sealed class FakePrdb(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
{
    private const string ApiKeyHeader = "X-Api-Key";

    /// <summary>
    /// How often the tool asked. A key that was stored without the tool ever
    /// asking would satisfy every other assertion in these tests.
    /// </summary>
    public int Calls { get; private set; }

    /// <summary>Answers as prdb does for the one key it knows, and refuses every other.</summary>
    public static FakePrdb Accepting(string apiKey) => new(request =>
        request.Headers.TryGetValues(ApiKeyHeader, out var values)
        && values.Contains(apiKey, StringComparer.Ordinal)
            ? Json(HttpStatusCode.OK, """{"userHash":"a-stable-hash","activeSubscriptions":[]}""")
            : Unauthorized());

    /// <summary>Answers the way prdb does for a key it has never seen.</summary>
    public static FakePrdb Refusing() => new(_ => Unauthorized());

    /// <summary>prdb is not there at all: no DNS, no route, a proxy that swallowed it.</summary>
    public static FakePrdb Unreachable() =>
        new(_ => throw new HttpRequestException("Name or service not known."));

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Calls++;

        return Task.FromResult(answer(request));
    }

    private static HttpResponseMessage Unauthorized() => Json(
        HttpStatusCode.Unauthorized,
        """{"type":"about:blank","title":"Unauthorized","status":401,"detail":"Unknown API key."}""");

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
    };
}

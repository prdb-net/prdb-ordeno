using System.Net;

namespace Prdb.Ordeno.Host.Tests;

/// <summary>
/// A media server, as far as these tests are concerned: the socket the client
/// would have opened, answering the routes section 9 measured.
/// </summary>
/// <remarks>
/// As with <see cref="FakePrdb"/>, what is replaced is the network and nothing
/// above it — the client, the connection test's verdict and the words it puts on
/// the screen all run for real.
/// </remarks>
internal sealed class FakeMediaServer(Func<HttpRequestMessage, HttpResponseMessage> answer)
    : HttpMessageHandler
{
    public int Calls { get; private set; }

    /// <summary>
    /// A default installation: it answers, it accepts the one key it knows, and
    /// its library is empty because nothing has been filed into it yet.
    /// </summary>
    public static FakeMediaServer Accepting(string apiKey, string releaseDateFormat = "yyyy-MM-dd") =>
        new(request => request.Headers.Authorization?.Parameter == $"Token=\"{apiKey}\""
            ? Route(request, releaseDateFormat)
            : new HttpResponseMessage(HttpStatusCode.Unauthorized));

    /// <summary>A key that was revoked, or one that was never right.</summary>
    public static FakeMediaServer Refusing() =>
        new(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

    /// <summary>Switched off, on another network, behind a proxy that swallowed it.</summary>
    public static FakeMediaServer Unreachable() =>
        new(_ => throw new HttpRequestException("Connection refused."));

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Calls++;

        return Task.FromResult(answer(request));
    }

    private static HttpResponseMessage Route(HttpRequestMessage request, string releaseDateFormat)
    {
        var path = request.RequestUri!.AbsolutePath;

        if (path.EndsWith("/System/Info", StringComparison.Ordinal))
        {
            return Json("""{"ProductName":"Jellyfin Server","Version":"10.11.11"}""");
        }

        if (path.EndsWith("/System/Configuration/xbmcmetadata", StringComparison.Ordinal))
        {
            return Json($$"""{"ReleaseDateFormat":"{{releaseDateFormat}}"}""");
        }

        if (path.EndsWith("/Items", StringComparison.Ordinal))
        {
            return Json("""{"Items":[],"TotalRecordCount":0}""");
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
}

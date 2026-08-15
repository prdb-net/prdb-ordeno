using System.Net;
using System.Text.Json;

namespace Prdb.Ordeno.Infrastructure.Tests.MediaServer;

/// <summary>
/// A media server, as far as a test is concerned: the socket the client would
/// have opened, answering the four routes section 9 measured.
/// </summary>
/// <remarks>
/// What is replaced is the network and nothing above it. The client, the mapping
/// from a status code to a sentence somebody reads, and the tail match that
/// turns a path into an item id all run for real — a test that talks to a
/// Jellyfin is a test that fails when nobody has one running.
/// </remarks>
internal sealed class FakeJellyfin(string apiKey) : HttpMessageHandler
{
    private readonly List<string> refreshed = [];

    /// <summary>What the server holds, as it sees the paths — not as the tool does.</summary>
    public List<(string Id, string Path)> Items { get; } = [];

    /// <summary>
    /// The setting that decides whether every date the tool writes is read or
    /// silently dropped, and the reason ADR 0018 exists at all.
    /// </summary>
    public string ReleaseDateFormat { get; set; } = "yyyy-MM-dd";

    /// <summary>
    /// The metadata settings route answers 404 — half a server behind a proxy, or
    /// a version that keeps the setting somewhere else.
    /// </summary>
    public bool HidesTheDateFormat { get; set; }

    /// <summary>The items the tool asked to be read again, in order.</summary>
    public IReadOnlyList<string> Refreshed => refreshed;

    /// <summary>How often the tool enumerated the library. Once per run is the price ADR 0018 accepted.</summary>
    public int Enumerations { get; private set; }

    /// <summary>Answers everything with this status instead, for the failures worth a sentence.</summary>
    public HttpStatusCode? Fails { get; set; }

    /// <summary>Answers with a redirect somewhere else, carrying nothing.</summary>
    public Uri? Redirect { get; set; }

    /// <summary>Nothing is listening: no DNS, no route, a proxy that swallowed it.</summary>
    public bool Down { get; set; }

    /// <summary>
    /// Every request the tool made, with the credential it carried. A test that
    /// asserts the key was not sent needs to be able to see that it was not.
    /// </summary>
    public List<(string Path, string? Authorization)> Requests { get; } = [];

    public void Holds(string path) =>
        Items.Add(($"item-{Items.Count + 1}", path));

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;

        Requests.Add((uri.PathAndQuery, request.Headers.Authorization?.ToString()));

        if (Down)
        {
            throw new HttpRequestException("Name or service not known.");
        }

        if (Redirect is { } elsewhere)
        {
            var moved = new HttpResponseMessage(HttpStatusCode.Found);
            moved.Headers.Location = elsewhere;

            return Task.FromResult(moved);
        }

        if (Fails is { } status)
        {
            return Task.FromResult(new HttpResponseMessage(status));
        }

        if (request.Headers.Authorization?.Parameter != $"Token=\"{apiKey}\"")
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        }

        return Task.FromResult(Answer(request.Method, uri));
    }

    private HttpResponseMessage Answer(HttpMethod method, Uri uri)
    {
        var path = uri.AbsolutePath;

        if (method == HttpMethod.Post && path.EndsWith("/Refresh", StringComparison.Ordinal))
        {
            // Items/{id}/Refresh — the id is the segment before the last.
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            refreshed.Add(Uri.UnescapeDataString(segments[^2]));

            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        if (path.EndsWith("/System/Info", StringComparison.Ordinal))
        {
            return Json("""{"ProductName":"Jellyfin Server","Version":"10.11.11","ServerName":"nas"}""");
        }

        if (path.EndsWith("/System/Configuration/xbmcmetadata", StringComparison.Ordinal))
        {
            return HidesTheDateFormat
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : Json(JsonSerializer.Serialize(new { ReleaseDateFormat, EnablePathSubstitution = true }));
        }

        if (path.EndsWith("/Items", StringComparison.Ordinal))
        {
            Enumerations++;

            return Json(JsonSerializer.Serialize(new
            {
                Items = Items.Select(item => new { item.Id, item.Path }),
                TotalRecordCount = Items.Count,
            }));
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
}

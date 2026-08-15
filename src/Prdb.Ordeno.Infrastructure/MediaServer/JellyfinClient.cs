using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Logging;

using Prdb.Ordeno.Core.MediaServer;

namespace Prdb.Ordeno.Infrastructure.MediaServer;

/// <summary>
/// Jellyfin, over the four calls ADR 0018 needs and no others.
/// </summary>
/// <remarks>
/// <para>
/// Every route here was measured against Jellyfin 10.11.11 by
/// <c>docs/jellyfin-probe/probe-itemid.sh</c>, and section 9 of the layout
/// document holds the result. Three of them are there because the obvious
/// alternative does not work: the library is enumerated because <c>path=</c> is
/// accepted and ignored, an item is refreshed by id because a path report obeys
/// the tolerance window it exists to beat, and the date format is read because
/// nothing else in either product would ever mention it.
/// </para>
/// <para>
/// Nothing here throws at a caller. A server that is down, moved or answering
/// with a stale key is a reply that says so, because the filing path calls this
/// and the filing path carries on regardless.
/// </para>
/// </remarks>
public sealed class JellyfinClient(IHttpClientFactory clients, ILogger<JellyfinClient> logger)
    : IMediaServerClient
{
    /// <summary>What to call it when it answers without saying what it is.</summary>
    private const string ProductName = "The media server";

    /// <summary>
    /// Somebody is waiting in front of a form. A minute of waiting is worse than
    /// being told the server did not answer.
    /// </summary>
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Long enough for a library of ten thousand scenes, which section 9 prices
    /// at a few megabytes. Nobody is waiting for this one: it happens after a
    /// filing run has already finished.
    /// </summary>
    private static readonly TimeSpan LibraryTimeout = TimeSpan.FromMinutes(2);

    public async Task<MediaServerReply<MediaServerFacts>> ExamineAsync(
        MediaServerConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var system = await GetAsync<SystemInfo>(connection, "System/Info", CheckTimeout, cancellationToken);

        if (!system.Answered)
        {
            return new MediaServerReply<MediaServerFacts>(system.Reach, null, system.Problem);
        }

        // Asked separately, and a failure here is not a failure of the check: a
        // server that answers everything else and not this one is still worth
        // talking to, and the verdict says the format was not read rather than
        // claiming it is right.
        var metadata = await GetAsync<XbmcMetadataOptions>(
            connection,
            "System/Configuration/xbmcmetadata",
            CheckTimeout,
            cancellationToken);

        return MediaServerReply<MediaServerFacts>.Of(new MediaServerFacts(
            string.IsNullOrWhiteSpace(system.Value!.ProductName) ? ProductName : system.Value.ProductName,
            string.IsNullOrWhiteSpace(system.Value.Version) ? "(no version)" : system.Value.Version,
            metadata.Value?.ReleaseDateFormat));
    }

    public async Task<MediaServerReply<IReadOnlyList<MediaServerItem>>> ItemsAsync(
        MediaServerConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        // No user id: section 9 measured that an API key reaches this without
        // one, which is what keeps the connection to a URL and a key.
        var answer = await GetAsync<ItemsResult>(
            connection,
            "Items?recursive=true&includeItemTypes=Movie&fields=Path",
            LibraryTimeout,
            cancellationToken);

        if (!answer.Answered)
        {
            return new MediaServerReply<IReadOnlyList<MediaServerItem>>(answer.Reach, null, answer.Problem);
        }

        // An item with no path is one the server holds somewhere this tool cannot
        // recognise — a playlist, a collection — and it is left out rather than
        // carried along as an entry nothing can ever match.
        IReadOnlyList<MediaServerItem> items =
        [
            .. (answer.Value!.Items ?? [])
                .Where(item => !string.IsNullOrEmpty(item.Id) && !string.IsNullOrEmpty(item.Path))
                .Select(item => new MediaServerItem(item.Id!, item.Path!)),
        ];

        return MediaServerReply<IReadOnlyList<MediaServerItem>>.Of(items);
    }

    public async Task<MediaServerReply> RefreshAsync(
        MediaServerConnection connection,
        string itemId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        // FullRefresh and replaceAllMetadata: the sidecar is the truth, and a
        // lesser refresh keeps what the item already carries.
        var path = "Items/"
            + Uri.EscapeDataString(itemId)
            + "/Refresh?metadataRefreshMode=FullRefresh&replaceAllMetadata=true";

        return await SendAsync(connection, HttpMethod.Post, path, LibraryTimeout, cancellationToken);
    }

    private async Task<MediaServerReply<T>> GetAsync<T>(
        MediaServerConnection connection,
        string path,
        TimeSpan timeout,
        CancellationToken cancellationToken)
        where T : class
    {
        using var client = Client(timeout);

        try
        {
            using var request = Request(connection, HttpMethod.Get, path);
            using var response = await client.SendAsync(request, cancellationToken);

            if (Rejected(connection, response) is { } problem)
            {
                return new MediaServerReply<T>(problem.Reach, null, problem.Problem);
            }

            var value = await response.Content.ReadFromJsonAsync<T>(cancellationToken);

            return value is null
                ? Unreadable<T>(connection)
                : MediaServerReply<T>.Of(value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return new MediaServerReply<T>(
                MediaServerReach.Unreachable,
                null,
                Unreachable(connection, exception));
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or NotSupportedException)
        {
            logger.LogWarning(exception, "The media server answered with something unreadable.");

            return Unreadable<T>(connection);
        }
    }

    private async Task<MediaServerReply> SendAsync(
        MediaServerConnection connection,
        HttpMethod method,
        string path,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var client = Client(timeout);

        try
        {
            using var request = Request(connection, method, path);
            using var response = await client.SendAsync(request, cancellationToken);

            return Rejected(connection, response) ?? new MediaServerReply(MediaServerReach.Answered);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return new MediaServerReply(MediaServerReach.Unreachable, Unreachable(connection, exception));
        }
    }

    private HttpClient Client(TimeSpan timeout)
    {
        var client = clients.CreateClient(MediaServerTransport.HttpClientName);
        client.Timeout = timeout;

        return client;
    }

    private static HttpRequestMessage Request(
        MediaServerConnection connection,
        HttpMethod method,
        string path)
    {
        var request = new HttpRequestMessage(method, connection.Endpoint(path));

        // The scheme a plain API key authenticates with. No user name, no
        // password and no user id — section 9 measured that this reaches every
        // endpoint above.
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "MediaBrowser",
            $"Token=\"{connection.ApiKey}\"");

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        return request;
    }

    /// <summary>
    /// The answer that is not one, in words. <c>null</c> when the server answered
    /// properly and the body is worth reading.
    /// </summary>
    private static MediaServerReply? Rejected(MediaServerConnection connection, HttpResponseMessage response) =>
        (int)response.StatusCode switch
        {
            >= 200 and < 300 => null,

            401 or 403 => new MediaServerReply(
                MediaServerReach.Refused,
                "The media server did not accept this API key. Make a new one in its dashboard and "
                + "paste that — it is an API key, not the password anybody signs in with."),

            // Not followed, deliberately: the key is on the request, and every
            // HTTP stack strips Authorization on a cross-origin redirect while
            // this scheme carries the credential in exactly that header.
            >= 300 and < 400 => new MediaServerReply(
                MediaServerReach.Unreachable,
                $"{connection.Address} redirected the tool to "
                + $"{response.Headers.Location?.ToString() ?? "somewhere else"} instead of answering. "
                + "The key was not sent on. Use the address it redirects to."),

            404 => new MediaServerReply(
                MediaServerReach.Unreachable,
                $"Something answered at {connection.Address} but there is no media server API there. "
                + "The address is the server's own — if it was copied out of a browser, everything "
                + "from '/web' onwards belongs to the page rather than to the server. A proxy that "
                + "puts the server on a path is the one case where a path belongs here."),

            >= 500 => new MediaServerReply(
                MediaServerReach.Unreachable,
                $"The media server answered with {(int)response.StatusCode} and could not be asked "
                + "anything. Its own log will say why."),

            var status => new MediaServerReply(
                MediaServerReach.Unreachable,
                $"The media server answered with {status.ToString(CultureInfo.InvariantCulture)}, "
                + "which the tool did not expect."),
        };

    private static MediaServerReply<T> Unreadable<T>(MediaServerConnection connection)
        where T : class =>
        MediaServerReply<T>.Unreachable(
            $"Something answered at {connection.Address}, and it is not a media server this tool "
            + "understands. Check the address and the port.");

    private string Unreachable(MediaServerConnection connection, Exception exception)
    {
        logger.LogWarning(exception, "The media server at {Address} could not be reached.", connection.Address);

        return $"{connection.Address} could not be reached: {exception.Message} The container has to "
            + "be able to reach the media server itself — a name that only resolves on your desktop "
            + "does not resolve in here.";
    }

    /// <summary>What <c>GET /System/Info</c> answers, as far as this is concerned.</summary>
    private sealed record SystemInfo(
        [property: JsonPropertyName("ProductName")] string? ProductName,
        [property: JsonPropertyName("Version")] string? Version);

    /// <summary>
    /// The metadata settings, whose one interesting field decides whether every
    /// date the tool writes is read or silently dropped — section 4.
    /// </summary>
    private sealed record XbmcMetadataOptions(
        [property: JsonPropertyName("ReleaseDateFormat")] string? ReleaseDateFormat);

    private sealed record ItemsResult(
        [property: JsonPropertyName("Items")] IReadOnlyList<ItemsResult.Item>? Items)
    {
        public sealed record Item(
            [property: JsonPropertyName("Id")] string? Id,
            [property: JsonPropertyName("Path")] string? Path);
    }
}

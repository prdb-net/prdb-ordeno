using System.Net;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Prdb.Ordeno.Core.MediaServer;
using Prdb.Ordeno.Infrastructure.MediaServer;

using Xunit;

namespace Prdb.Ordeno.Infrastructure.Tests.MediaServer;

/// <summary>
/// The four routes of section 9, over a socket that answers the way Jellyfin
/// 10.11.11 did when they were measured.
/// </summary>
public sealed class JellyfinClientTests : IDisposable
{
    private const string Key = "0123456789abcdef";

    private readonly FakeJellyfin server = new(Key);
    private readonly ServiceProvider services;

    public JellyfinClientTests()
    {
        var collection = new ServiceCollection();
        collection.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        collection.AddOrdenoMediaServer();
        collection
            .AddHttpClient(MediaServerTransport.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => server);

        services = collection.BuildServiceProvider();
    }

    public void Dispose()
    {
        services.Dispose();
        server.Dispose();
    }

    private IMediaServerClient Client => services.GetRequiredService<IMediaServerClient>();

    private static MediaServerConnection At(string url = "http://nas:8096") =>
        MediaServerConnection.From(url, Key, out _)!;

    [Fact]
    public async Task A_server_says_what_it_is_and_how_it_will_read_the_dates()
    {
        var facts = await Client.ExamineAsync(At());

        Assert.True(facts.Answered);
        Assert.Equal("Jellyfin Server", facts.Value!.Name);
        Assert.Equal("10.11.11", facts.Value.Version);
        Assert.Equal("yyyy-MM-dd", facts.Value.ReleaseDateFormat);
    }

    /// <summary>
    /// A plain API key and no user account, which is what keeps the connection to
    /// two fields — section 9 measured that this reaches every route here.
    /// </summary>
    [Fact]
    public async Task The_key_travels_as_the_scheme_the_server_authenticates_with()
    {
        await Client.ExamineAsync(At());

        Assert.All(server.Requests, request =>
            Assert.Equal($"MediaBrowser Token=\"{Key}\"", request.Authorization));
    }

    [Fact]
    public async Task A_key_the_server_will_not_have_is_a_refusal_and_not_a_silence()
    {
        var facts = await Client.ExamineAsync(MediaServerConnection.From("http://nas:8096", "wrong", out _)!);

        Assert.Equal(MediaServerReach.Refused, facts.Reach);
        Assert.Contains("did not accept this API key", facts.Problem!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_server_that_is_not_there_is_a_silence_and_not_a_refusal()
    {
        server.Down = true;

        var facts = await Client.ExamineAsync(At());

        Assert.Equal(MediaServerReach.Unreachable, facts.Reach);
        Assert.Contains("could not be reached", facts.Problem!, StringComparison.Ordinal);
    }

    /// <summary>
    /// The key is in <c>Authorization</c>, which is the header every HTTP stack
    /// strips on a cross-origin redirect and this scheme carries the credential
    /// in. So the redirect is reported rather than followed, and the address it
    /// points at is put in front of the user.
    /// </summary>
    [Fact]
    public async Task A_redirect_is_answered_with_words_rather_than_followed()
    {
        server.Redirect = new Uri("https://somewhere.example/");

        var facts = await Client.ExamineAsync(At());

        Assert.Equal(MediaServerReach.Unreachable, facts.Reach);
        Assert.Contains("https://somewhere.example/", facts.Problem!, StringComparison.Ordinal);
        Assert.Single(server.Requests);
    }

    /// <summary>
    /// The date format is asked for separately, and a server that answers
    /// everything else and not that one is still worth talking to. What must not
    /// happen is the tool claiming the format is right because it did not look.
    /// </summary>
    [Fact]
    public async Task A_date_format_that_could_not_be_read_is_not_claimed_to_be_right()
    {
        server.HidesTheDateFormat = true;

        var facts = await Client.ExamineAsync(At());

        Assert.True(facts.Answered);
        Assert.Equal("Jellyfin Server", facts.Value!.Name);
        Assert.Null(facts.Value.ReleaseDateFormat);
    }

    [Fact]
    public async Task The_library_comes_back_as_items_with_the_paths_the_server_holds_them_at()
    {
        server.Holds("/media/movies/Site/Scene/Scene.mkv");
        server.Holds("/media/movies/Site/Other/Other.mkv");

        var items = await Client.ItemsAsync(At());

        Assert.True(items.Answered);
        Assert.Equal(2, items.Value!.Count);
        Assert.Equal("/media/movies/Site/Scene/Scene.mkv", items.Value[0].Path);
    }

    /// <summary>
    /// Something the server holds without a path on disk — a collection, a
    /// playlist — is left out rather than carried along as an entry no tail can
    /// ever match.
    /// </summary>
    [Fact]
    public async Task An_item_with_no_path_is_left_out()
    {
        server.Items.Add(("no-path", string.Empty));
        server.Holds("/media/movies/Site/Scene/Scene.mkv");

        var items = await Client.ItemsAsync(At());

        Assert.Equal("/media/movies/Site/Scene/Scene.mkv", Assert.Single(items.Value!).Path);
    }

    /// <summary>
    /// FullRefresh and replaceAllMetadata: the sidecar next to the file is the
    /// truth, and a lesser refresh keeps whatever the item is already carrying.
    /// </summary>
    [Fact]
    public async Task An_item_is_read_again_whole()
    {
        var refreshed = await Client.RefreshAsync(At(), "abc123");

        Assert.True(refreshed.Answered);
        Assert.Equal("abc123", Assert.Single(server.Refreshed));

        var request = server.Requests[^1].Path;
        Assert.Contains("metadataRefreshMode=FullRefresh", request, StringComparison.Ordinal);
        Assert.Contains("replaceAllMetadata=true", request, StringComparison.Ordinal);
    }

    /// <summary>
    /// A server behind a proxy on a path keeps that path in front of every route,
    /// or the request lands on the proxy's own 404 and reads as a wrong key.
    /// </summary>
    [Fact]
    public async Task A_server_on_a_path_keeps_it()
    {
        await Client.ExamineAsync(At("http://nas/jellyfin"));

        Assert.StartsWith("/jellyfin/System/Info", server.Requests[0].Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_server_having_trouble_says_so_rather_than_looking_like_a_wrong_key()
    {
        server.Fails = HttpStatusCode.BadGateway;

        var facts = await Client.ExamineAsync(At());

        Assert.Equal(MediaServerReach.Unreachable, facts.Reach);
        Assert.Contains("502", facts.Problem!, StringComparison.Ordinal);
    }
}

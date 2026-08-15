using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Prdb.Ordeno.Core.MediaServer;
using Prdb.Ordeno.Infrastructure.MediaServer;
using Prdb.Ordeno.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Ordeno.Infrastructure.Tests.MediaServer;

/// <summary>
/// The optional connection against a real database and a socket that answers
/// like Jellyfin: what the connection test proves, and what a finished filing
/// run tells the server afterwards.
/// </summary>
public sealed class MediaServerServiceTests : IAsyncLifetime
{
    private const string Key = "0123456789abcdef";
    private const string Library = "/library";

    private const string Scene = "/Example Studio/Example Studio - 2025-11-15 - A Scene";

    private readonly TempDirectory directory = new();
    private readonly FakeJellyfin server = new(Key);

    private ServiceProvider services = null!;

    public async Task InitializeAsync()
    {
        var collection = new ServiceCollection();
        collection.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        collection.AddOrdenoPersistence(directory.Combine("data"));
        collection.AddOrdenoMediaServer();
        collection
            .AddHttpClient(MediaServerTransport.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => server);

        services = collection.BuildServiceProvider();

        await services.PrepareOrdenoDatabaseAsync();
    }

    public async Task DisposeAsync()
    {
        await services.DisposeAsync();
        server.Dispose();
        directory.Dispose();
    }

    /// <summary>
    /// The proof ADR 0018 asks the test for: not that the server answered, but
    /// that it holds something this tool filed — and the mount prefix that fact
    /// yields, which nobody configured and nothing else would ever mention.
    /// </summary>
    [Fact]
    public async Task A_server_holding_something_the_tool_filed_proves_the_path_substitution()
    {
        await ConfiguredAsync();
        await FiledAsync("A Scene.mkv");
        server.Holds($"/media/movies{Scene}/A Scene.mkv");

        var check = await CheckAsync();

        Assert.Equal(MediaServerCheckStatus.Working, check.Status);
        Assert.Contains("/library here is /media/movies there", check.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The state that looks fine and does nothing: a server that answers, a key
    /// that works, and a library pointed somewhere else entirely.
    /// </summary>
    [Fact]
    public async Task A_server_that_holds_none_of_it_is_not_a_working_connection()
    {
        await ConfiguredAsync();
        await FiledAsync("A Scene.mkv");
        server.Holds("/media/something-else/Other/Other.mkv");

        var check = await CheckAsync();

        Assert.Equal(MediaServerCheckStatus.Unmatched, check.Status);
    }

    [Fact]
    public async Task Nothing_filed_yet_is_the_ordinary_state_during_setup()
    {
        await ConfiguredAsync();

        Assert.Equal(MediaServerCheckStatus.Unproven, (await CheckAsync()).Status);
    }

    [Fact]
    public async Task A_date_format_the_server_would_discard_dates_by_is_the_headline()
    {
        await ConfiguredAsync();
        await FiledAsync("A Scene.mkv");
        server.Holds($"/media/movies{Scene}/A Scene.mkv");
        server.ReleaseDateFormat = "dd/MM/yyyy";

        Assert.Equal(MediaServerCheckStatus.DatesDiscarded, (await CheckAsync()).Status);
    }

    /// <summary>
    /// What a finished filing run does with the connection: one enumeration for
    /// the batch, and a refresh for each file the server actually holds.
    /// </summary>
    [Fact]
    public async Task A_finished_run_has_the_server_read_the_sidecars_it_just_wrote()
    {
        await ConfiguredAsync();
        server.Holds($"/media/movies{Scene}/A Scene.mkv");

        var refreshed = await RefreshAsync(
            $"{Library}{Scene}/A Scene.mkv",
            $"{Library}{Scene}/A Scene - [2160p].mkv");

        Assert.Equal(1, refreshed.Told);
        Assert.Equal("item-1", Assert.Single(server.Refreshed));

        // The one the server has not scanned yet is passed over in silence: the
        // scan that finds it reads the sidecar sitting next to it.
        Assert.Equal(1, refreshed.Missed);
        Assert.Equal(1, server.Enumerations);
    }

    /// <summary>
    /// The rule the whole design turns on. A server that is down, moved or
    /// answering with a stale key changes nothing about what was filed, and
    /// costs nothing but a line in the log.
    /// </summary>
    [Fact]
    public async Task A_server_that_is_down_is_not_a_failed_filing()
    {
        await ConfiguredAsync();
        server.Down = true;

        var refreshed = await RefreshAsync($"{Library}{Scene}/A Scene.mkv");

        Assert.Equal(0, refreshed.Told);
        Assert.NotNull(refreshed.Problem);
    }

    /// <summary>
    /// And the ordinary installation, which left both fields blank: nothing is
    /// asked of anything, and nothing anywhere reports a problem.
    /// </summary>
    [Fact]
    public async Task No_connection_means_no_request_and_no_complaint()
    {
        await ConfiguredAsync(connected: false);

        var refreshed = await RefreshAsync($"{Library}{Scene}/A Scene.mkv");

        Assert.Equal(0, refreshed.Told);
        Assert.Null(refreshed.Problem);
        Assert.Empty(server.Requests);
    }

    private async Task ConfiguredAsync(bool connected = true)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdenoDbContext>();

        var configuration = await context.Configuration.SingleAsync();
        configuration.TargetDirectory = Library;
        configuration.MediaServerUrl = connected ? "http://nas:8096/" : null;
        configuration.MediaServerApiKey = connected ? Key : null;

        await context.SaveChangesAsync();
    }

    private async Task FiledAsync(string fileName)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdenoDbContext>();

        context.FiledVideos.Add(new FiledVideo
        {
            VideoId = Guid.NewGuid(),
            LibraryRoot = Library,
            Directory = Library + Scene,
            FileName = fileName,
            QualityLabel = "1080p",
            FiledAt = DateTimeOffset.UnixEpoch,
        });

        await context.SaveChangesAsync();
    }

    private async Task<MediaServerCheck> CheckAsync()
    {
        await using var scope = services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<MediaServerService>();

        return await service.CheckAsync((await service.ConnectionAsync())!);
    }

    private async Task<MediaServerRefresh> RefreshAsync(params string[] paths)
    {
        await using var scope = services.CreateAsyncScope();

        return await scope.ServiceProvider
            .GetRequiredService<MediaServerService>()
            .RefreshAsync(paths);
    }
}

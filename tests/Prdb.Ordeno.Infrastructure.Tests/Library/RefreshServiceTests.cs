using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Prdb.Ordeno.Core.Configuration;
using Prdb.Ordeno.Core.History;
using Prdb.Ordeno.Core.Identification;
using Prdb.Ordeno.Core.Library;
using Prdb.Ordeno.Core.Review;
using Prdb.Ordeno.Infrastructure.Library;
using Prdb.Ordeno.Infrastructure.Persistence;
using Prdb.Ordeno.Infrastructure.Tests.Review;
using Prdb.Ordeno.Infrastructure.Tests.Scanning;

using Xunit;

namespace Prdb.Ordeno.Infrastructure.Tests.Library;

/// <summary>
/// ADR 0032 and ADR 0033 against a real filesystem and a real SQLite file: a
/// library the tool filed, a title prdb has corrected since, and what a run does
/// about it.
/// </summary>
/// <remarks>
/// Only prdb's endpoint and the CDN socket are replaced. The scenes are written
/// as rows and directories rather than by running a filing, because what is
/// under test starts where filing ended — a library that was written months ago
/// and has been sitting there since.
/// </remarks>
public sealed class RefreshServiceTests : IAsyncLifetime
{
    private const string ImageUrl = "https://cdn.example/videos/scene.jpg";

    private readonly TempDirectory directory = new();
    private readonly TestTime time = new();
    private readonly FakeVideoLookup videos = new();
    private readonly FakeCdn cdn = new();

    private ServiceProvider services = null!;
    private string library = null!;

    public async Task InitializeAsync()
    {
        library = Directory.CreateDirectory(directory.Combine("library")).FullName;

        var collection = new ServiceCollection();
        collection.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        collection.AddSingleton<TimeProvider>(time);
        collection.AddSingleton<IVideoLookup>(videos);

        collection.AddOrdenoPersistence(directory.Combine("data"));
        collection.AddOrdenoLibrary();

        collection
            .AddHttpClient(SceneArtwork.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => cdn);

        services = collection.BuildServiceProvider();

        await services.PrepareOrdenoDatabaseAsync();
        await ConfigureAsync();
    }

    public async Task DisposeAsync()
    {
        await services.DisposeAsync();
        cdn.Dispose();
        directory.Dispose();
    }

    /// <summary>
    /// The whole point of the feature: prdb corrected the title, and the file
    /// written last spring stops saying the old thing.
    /// </summary>
    [Fact]
    public async Task A_corrected_title_reaches_the_sidecar()
    {
        var scene = await FiledAsync("Scene Title");
        videos.Knows(scene.Video with { Title = "Corrected Title" });

        var report = await RefreshAsync();

        Assert.Equal(1, report.Checked);
        Assert.Equal(1, report.Sidecars);
        Assert.Contains("Corrected Title", await File.ReadAllTextAsync(scene.Sidecar), StringComparison.Ordinal);
        Assert.Contains("brought up to date", report.Account, StringComparison.Ordinal);
    }

    /// <summary>
    /// The steady state, and the reason the trigger is the document rather than
    /// a timestamp: the second run over an unchanged scene writes nothing at all,
    /// so a nightly check is reads and no writes.
    /// </summary>
    [Fact]
    public async Task A_scene_that_still_says_what_prdb_says_is_not_written_to()
    {
        var scene = await FiledAsync("Scene Title");
        var before = File.GetLastWriteTimeUtc(scene.Sidecar);

        var report = await RefreshAsync();

        Assert.Equal(1, report.Checked);
        Assert.Equal(0, report.Sidecars);
        Assert.Empty(report.Notes);
        Assert.Equal(before, File.GetLastWriteTimeUtc(scene.Sidecar));
        Assert.Contains("already said it", report.Account, StringComparison.Ordinal);
    }

    /// <summary>
    /// The hard rule, in the run that would break it most quietly. A document
    /// without the marker is somebody's own work, at the one name a Movies
    /// library reads.
    /// </summary>
    [Fact]
    public async Task A_sidecar_somebody_else_wrote_is_left_exactly_as_it_is()
    {
        var scene = await FiledAsync("Scene Title");
        await File.WriteAllTextAsync(scene.Sidecar, "<movie><title>Mine</title></movie>");
        videos.Knows(scene.Video with { Title = "Corrected Title" });

        var report = await RefreshAsync();

        Assert.Equal(0, report.Sidecars);
        Assert.Equal("<movie><title>Mine</title></movie>", await File.ReadAllTextAsync(scene.Sidecar));

        // And it cost no request: there was nothing this run could have written.
        Assert.Empty(videos.Described);
    }

    /// <summary>ADR 0024 left this to the refresh, and this is the refresh doing it.</summary>
    [Fact]
    public async Task A_sidecar_that_has_gone_missing_is_written_again()
    {
        var scene = await FiledAsync("Scene Title");
        File.Delete(scene.Sidecar);

        var report = await RefreshAsync();

        Assert.Equal(1, report.Sidecars);
        Assert.True(File.Exists(scene.Sidecar));
        Assert.Contains("Scene Title", await File.ReadAllTextAsync(scene.Sidecar), StringComparison.Ordinal);
    }

    /// <summary>
    /// The case somebody has after switching artwork on over a library that was
    /// filed without it — and ADR 0027 unchanged: written where there is none,
    /// never over one.
    /// </summary>
    [Fact]
    public async Task An_image_is_written_where_there_is_none_once_artwork_is_on()
    {
        var scene = await FiledAsync("Scene Title", image: ImageUrl);
        await ArtworkAsync(wanted: true);

        var report = await RefreshAsync();

        Assert.Equal(1, report.Images);
        Assert.True(File.Exists(scene.Artwork));
        Assert.Equal(ImageUrl, Assert.Single(cdn.Requests));
    }

    [Fact]
    public async Task An_image_that_is_already_there_is_never_replaced()
    {
        var scene = await FiledAsync("Scene Title", image: ImageUrl);
        await ArtworkAsync(wanted: true);
        await File.WriteAllTextAsync(scene.Artwork, "a picture somebody chose");

        var report = await RefreshAsync();

        Assert.Equal(0, report.Images);
        Assert.Equal("a picture somebody chose", await File.ReadAllTextAsync(scene.Artwork));
        Assert.Empty(cdn.Requests);
    }

    /// <summary>
    /// A corrected title changes the name the layout would produce, and this run
    /// does not act on that: re-filing a library is a different operation with
    /// different risks, and it was left out of this decision on purpose.
    /// </summary>
    [Fact]
    public async Task Nothing_is_moved_or_renamed_even_when_the_title_changed()
    {
        var scene = await FiledAsync("Scene Title");
        videos.Knows(scene.Video with { Title = "A Completely Different Title" });

        await RefreshAsync();

        Assert.True(File.Exists(scene.Video1080p));
        Assert.True(Directory.Exists(scene.Directory));

        var row = await RowAsync();
        Assert.Equal(scene.Directory, row.Directory);
        Assert.Equal("Scene Title.mkv", row.FileName);
    }

    /// <summary>
    /// What ADR 0033 decided about the log: the run has a row and its rewrites
    /// have no entries, because an entry is shaped for a way back that a replaced
    /// document does not need.
    /// </summary>
    [Fact]
    public async Task A_run_leaves_a_row_in_the_log_and_no_entries()
    {
        var scene = await FiledAsync("Scene Title");
        videos.Knows(scene.Video with { Title = "Corrected Title" });

        await RefreshAsync();

        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdenoDbContext>();

        var run = Assert.Single(await context.OperationRuns.AsNoTracking().ToListAsync());
        Assert.Equal(RunKind.Refresh, run.Kind);
        Assert.Contains("brought up to date", run.Account!, StringComparison.Ordinal);
        Assert.Empty(await context.Operations.AsNoTracking().ToListAsync());
    }

    /// <summary>
    /// ADR 0031's rule, applied to the run that would otherwise leave a row every
    /// night saying nothing happened.
    /// </summary>
    [Fact]
    public async Task A_run_nobody_asked_for_that_changed_nothing_leaves_no_row()
    {
        await FiledAsync("Scene Title");

        await RefreshAsync(AskedBy.Timer);

        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdenoDbContext>();

        Assert.Empty(await context.OperationRuns.AsNoTracking().ToListAsync());
    }

    /// <summary>
    /// And the other half of it: a run somebody asked for keeps its row whatever
    /// it did, because they asked.
    /// </summary>
    [Fact]
    public async Task A_run_somebody_asked_for_keeps_its_row_even_when_nothing_changed()
    {
        await FiledAsync("Scene Title");

        await RefreshAsync();

        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdenoDbContext>();

        Assert.Single(await context.OperationRuns.AsNoTracking().ToListAsync());
    }

    /// <summary>
    /// The slice, and what makes it survivable: the run takes the scenes nothing
    /// has looked at, stamps them, and the next one takes the others.
    /// </summary>
    [Fact]
    public async Task A_sliced_run_takes_the_least_recently_checked_scenes_and_the_next_one_carries_on()
    {
        var first = await FiledAsync("First Scene");
        var second = await FiledAsync("Second Scene");

        videos.Knows(first.Video with { Title = "First Scene Corrected" });
        videos.Knows(second.Video with { Title = "Second Scene Corrected" });

        var one = await RefreshAsync(slice: 1);

        Assert.Equal(1, one.Checked);

        // The one it did not get to. A run says what it left for the next one,
        // which on a rotating check is what somebody reading the screen wants.
        Assert.Equal(1, one.Waiting);

        var two = await RefreshAsync(slice: 1);

        Assert.Equal(1, two.Checked);
        Assert.Equal(1, two.Sidecars);

        // Both got there, one run each, and neither was checked twice.
        Assert.Contains(
            "Corrected",
            await File.ReadAllTextAsync(first.Sidecar),
            StringComparison.Ordinal);

        Assert.Contains(
            "Corrected",
            await File.ReadAllTextAsync(second.Sidecar),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Stopping on the quota is not a failure — the run wrote what it reached,
    /// said why it stopped, and left the rest for next time.
    /// </summary>
    [Fact]
    public async Task A_run_stops_before_it_has_spent_the_hourly_quota()
    {
        // One more scene than a batch holds, so that there is a second request
        // for the reserve to refuse.
        for (var number = 0; number <= IVideoLookup.MaxBatch; number++)
        {
            await FiledAsync($"Scene {number:00}");
        }

        // What prdb reports on the way past the first batch. The reading arrives
        // with the answer, so the first request is always spent — what the
        // reserve buys is everything after it.
        videos.Quota = new RateLimitReading(RefreshSchedule.QuotaReserve, TimeSpan.FromMinutes(5));

        var report = await RefreshAsync();

        Assert.Single(videos.Described);
        Assert.Equal(IVideoLookup.MaxBatch, report.Checked);
        Assert.Equal(1, report.Waiting);
        Assert.Contains("quota", report.Problem!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// And the monthly window, which is the one a nightly pass over a whole
    /// library actually threatens.
    /// </summary>
    [Fact]
    public async Task A_run_stops_before_it_has_spent_the_monthly_quota()
    {
        for (var number = 0; number <= IVideoLookup.MaxBatch; number++)
        {
            await FiledAsync($"Scene {number:00}");
        }

        videos.Quota = new RateLimitReading(
            null,
            null,
            RefreshSchedule.MonthlyQuotaReserve);

        var report = await RefreshAsync();

        Assert.Single(videos.Described);
        Assert.Contains("monthly", report.Problem!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A scene the run never got an answer about keeps its place at the front of
    /// the queue, or a library would be walked past while prdb was down.
    /// </summary>
    [Fact]
    public async Task A_scene_prdb_could_not_be_asked_about_is_not_marked_as_checked()
    {
        await FiledAsync("Scene Title");
        videos.Stopped = "prdb is having trouble answering.";

        var stopped = await RefreshAsync();

        Assert.Equal(0, stopped.Checked);
        Assert.Contains("trouble answering", stopped.Problem!, StringComparison.Ordinal);

        videos.Stopped = null;

        await using var scope = services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<RefreshService>();

        Assert.Equal(1, (await service.StandingAsync()).NeverChecked);
    }

    /// <summary>
    /// A row whose file has gone is skipped rather than acted on — and stamped,
    /// so that it cannot hold the front of the queue against the rest of the
    /// library forever.
    /// </summary>
    [Fact]
    public async Task A_row_whose_video_has_gone_is_left_alone_and_does_not_block_the_queue()
    {
        var missing = await FiledAsync("Gone Scene");
        File.Delete(missing.Video1080p);

        var other = await FiledAsync("Other Scene");
        videos.Knows(other.Video with { Title = "Other Scene Corrected" });

        var report = await RefreshAsync(slice: 1);

        // The slice was spent on the row whose file has gone: nothing was
        // written and nothing was removed.
        Assert.Equal(0, report.Checked);
        Assert.True(Directory.Exists(missing.Directory));
        Assert.True(File.Exists(missing.Sidecar));

        // And the next run reaches the scene behind it.
        var next = await RefreshAsync(slice: 1);

        Assert.Equal(1, next.Checked);
        Assert.Equal(1, next.Sidecars);
    }

    /// <summary>
    /// Only the library the tool is pointed at now. A row filed under a root
    /// somebody has moved away from says nothing about the one they are using.
    /// </summary>
    [Fact]
    public async Task Scenes_filed_under_another_library_root_are_not_touched()
    {
        var scene = await FiledAsync("Scene Title", libraryRoot: "/somewhere/else");
        videos.Knows(scene.Video with { Title = "Corrected Title" });

        var report = await RefreshAsync();

        Assert.Equal(0, report.Checked);
        Assert.Empty(videos.Described);
    }

    private async Task<RefreshReport> RefreshAsync(
        AskedBy askedBy = AskedBy.Person,
        int? slice = null)
    {
        await using var scope = services.CreateAsyncScope();

        return await scope.ServiceProvider
            .GetRequiredService<RefreshService>()
            .RefreshAsync(askedBy, slice);
    }

    /// <summary>
    /// A scene as filing left it months ago: a directory, a video, a sidecar
    /// carrying the marker, and the row that says the library holds it.
    /// </summary>
    private async Task<Filed> FiledAsync(string title, string? image = null, string? libraryRoot = null)
    {
        var video = videos.Knows(new VideoSummary(
            Guid.NewGuid(),
            title,
            new DateOnly(2024, 5, 1),
            Guid.NewGuid(),
            "Example Studio",
            ["A Performer"],
            image));

        var directory = Directory.CreateDirectory(Path.Combine(library, "Example Studio", title)).FullName;
        var videoPath = Path.Combine(directory, $"{title}.mkv");

        await File.WriteAllTextAsync(videoPath, "not really a video");
        await File.WriteAllTextAsync(
            Path.Combine(directory, ScenePath.SidecarFileName),
            MovieNfo.For(SceneMetadata.From(video)!));

        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdenoDbContext>();

        context.FiledVideos.Add(new FiledVideo
        {
            VideoId = video.VideoId,
            LibraryRoot = libraryRoot ?? library,
            Directory = directory,
            FileName = Path.GetFileName(videoPath),
            QualityLabel = "1080p",
            FiledAt = time.GetUtcNow(),
        });

        await context.SaveChangesAsync();

        return new Filed(
            video,
            directory,
            videoPath,
            Path.Combine(directory, ScenePath.SidecarFileName),
            Path.Combine(directory, ScenePath.ArtworkFileName));
    }

    private async Task<FiledVideo> RowAsync()
    {
        await using var scope = services.CreateAsyncScope();

        return await scope.ServiceProvider
            .GetRequiredService<OrdenoDbContext>()
            .FiledVideos
            .AsNoTracking()
            .SingleAsync();
    }

    private async Task ArtworkAsync(bool wanted)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdenoDbContext>();

        var configuration = await context.Configuration.SingleAsync();
        configuration.DownloadArtwork = wanted;

        await context.SaveChangesAsync();
    }

    private async Task ConfigureAsync()
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdenoDbContext>();

        var configuration = await context.Configuration.SingleAsync();
        configuration.PrdbApiKey = "not-a-real-key";
        configuration.TargetDirectory = library;
        configuration.Layout = LibraryLayouts.NameOf(LibraryLayout.Jellyfin);
        configuration.OnboardingCompletedAt = time.GetUtcNow();

        await context.SaveChangesAsync();
    }

    private sealed record Filed(
        VideoSummary Video,
        string Directory,
        string Video1080p,
        string Sidecar,
        string Artwork);
}

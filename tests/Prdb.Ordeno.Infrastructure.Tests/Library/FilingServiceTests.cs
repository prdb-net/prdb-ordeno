using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Prdb.Ordeno.Core.Configuration;
using Prdb.Ordeno.Core.Identification;
using Prdb.Ordeno.Core.Library;
using Prdb.Ordeno.Core.Scanning;
using Prdb.Ordeno.Infrastructure.Identification;
using Prdb.Ordeno.Infrastructure.Library;
using Prdb.Ordeno.Infrastructure.Persistence;
using Prdb.Ordeno.Infrastructure.Scanning;
using Prdb.Ordeno.Infrastructure.Tests.Identification;
using Prdb.Ordeno.Infrastructure.Tests.Scanning;

using Xunit;

namespace Prdb.Ordeno.Infrastructure.Tests.Library;

/// <summary>
/// Issue #17 end to end, against a real filesystem, a real SQLite file, real
/// videos and the real ffprobe: a download is walked, asked about, planned and
/// moved, and the download directory ends up emptier by exactly one file.
/// </summary>
/// <remarks>
/// Only prdb's endpoint and the exact hash are replaced, because they are the
/// two things a test has to be able to decide. Everything between them — the
/// scan, the settling rule, the planner, the quality reading and the move — is
/// what a container would run.
/// </remarks>
public sealed class FilingServiceTests : IAsyncLifetime
{
    private const string Site = "Example Studio";
    private const string Title = "Scene Title";

    /// <summary>What the layout gives the scene the fake prdb answers with.</summary>
    private const string SceneDirectory = "Example Studio/Example Studio - 2024-05-01 - Scene Title";

    private readonly TempDirectory directory = new();
    private readonly TestTime time = new();
    private readonly FakePrdbIdentification prdb = new();
    private readonly TestFileHashes hashes = new();
    private readonly StoppingQualities stopping = new();

    private ServiceProvider services = null!;
    private string downloads = null!;
    private string library = null!;

    public async Task InitializeAsync()
    {
        downloads = Directory.CreateDirectory(directory.Combine("downloads")).FullName;
        library = Directory.CreateDirectory(directory.Combine("library")).FullName;

        var collection = new ServiceCollection();
        collection.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        collection.AddSingleton<TimeProvider>(time);
        collection.AddSingleton<IVideoIdentification>(prdb);
        collection.AddSingleton<IFileHashes>(hashes);

        // Registered before the slice, whose registrations are TryAdd. It is the
        // real ffprobe until a test asks it to stop the run, which is how a
        // shutdown is put in a known place rather than raced for.
        collection.AddSingleton<IVideoQualities>(stopping);

        collection.AddOrdenoPersistence(directory.Combine("data"));
        collection.AddOrdenoScanning();
        collection.AddOrdenoIdentification();
        collection.AddOrdenoLibrary();

        services = collection.BuildServiceProvider();

        await services.PrepareOrdenoDatabaseAsync();
        await ConfigureAsync();
    }

    public async Task DisposeAsync()
    {
        await services.DisposeAsync();
        stopping.Dispose();
        directory.Dispose();
    }

    /// <summary>
    /// The line the whole issue is about: the video is where the layout says,
    /// and the download directory is emptier by exactly one file.
    /// </summary>
    [Fact]
    public async Task A_recognised_video_ends_up_where_the_layout_says()
    {
        prdb.Recognises(Guid.NewGuid(), Title, Site);
        var source = await ArrivedAsync("Example.Studio.24.05.01.Scene.Title.1080p.mkv", 1920, 1080);
        await ReadyAsync();

        var report = await FileAsync();

        var result = Assert.Single(report.Results);
        Assert.Equal(FilingResultState.Filed, result.State);

        Assert.Equal(
            Path.Combine(library, SceneDirectory, "Example Studio - 2024-05-01 - Scene Title.mkv"),
            result.Plan.TargetPath);
        Assert.True(File.Exists(result.Plan.TargetPath));
        Assert.False(File.Exists(source));
        Assert.Empty(Directory.GetFiles(downloads));
    }

    /// <summary>
    /// ADR 0022, and the reason the plan is a type rather than a log line: what
    /// the user is shown is what runs. Not a description of it — the same
    /// computation, made again.
    /// </summary>
    [Fact]
    public async Task The_preview_is_what_the_run_carries_out()
    {
        prdb.Recognises(Guid.NewGuid(), Title, Site);
        await ArrivedAsync("Example.Studio.24.05.01.Scene.Title.1080p.mkv", 1920, 1080);
        await ReadyAsync();

        var preview = await PlanAsync();
        var report = await FileAsync();

        var planned = Assert.Single(preview.Plans);
        var carried = Assert.Single(report.Results);

        Assert.Equal(planned, carried.Plan);
        Assert.True(File.Exists(planned.TargetPath));
    }

    /// <summary>
    /// A preview moves nothing. It is the half of ADR 0022 that would be easy to
    /// get wrong once the two share a code path.
    /// </summary>
    [Fact]
    public async Task Working_out_what_would_happen_moves_nothing()
    {
        prdb.Recognises(Guid.NewGuid(), Title, Site);
        var source = await ArrivedAsync("scene.1080p.mkv", 1280, 720);
        await ReadyAsync();

        var preview = await PlanAsync();

        Assert.Single(preview.Plans);
        Assert.True(File.Exists(source));
        Assert.Empty(Directory.GetDirectories(library));
    }

    /// <summary>
    /// ADR 0003: the same scene at the same quality is not filed, not deleted,
    /// and reported. This is the download directory that holds one scene twice,
    /// which is the case the tool was pointed at.
    /// </summary>
    [Fact]
    public async Task A_second_copy_at_the_same_quality_is_left_where_it_is()
    {
        prdb.Recognises(Guid.NewGuid(), Title, Site);
        await ArrivedAsync("first.1080p.mkv", 1920, 1080);
        await ReadyAsync();
        await FileAsync();

        var second = await ArrivedAsync("second.1080p.mkv", 1920, 1080);
        await ReadyAsync();

        var result = Assert.Single((await FileAsync()).Results);

        Assert.Equal(FilingResultState.Skipped, result.State);
        Assert.Equal(FilingOutcome.AlreadyFiled, result.Plan.Outcome);
        Assert.True(File.Exists(second));
        Assert.Contains("not deleted", result.Message);

        // One directory, one file: the library did not gain a second entry.
        Assert.Single(Directory.GetFiles(Path.Combine(library, SceneDirectory)));
    }

    /// <summary>
    /// ADR 0003 and ADR 0020 together: both qualities are kept, in one
    /// directory, and the one that was filed first is renamed to carry its own
    /// label so the media server lists both by their quality.
    /// </summary>
    [Fact]
    public async Task A_second_quality_joins_the_first_and_both_end_up_labelled()
    {
        prdb.Recognises(Guid.NewGuid(), Title, Site);
        await ArrivedAsync("first.1080p.mkv", 1920, 1080);
        await ReadyAsync();
        await FileAsync();

        await ArrivedAsync("second.2160p.mkv", 3840, 2160);
        await ReadyAsync();

        var result = Assert.Single((await FileAsync()).Results);

        Assert.Equal(FilingResultState.Filed, result.State);
        Assert.Equal(FilingOutcome.SecondQuality, result.Plan.Outcome);

        var scene = Path.Combine(library, SceneDirectory);

        Assert.Equal(
            [
                "Example Studio - 2024-05-01 - Scene Title - [1080p].mkv",
                "Example Studio - 2024-05-01 - Scene Title - [2160p].mkv",
            ],
            Directory.GetFiles(scene).Select(Path.GetFileName).Order(StringComparer.Ordinal));

        // And the record was rewritten with it, or the next filing would look
        // for a file that is no longer called that.
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdenoDbContext>();

        Assert.Equal(
            ["Example Studio - 2024-05-01 - Scene Title - [1080p].mkv",
                "Example Studio - 2024-05-01 - Scene Title - [2160p].mkv"],
            await context.FiledVideos.OrderBy(row => row.FileName).Select(row => row.FileName).ToListAsync());
    }

    /// <summary>
    /// Section 6 is what this protects: every file in a scene directory has to
    /// begin with the directory's own name, or the library shows two entries
    /// with identical names instead of one with two versions.
    /// </summary>
    [Fact]
    public async Task Every_file_in_a_scene_directory_begins_with_its_name()
    {
        prdb.Recognises(Guid.NewGuid(), Title, Site);
        await ArrivedAsync("first.720p.mkv", 1280, 720);
        await ReadyAsync();
        await FileAsync();

        await ArrivedAsync("second.2160p.mkv", 3840, 2160);
        await ReadyAsync();
        await FileAsync();

        var scene = Path.Combine(library, SceneDirectory);

        Assert.All(
            Directory.GetFiles(scene),
            file => Assert.StartsWith(
                Path.GetFileName(scene),
                Path.GetFileName(file),
                StringComparison.Ordinal));
    }

    /// <summary>
    /// ADR 0020: without a quality neither the skip nor the label can be
    /// decided. The file stays where it is and the message says why.
    /// </summary>
    [Fact]
    public async Task A_file_whose_quality_cannot_be_read_is_not_filed()
    {
        prdb.Recognises(Guid.NewGuid(), Title, Site);

        var path = Path.Combine(downloads, "truncated.mkv");
        await File.WriteAllBytesAsync(path, new byte[64 * 1024]);
        await ReadyAsync();

        var result = Assert.Single((await FileAsync()).Results);

        Assert.Equal(FilingResultState.Skipped, result.State);
        Assert.Equal(FilingOutcome.Blocked, result.Plan.Outcome);
        Assert.True(File.Exists(path));
        Assert.Empty(Directory.GetDirectories(library));
    }

    /// <summary>
    /// ADR 0019: a file prdb cannot name is not filed at all, so it is not even
    /// a candidate. It waits for the review queue rather than appearing here as
    /// something that failed.
    /// </summary>
    [Fact]
    public async Task A_file_prdb_could_not_name_is_not_a_candidate()
    {
        await ArrivedAsync("mystery.mkv", 1280, 720);
        await ReadyAsync();

        Assert.Empty((await PlanAsync()).Plans);
    }

    /// <summary>
    /// The rule the scan established and filing inherits: a file that is still
    /// being written is not acted on, whatever prdb has already said about it.
    /// </summary>
    [Fact]
    public async Task A_file_that_has_not_settled_is_not_filed()
    {
        prdb.Recognises(Guid.NewGuid(), Title, Site);
        await ArrivedAsync("still.arriving.mkv", 1280, 720);
        await ReadyAsync();

        // It grew again after prdb had answered about it.
        await ArrivedAsync("still.arriving.mkv", 1920, 1080);
        await ScanAsync();

        Assert.Empty((await PlanAsync()).Plans);
    }

    /// <summary>
    /// Nothing is filed until the setup is finished — ADR 0009 — and the answer
    /// says so rather than being an empty list somebody has to interpret.
    /// </summary>
    [Fact]
    public async Task Nothing_is_filed_before_the_setup_is_finished()
    {
        prdb.Recognises(Guid.NewGuid(), Title, Site);
        await ArrivedAsync("scene.mkv", 1280, 720);
        await ReadyAsync();

        await using (var scope = services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<OrdenoDbContext>();
            var configuration = await context.Configuration.SingleAsync();
            configuration.OnboardingCompletedAt = null;

            await context.SaveChangesAsync();
        }

        var preview = await PlanAsync();

        Assert.Empty(preview.Plans);
        Assert.NotNull(preview.Problem);
    }

    /// <summary>
    /// A library that has gone — an unmounted share, most often — stops
    /// everything and says so. Filing into the empty mount point underneath it
    /// would write somebody's library onto the container's own disk.
    /// </summary>
    [Fact]
    public async Task A_library_that_cannot_be_written_to_stops_the_run()
    {
        prdb.Recognises(Guid.NewGuid(), Title, Site);
        var source = await ArrivedAsync("scene.mkv", 1280, 720);
        await ReadyAsync();

        Directory.Delete(library, recursive: true);

        var report = await FileAsync();

        Assert.Empty(report.Results);
        Assert.NotNull(report.Problem);
        Assert.True(File.Exists(source));
    }

    /// <summary>
    /// The tool's memory of the download goes with the download. Leaving the row
    /// behind would have the next run try to move a file that is not there and
    /// report it as missing, minutes after filing it.
    /// </summary>
    [Fact]
    public async Task A_video_that_has_been_filed_is_no_longer_a_download()
    {
        prdb.Recognises(Guid.NewGuid(), Title, Site);
        await ArrivedAsync("scene.1080p.mkv", 1920, 1080);
        await ReadyAsync();
        await FileAsync();

        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdenoDbContext>();

        Assert.Empty(await context.DiscoveredFiles.ToListAsync());
        Assert.Empty(await context.FileIdentifications.ToListAsync());
        Assert.Empty((await PlanAsync()).Plans);
    }

    /// <summary>
    /// A record of a file the user has since deleted describes a library that no
    /// longer holds the scene, so the scene is filed again rather than reported
    /// as a duplicate of something that is not there.
    /// </summary>
    [Fact]
    public async Task A_scene_the_user_deleted_from_the_library_is_filed_again()
    {
        prdb.Recognises(Guid.NewGuid(), Title, Site);
        await ArrivedAsync("first.1080p.mkv", 1920, 1080);
        await ReadyAsync();
        await FileAsync();

        Directory.Delete(Path.Combine(library, SceneDirectory), recursive: true);

        await ArrivedAsync("again.1080p.mkv", 1920, 1080);
        await ReadyAsync();

        var result = Assert.Single((await FileAsync()).Results);

        Assert.Equal(FilingResultState.Filed, result.State);
        Assert.Equal(FilingOutcome.Filed, result.Plan.Outcome);

        // And the record that had gone stale was replaced rather than joined.
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdenoDbContext>();

        Assert.Single(await context.FiledVideos.ToListAsync());
    }

    /// <summary>
    /// Two different scenes whose names the layout cannot tell apart. Filed into
    /// one directory they would become one entry with two versions, and one of
    /// them would stop existing — so the second is stepped around with prdb's
    /// scene id.
    /// </summary>
    [Fact]
    public async Task Two_scenes_the_layout_gives_one_name_do_not_share_a_directory()
    {
        var second = Guid.NewGuid();

        prdb.Recognises(Guid.NewGuid(), Title, Site);
        await ArrivedAsync("first.1080p.mkv", 1920, 1080);
        await ReadyAsync();
        await FileAsync();

        prdb.Recognises(second, Title, Site);
        await ArrivedAsync("other.scene.1080p.mkv", 1920, 1080);
        await ReadyAsync();

        var result = Assert.Single((await FileAsync()).Results);

        Assert.Equal(FilingResultState.Filed, result.State);
        Assert.Equal(FilingOutcome.CollisionBroken, result.Plan.Outcome);
        Assert.Contains(second.ToString("d"), result.Plan.Directory);
        Assert.Equal(2, Directory.GetDirectories(Path.Combine(library, "Example Studio")).Length);
    }

    /// <summary>
    /// What <c>docker stop</c> has to mean. The entrypoint <c>exec</c>s so the
    /// signal reaches the application, and what arrives here is a cancelled
    /// token partway through a run: the file that had already moved is filed and
    /// written down, and the ones behind it are untouched and reported as not
    /// reached rather than silently dropped.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The stop is arranged from inside the run rather than by racing it with a
    /// timer, because a test that sometimes stops in the right place is a test
    /// that sometimes proves nothing. The quality reading is the seam: it is
    /// what every file goes through on its way to being planned.
    /// </para>
    /// <para>
    /// The other half — a stop that lands in the middle of a cross-filesystem
    /// copy — is in <see cref="LibraryMovesTests"/>. It cannot be arranged here:
    /// both directories are on one filesystem, so a filing is a rename, and a
    /// rename has no middle.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_run_stopped_partway_files_what_it_reached_and_leaves_the_rest()
    {
        prdb.Recognises(Guid.NewGuid(), Title, Site);
        var first = await ArrivedAsync("first.1080p.mkv", 1920, 1080);
        var second = await ArrivedAsync("second.1080p.mkv", 1920, 1080);
        await ReadyAsync();

        // The stop arrives while the first file is being worked out, which is
        // where a real one would: mid-run, with a file already on its way.
        stopping.After(reads: 1);

        var report = await FileAsync(stopping.Token);

        Assert.Equal(2, report.Results.Count);
        Assert.NotNull(report.Problem);

        var filed = Assert.Single(report.Results, result => result.Filed);
        var stopped = Assert.Single(report.Results, result => result.State is FilingResultState.Stopped);

        Assert.True(File.Exists(filed.Plan.TargetPath));
        Assert.False(File.Exists(filed.Plan.SourcePath));
        Assert.True(File.Exists(stopped.Plan.SourcePath));

        // And the one that did move was written down, although the run was
        // stopping while that happened. A library holding a video no row knows
        // about would be filed around next time.
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdenoDbContext>();

        Assert.Single(await context.FiledVideos.ToListAsync());

        // Belt and braces: one of the two is still in the download directory.
        Assert.Single([first, second], File.Exists);
    }

    private async Task<FilingPreview> PlanAsync()
    {
        await using var scope = services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<FilingService>().PlanAsync();
    }

    private async Task<FilingReport> FileAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();

        return await scope.ServiceProvider
            .GetRequiredService<FilingService>()
            .FileAsync(cancellationToken);
    }

    /// <summary>A real video of the given size, in the download directory.</summary>
    private async Task<string> ArrivedAsync(string relative, int width, int height)
    {
        var path = Path.Combine(downloads, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        TestVideos.Write(path, width, height);

        await Task.CompletedTask;

        return path;
    }

    /// <summary>Two scans a quiet period apart, and then prdb is asked.</summary>
    private async Task ReadyAsync()
    {
        await ScanAsync();
        time.Advance(Settling.QuietPeriod);
        await ScanAsync();
        await IdentifyAsync();
    }

    private async Task ScanAsync()
    {
        await using var scope = services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<ScanService>().ScanAsync();
    }

    private async Task IdentifyAsync()
    {
        await using var scope = services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<IdentificationService>().IdentifyAsync();
    }

    /// <summary>
    /// The real reading of a picture size, with a way to cancel the run at a
    /// known point — after the given number of files have been measured.
    /// </summary>
    private sealed class StoppingQualities : IVideoQualities, IDisposable
    {
        private readonly VideoQualities real = new(NullLogger<VideoQualities>.Instance);
        private readonly CancellationTokenSource source = new();

        private int stopAfter = -1;
        private int read;

        public CancellationToken Token => source.Token;

        public void After(int reads) => stopAfter = reads;

        public async Task<VideoQualityReading> ReadAsync(
            string path,
            CancellationToken cancellationToken = default)
        {
            var reading = await real.ReadAsync(path, cancellationToken);

            if (stopAfter > 0 && ++read >= stopAfter)
            {
                await source.CancelAsync();
            }

            return reading;
        }

        public void Dispose() => source.Dispose();
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

        context.SourceDirectories.Add(new SourceDirectory { Path = downloads, AddedAt = time.GetUtcNow() });

        await context.SaveChangesAsync();
    }
}

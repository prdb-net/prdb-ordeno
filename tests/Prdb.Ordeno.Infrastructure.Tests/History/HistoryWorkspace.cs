using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Prdb.Ordeno.Core.Configuration;
using Prdb.Ordeno.Core.History;
using Prdb.Ordeno.Core.Identification;
using Prdb.Ordeno.Core.Library;
using Prdb.Ordeno.Core.Review;
using Prdb.Ordeno.Core.Scanning;
using Prdb.Ordeno.Infrastructure.History;
using Prdb.Ordeno.Infrastructure.Identification;
using Prdb.Ordeno.Infrastructure.Library;
using Prdb.Ordeno.Infrastructure.Persistence;
using Prdb.Ordeno.Infrastructure.Scanning;
using Prdb.Ordeno.Infrastructure.Tests.Identification;
using Prdb.Ordeno.Infrastructure.Tests.Library;
using Prdb.Ordeno.Infrastructure.Tests.Review;
using Prdb.Ordeno.Infrastructure.Tests.Scanning;

namespace Prdb.Ordeno.Infrastructure.Tests.History;

/// <summary>
/// A container's worth of the tool, against a real filesystem, a real SQLite
/// file, real videos and the real ffprobe — set up, pointed at two directories,
/// and able to file and to put back.
/// </summary>
/// <remarks>
/// Shared by the two files that test the log and the way back, because both need
/// the same thing to have happened first: something actually filed. Only prdb's
/// endpoint, its lookup and the CDN are replaced; the scan, the settling rule,
/// the planner, the moves and the log are what a container would run.
/// </remarks>
internal sealed class HistoryWorkspace : IAsyncDisposable
{
    public const string Site = "Example Studio";
    public const string Title = "Scene Title";
    public const string ImageUrl = "https://cdn.example/videos/scene.jpg";

    /// <summary>What the layout gives the scene the fake prdb answers with.</summary>
    public const string SceneDirectory = "Example Studio/Example Studio - 2024-05-01 - Scene Title";

    private readonly TempDirectory directory = new();

    private HistoryWorkspace()
    {
    }

    public TestTime Time { get; } = new();

    public FakePrdbIdentification Prdb { get; } = new();

    public FakeVideoLookup Videos { get; } = new();

    public TestFileHashes Hashes { get; } = new();

    public FakeCdn Cdn { get; } = new();

    public ServiceProvider Services { get; private set; } = null!;

    public string Downloads { get; private set; } = null!;

    public string Library { get; private set; } = null!;

    /// <summary>The scene directory the fixtures below are filed into.</summary>
    public string Scene => Path.Combine(Library, SceneDirectory);

    public static async Task<HistoryWorkspace> StartAsync()
    {
        var workspace = new HistoryWorkspace();

        workspace.Downloads = Directory.CreateDirectory(workspace.directory.Combine("downloads")).FullName;
        workspace.Library = Directory.CreateDirectory(workspace.directory.Combine("library")).FullName;

        var collection = new ServiceCollection();
        collection.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        collection.AddSingleton<TimeProvider>(workspace.Time);
        collection.AddSingleton<IVideoIdentification>(workspace.Prdb);
        collection.AddSingleton<IVideoLookup>(workspace.Videos);
        collection.AddSingleton<IFileHashes>(workspace.Hashes);

        collection.AddOrdenoPersistence(workspace.directory.Combine("data"));
        collection.AddOrdenoScanning();
        collection.AddOrdenoIdentification();
        collection.AddOrdenoHistory();

        collection
            .AddHttpClient(SceneArtwork.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => workspace.Cdn);

        workspace.Services = collection.BuildServiceProvider();

        await workspace.Services.PrepareOrdenoDatabaseAsync();
        await workspace.ConfigureAsync();

        return workspace;
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        Cdn.Dispose();
        directory.Dispose();
    }

    /// <summary>One video, known to both halves of prdb that a test replaces.</summary>
    public Guid Recognised(string? image = ImageUrl, Guid? videoId = null)
    {
        var id = videoId ?? Guid.NewGuid();

        Prdb.Recognises(id, Title, Site);

        Videos.Knows(new VideoSummary(
            id,
            Title,
            new DateOnly(2024, 5, 1),
            Guid.NewGuid(),
            Site,
            [],
            image));

        return id;
    }

    /// <summary>Somebody turned artwork on — ADR 0027.</summary>
    public async Task ArtworkOnAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdenoDbContext>();

        (await context.Configuration.SingleAsync()).DownloadArtwork = true;

        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Each of these files is its own video.
    /// </summary>
    /// <remarks>
    /// The fake answers one video for a whole batch, which makes two files in
    /// one run a scene at two qualities. That is a real case and it is not this
    /// one: two scenes filed in one run is what a batch usually is, and it is
    /// what an undo of a run has to put back one by one.
    /// </remarks>
    public void RecognisedSeparately(params string[] names)
    {
        var scenes = names.ToDictionary(
            name => name,
            name => (Id: Guid.NewGuid(), Title: $"{Title} {name}"),
            StringComparer.Ordinal);

        foreach (var scene in scenes.Values)
        {
            Videos.Knows(new VideoSummary(
                scene.Id,
                scene.Title,
                new DateOnly(2024, 5, 1),
                Guid.NewGuid(),
                Site,
                [],
                null));
        }

        Prdb.Answers(files => IdentificationAnswer.From(
        [
            .. files.Select(file =>
            {
                var scene = scenes[Path.GetFileName(file.FileName)];

                return new RecognisedFile(
                    file.Ref,
                    MatchConfidence.Exact,
                    MatchRung.OsHash,
                    scene.Id,
                    scene.Title,
                    new DateOnly(2024, 5, 1),
                    Guid.NewGuid(),
                    Site,
                    []);
            }),
        ]));
    }

    /// <summary>A real video of the given size, in the download directory.</summary>
    /// <param name="lossless">
    /// Large enough to have an exact hash. Only the test about a file whose
    /// bytes changed without its length changing needs one.
    /// </param>
    public string Arrived(string relative, int width = 1920, int height = 1080, bool lossless = false)
    {
        var path = Path.Combine(Downloads, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        TestVideos.Write(path, width, height, lossless);

        return path;
    }

    /// <summary>Two scans a quiet period apart, and then prdb is asked.</summary>
    public async Task ReadyAsync()
    {
        await ScanAsync();
        Time.Advance(Settling.QuietPeriod);
        await ScanAsync();

        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdentificationService>().IdentifyAsync();
    }

    public async Task<FilingReport> FileAsync(AskedBy askedBy = AskedBy.Person)
    {
        await using var scope = Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<FilingService>().FileAsync(askedBy);
    }

    public async Task<FilingPreview> PlanAsync()
    {
        await using var scope = Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<FilingService>().PlanAsync();
    }

    /// <summary>What the timer asks before it starts a run at all — ADR 0031.</summary>
    public async Task<bool> AnythingWaitingAsync()
    {
        await using var scope = Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<FilingService>().AnythingWaitingAsync();
    }

    /// <summary>Takes a hold off one file, or off all of them — ADR 0030.</summary>
    public async Task<int> ReleaseAsync(int? fileId = null)
    {
        await using var scope = Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<FilingService>().ReleaseAsync(fileId);
    }

    public async Task<IReadOnlyList<FileHold>> HoldsAsync()
    {
        await using var scope = Services.CreateAsyncScope();

        return await scope.ServiceProvider
            .GetRequiredService<OrdenoDbContext>()
            .FileHolds.AsNoTracking().OrderBy(hold => hold.Id).ToListAsync();
    }

    public async Task<UndoPreview> CheckAsync(int? runId = null, int? operationId = null)
    {
        await using var scope = Services.CreateAsyncScope();

        return await scope.ServiceProvider
            .GetRequiredService<UndoService>()
            .CheckAsync(runId, operationId);
    }

    public async Task<UndoReport> UndoAsync(int? runId = null, int? operationId = null)
    {
        await using var scope = Services.CreateAsyncScope();

        return await scope.ServiceProvider
            .GetRequiredService<UndoService>()
            .UndoAsync(runId, operationId);
    }

    public async Task<OperationHistory> HistoryAsync(int page = 1)
    {
        await using var scope = Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<HistoryService>().ReadAsync(page);
    }

    /// <summary>
    /// What the review queue writes when somebody answers, written straight to
    /// the database: what is under test here is what the log makes of it.
    /// </summary>
    public async Task DecidedAsync(string relative, Guid videoId)
    {
        Videos.Knows(new VideoSummary(
            videoId,
            Title,
            new DateOnly(2024, 5, 1),
            Guid.NewGuid(),
            Site,
            []));

        await using var scope = Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdenoDbContext>();

        var path = Path.Combine(Downloads, relative);
        var file = await context.DiscoveredFiles.SingleAsync(row => row.Path == path);

        context.FileResolutions.Add(new FileResolution
        {
            DiscoveredFileId = file.Id,
            DecidedAt = Time.GetUtcNow(),
            Kind = ResolutionKind.Assigned,
            From = ResolvedFrom.Search,
            VideoId = videoId,
            Title = Title,
            ReleaseDate = new DateOnly(2024, 5, 1),
            SiteTitle = Site,
        });

        await context.SaveChangesAsync();
    }

    /// <summary>Whatever a test has to write or read straight out of the database.</summary>
    public async Task WithContextAsync(Func<OrdenoDbContext, Task> what)
    {
        ArgumentNullException.ThrowIfNull(what);

        await using var scope = Services.CreateAsyncScope();

        await what(scope.ServiceProvider.GetRequiredService<OrdenoDbContext>());
    }

    /// <summary>The trim ADR 0028 runs after every run that finishes.</summary>
    public async Task TrimAsync()
    {
        await using var scope = Services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<OperationLog>().TrimAsync();
    }

    public async Task<IReadOnlyList<OperationRun>> RunsAsync()
    {
        await using var scope = Services.CreateAsyncScope();

        return await scope.ServiceProvider
            .GetRequiredService<OrdenoDbContext>()
            .OperationRuns.AsNoTracking().OrderBy(run => run.Id).ToListAsync();
    }

    public async Task<IReadOnlyList<OperationEntry>> OperationsAsync()
    {
        await using var scope = Services.CreateAsyncScope();

        return await scope.ServiceProvider
            .GetRequiredService<OrdenoDbContext>()
            .Operations.AsNoTracking().OrderBy(entry => entry.Id).ToListAsync();
    }

    public async Task<IReadOnlyList<FiledVideo>> FiledAsync()
    {
        await using var scope = Services.CreateAsyncScope();

        return await scope.ServiceProvider
            .GetRequiredService<OrdenoDbContext>()
            .FiledVideos.AsNoTracking().OrderBy(row => row.FileName).ToListAsync();
    }

    /// <summary>Everything in a scene directory that filing did not write next to a video.</summary>
    public static IReadOnlyList<string> VideosIn(string sceneDirectory) =>
    [
        .. Directory.GetFiles(sceneDirectory)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(file => file is not ("movie.nfo" or "fanart.jpg"))
            .Order(StringComparer.Ordinal),
    ];

    public async Task ScanAsync()
    {
        await using var scope = Services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<ScanService>().ScanAsync();
    }

    private async Task ConfigureAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdenoDbContext>();

        var configuration = await context.Configuration.SingleAsync();
        configuration.PrdbApiKey = "not-a-real-key";
        configuration.TargetDirectory = Library;
        configuration.Layout = LibraryLayouts.NameOf(LibraryLayout.Jellyfin);
        configuration.OnboardingCompletedAt = Time.GetUtcNow();

        context.SourceDirectories.Add(new SourceDirectory { Path = Downloads, AddedAt = Time.GetUtcNow() });

        await context.SaveChangesAsync();
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Ordeno.Core.History;
using Prdb.Ordeno.Core.Identification;
using Prdb.Ordeno.Core.Library;
using Prdb.Ordeno.Core.Review;
using Prdb.Ordeno.Infrastructure.History;
using Prdb.Ordeno.Infrastructure.Persistence;

namespace Prdb.Ordeno.Infrastructure.Library;

/// <summary>
/// What `VISION.md` calls the library not being written once: prdb corrects a
/// title, a date or a cast entry, and the file written last spring is brought up
/// to date — ADR 0032 and ADR 0033.
/// </summary>
/// <remarks>
/// <para>
/// A run of its own rather than a phase of filing. Its subject is the
/// <see cref="FiledVideo"/> rows for the library the tool is pointed at now, one
/// scene directory at a time, least recently checked first — never a walk of the
/// library root, because a directory the tool did not file is somebody else's
/// and a row that was deleted was deleted on purpose.
/// </para>
/// <para>
/// It moves nothing and renames nothing. A corrected title changes what the
/// layout would produce, and re-filing a library under a changed name is a
/// different operation with different risks that has not been decided.
/// </para>
/// <para>
/// The order of the two questions is the opposite of filing's, and deliberately
/// (ADR 0033): what is in the directory decides whether prdb is asked at all,
/// because that look is a directory read the run has to make anyway and the
/// request is the scarce thing.
/// </para>
/// </remarks>
public sealed class RefreshService(
    OrdenoDbContext context,
    IVideoLookup videos,
    Sidecars sidecars,
    SceneArtwork artwork,
    OperationLog log,
    TimeProvider time,
    ILogger<RefreshService> logger)
{
    /// <summary>
    /// How many scene directories are stamped in one statement. The paths go in
    /// as parameters, and a first pass over a large library is thousands of them.
    /// </summary>
    private const int StampSize = 200;

    /// <summary>
    /// Checks what the tool filed against what prdb says now, and writes what has
    /// changed.
    /// </summary>
    /// <param name="askedBy">
    /// Whether somebody pressed the button or the timer came round. It changes
    /// nothing about what the run does except how far it goes, and what an empty
    /// run leaves in the log.
    /// </param>
    /// <param name="slice">
    /// How many scenes to look at, or <c>null</c> for the whole library.
    /// <see cref="RefreshSchedule.Slice"/> is what the timer passes: it fixes
    /// what a tick costs whatever size the library is.
    /// </param>
    public async Task<RefreshReport> RefreshAsync(
        AskedBy askedBy = AskedBy.Person,
        int? slice = null,
        CancellationToken cancellationToken = default)
    {
        // Opened before anything is looked at, exactly as filing opens one: "you
        // asked, and this is why nothing happened" is an answer somebody is owed.
        // A run nobody asked for that changed nothing is removed again when it
        // closes.
        var runId = await log.StartAsync(RunKind.Refresh, askedBy);
        var library = await ReadLibraryAsync(cancellationToken);

        if (library.Problem is { } unusable)
        {
            await log.FinishAsync(runId, account: null, unusable, changedFiles: false);

            return RefreshReport.Nothing with { Problem = unusable };
        }

        var scenes = await ScenesAsync(library.Root!, cancellationToken);
        var taking = slice is { } max && max < scenes.Count ? scenes[..max] : scenes;

        var report = await CheckAsync(library, taking, scenes.Count, cancellationToken);

        await log.FinishAsync(runId, report.Account, report.Problem, report.ChangedAnything);

        return report;
    }

    /// <summary>
    /// Whether the tool checks its own library without being asked — ADR 0032.
    /// Read straight off the row, like <c>FilingService.UnattendedAsync</c>, for
    /// the same reason: a screen polls this.
    /// </summary>
    public async Task<bool> UnattendedAsync(CancellationToken cancellationToken = default) =>
        await context.Configuration
            .AsNoTracking()
            .Select(configuration => configuration.UnattendedRefresh)
            .SingleAsync(cancellationToken);

    /// <summary>
    /// What there is to check, without checking any of it: how many scenes this
    /// library holds that the tool filed, and how many of those nothing has
    /// looked at yet.
    /// </summary>
    /// <remarks>
    /// One query over the tool's own tables and no filesystem at all — it is what
    /// the screen shows before a run, and what the timer asks before starting
    /// one.
    /// </remarks>
    public async Task<RefreshStanding> StandingAsync(CancellationToken cancellationToken = default)
    {
        var root = await context.Configuration
            .AsNoTracking()
            .Select(configuration => configuration.TargetDirectory)
            .SingleAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(root))
        {
            return RefreshStanding.Nothing;
        }

        var directories = await context.FiledVideos
            .AsNoTracking()
            .Where(row => row.LibraryRoot == root)
            .GroupBy(row => row.Directory)
            .Select(group => new { Checked = group.Min(row => row.MetadataCheckedAt) })
            .ToListAsync(cancellationToken);

        return new RefreshStanding(
            directories.Count,
            directories.Count(scene => scene.Checked is null),
            directories.Select(scene => scene.Checked).Min());
    }

    /// <summary>
    /// The scenes, least recently checked first. A row nothing has looked at
    /// sorts before every stamped one, which is what puts a library filed before
    /// this feature shipped at the front of the queue exactly once.
    /// </summary>
    /// <remarks>
    /// Grouped here rather than in SQL: one scene directory can hold several
    /// rows (ADR 0003 keeps a 1080p and a 2160p copy side by side) and the unit
    /// of this run is the directory, since that is where the two files it may
    /// write live. The rows are small and the query is bounded by the library
    /// rather than by the download directories.
    /// </remarks>
    private async Task<List<Scene>> ScenesAsync(string root, CancellationToken cancellationToken)
    {
        var rows = await context.FiledVideos
            .AsNoTracking()
            .Where(row => row.LibraryRoot == root)
            .Select(row => new
            {
                row.VideoId,
                row.Directory,
                row.FileName,
                row.MetadataCheckedAt,
            })
            .ToListAsync(cancellationToken);

        return [.. rows
            .GroupBy(row => row.Directory, StringComparer.Ordinal)
            .Select(scene => new Scene(
                scene.Key,
                Path.Combine(scene.Key, scene.First().FileName),
                scene.First().VideoId,
                scene.Min(row => row.MetadataCheckedAt)))
            .OrderBy(scene => scene.CheckedAt ?? DateTimeOffset.MinValue)
            .ThenBy(scene => scene.Directory, StringComparer.Ordinal)];
    }

    /// <summary>
    /// The run itself: look, ask about what could be written to, write what has
    /// changed.
    /// </summary>
    private async Task<RefreshReport> CheckAsync(
        Library library,
        IReadOnlyList<Scene> taking,
        int total,
        CancellationToken cancellationToken)
    {
        var looked = new List<Looked>(taking.Count);

        // Every directory this run has been to, whether it wrote there or not.
        // A scene that cannot be checked is stamped like any other: the stamp
        // says "this run got here", and a scene that never got one would sit at
        // the front of the queue forever and starve the rest of the library.
        var stamped = new List<string>(taking.Count);

        foreach (var scene in taking)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            // A row whose directory no longer holds the file it names is skipped
            // and left alone (ADR 0033). Reconciling the library with the table
            // is a different decision, and guessing at it inside a run nobody is
            // watching is how a tool deletes something.
            if (!File.Exists(scene.VideoPath))
            {
                stamped.Add(scene.Directory);

                continue;
            }

            var sidecar = sidecars.Look(ScenePathOf(scene, ScenePath.SidecarFileName));
            var image = artwork.StateOf(ScenePathOf(scene, ScenePath.ArtworkFileName));

            looked.Add(new Looked(scene, sidecar, image));
        }

        var answers = await DescribeAsync(library, looked, cancellationToken);
        var results = new List<RefreshResult>();
        var checkedScenes = 0;
        var written = 0;
        var images = 0;

        foreach (var scene in looked)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            // A scene the run never got an answer about is left unstamped, so
            // that the next run starts with it rather than with the one after it.
            if (SceneRefresh.WorthAsking(scene.Sidecar.State, scene.Image, library.Artwork)
                && !answers.Asked.Contains(scene.At.VideoId))
            {
                continue;
            }

            var result = await CarryOutAsync(library, scene, answers, cancellationToken);

            stamped.Add(scene.At.Directory);
            checkedScenes++;

            if (result is not null)
            {
                results.Add(result);
                written += result.WroteSidecar ? 1 : 0;
                images += result.WroteArtwork ? 1 : 0;
            }
        }

        await StampAsync(stamped);

        return new RefreshReport(
            checkedScenes,
            written,
            images,
            total - stamped.Count,
            results,
            cancellationToken.IsCancellationRequested
                ? "The tool stopped before it had checked everything. Nothing it had already "
                    + "written is affected, and the next run carries on where this one stopped."
                : answers.Problem);
    }

    /// <summary>
    /// One scene: what would be written, and then writing it.
    /// </summary>
    /// <returns>
    /// A row for the screen, or <c>null</c> when there is nothing to say — which
    /// is the ordinary outcome, a document that already says what prdb says.
    /// </returns>
    private async Task<RefreshResult?> CarryOutAsync(
        Library library,
        Looked scene,
        Answers answers,
        CancellationToken cancellationToken)
    {
        var metadata = answers.Of(scene.At.VideoId);

        var plan = SceneRefresh.Decide(
            scene.Sidecar.State,
            scene.Sidecar.Document,
            metadata,
            scene.Image,
            library.Artwork);

        if (!plan.Writes)
        {
            return plan.SidecarNote is null ? null : Row(scene, metadata, sidecar: plan.SidecarNote);
        }

        string? problem = null;
        var wroteSidecar = false;
        var wroteArtwork = false;
        var note = plan.SidecarNote;

        if (plan.Sidecar is { } document)
        {
            // Asked again inside Write, at the last moment before the file is
            // written over: what is at the path now is what decides, not what was
            // there when the run looked.
            var outcome = sidecars.Write(ScenePathOf(scene.At, ScenePath.SidecarFileName), document);

            wroteSidecar = outcome.Wrote;
            note = outcome.Problem ?? note;

            if (outcome.State is SidecarWriteState.Failed)
            {
                problem = outcome.Problem;
                note = null;
            }
        }

        if (plan.Artwork && metadata?.ImageUrl is { } url)
        {
            var downloaded = await artwork.DownloadAsync(
                url,
                ScenePathOf(scene.At, ScenePath.ArtworkFileName),
                cancellationToken);

            wroteArtwork = downloaded.Wrote;

            if (downloaded.State is ArtworkWriteState.Failed)
            {
                problem ??= downloaded.Problem;
            }
        }

        if (!wroteSidecar && !wroteArtwork && problem is null && note is null)
        {
            return null;
        }

        return Row(
            scene,
            metadata,
            wroteSidecar,
            wroteArtwork,
            wroteSidecar
                ? "The metadata file now says what prdb says about this scene."
                : note,
            wroteArtwork ? "An image was written where there was none." : null,
            problem);
    }

    /// <summary>
    /// What prdb says about the scenes this run could write something to, in
    /// batches of fifty.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The batch endpoint is what fixes the cost of a pass over a library at one
    /// request per fifty scenes, which is the number every bound in ADR 0032
    /// comes off. Ids prdb does not know are left out of the answer rather than
    /// failing it, and a scene it has forgotten keeps the sidecar it has.
    /// </para>
    /// <para>
    /// It stops on the quota rather than spending it, far earlier than
    /// identification does and against both windows: identification is the loop,
    /// and a nightly pass over a whole library is the first thing here that
    /// spends a month rather than an hour. What it did not ask about is what the
    /// next run starts with.
    /// </para>
    /// </remarks>
    private async Task<Answers> DescribeAsync(
        Library library,
        IReadOnlyList<Looked> looked,
        CancellationToken cancellationToken)
    {
        var wanted = looked
            .Where(scene => SceneRefresh.WorthAsking(scene.Sidecar.State, scene.Image, library.Artwork))
            .Select(scene => scene.At.VideoId)
            .Distinct()
            .ToList();

        if (wanted.Count == 0)
        {
            return Answers.Nothing;
        }

        if (library.ApiKey is not { } apiKey)
        {
            return Answers.Stopped(
                "There is no prdb API key stored, so nothing could be checked. Put one in under "
                + "Settings.");
        }

        var known = new Dictionary<Guid, SceneMetadata>();
        var asked = new HashSet<Guid>();
        RateLimitReading? quota = null;

        foreach (var batch in wanted.Chunk(IVideoLookup.MaxBatch))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            // Checked before the request rather than after the answer that
            // carried the reading, so that stopping is only ever said when there
            // is something left to stop for.
            if (Spent(quota) is { } spent)
            {
                logger.LogInformation("Stopping this refresh: {Reason}", spent);

                return new Answers(known, asked, spent);
            }

            var answer = await videos.DescribeAsync(apiKey, batch, cancellationToken);

            if (!answer.Answered)
            {
                logger.LogWarning("A refresh could not ask prdb about a batch: {Problem}", answer.Message);

                return new Answers(
                    known,
                    asked,
                    $"{answer.Message} What had already been checked is up to date; the rest is "
                        + "left for the next run.");
            }

            foreach (var video in answer.Videos)
            {
                if (SceneMetadata.From(video) is { } metadata)
                {
                    known[metadata.VideoId] = metadata;
                }
            }

            // Every id in the batch, and not only the ones that came back: a
            // video prdb has forgotten was asked about, and asking again
            // tomorrow would be spending the quota to be told the same thing.
            asked.UnionWith(batch);
            quota = answer.RateLimit;
        }

        return new Answers(known, asked);
    }

    /// <summary>
    /// Why this run should stop asking, or <c>null</c> to carry on — ADR 0032's
    /// reserves, which are what keeps a refresh from spending the hour
    /// identification needs.
    /// </summary>
    private static string? Spent(RateLimitReading? quota) => quota switch
    {
        { Remaining: { } left } when left <= RefreshSchedule.QuotaReserve =>
            "prdb's hourly quota for this key is nearly spent, so the rest of the library is left "
            + "for the next run. Nothing that was checked is affected.",
        { MonthRemaining: { } month } when month <= RefreshSchedule.MonthlyQuotaReserve =>
            "prdb's monthly quota for this key is nearly spent, so the rest of the library is left "
            + "for later. Identifying new downloads comes first.",
        _ => null,
    };

    /// <summary>
    /// Marks the scenes this run reached as looked at — including the ones it
    /// wrote nothing to, because that is what "looked at" means and what stops a
    /// scene nothing can be done about from blocking the queue forever.
    /// </summary>
    /// <remarks>
    /// No cancellation token: by the time this runs the files have been written,
    /// and a run whose stamps did not land would check the same scenes again
    /// tomorrow and never reach the rest of the library.
    /// </remarks>
    private async Task StampAsync(IReadOnlyList<string> directories)
    {
        if (directories.Count == 0)
        {
            return;
        }

        var now = time.GetUtcNow();

        foreach (var chunk in directories.Chunk(StampSize))
        {
            await context.FiledVideos
                .Where(row => chunk.Contains(row.Directory))
                .ExecuteUpdateAsync(
                    row => row.SetProperty(filed => filed.MetadataCheckedAt, now),
                    CancellationToken.None);
        }
    }

    /// <summary>
    /// The library this run is about: where it is, whether images may be
    /// written, and the key to ask with.
    /// </summary>
    private async Task<Library> ReadLibraryAsync(CancellationToken cancellationToken)
    {
        var configuration = await context.Configuration.AsNoTracking().SingleAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(configuration.TargetDirectory))
        {
            return new Library(null, false, null, "There is no library directory set up yet.");
        }

        return new Library(
            configuration.TargetDirectory,
            configuration.DownloadArtwork,
            string.IsNullOrWhiteSpace(configuration.PrdbApiKey) ? null : configuration.PrdbApiKey);
    }

    private static string ScenePathOf(Scene scene, string fileName) =>
        Path.Combine(scene.Directory, fileName);

    private static RefreshResult Row(
        Looked scene,
        SceneMetadata? metadata,
        bool wroteSidecar = false,
        bool wroteArtwork = false,
        string? sidecar = null,
        string? image = null,
        string? problem = null) =>
        new(
            scene.At.Directory,
            scene.At.VideoPath,
            Path.GetFileName(scene.At.Directory),
            scene.At.VideoId,
            metadata?.Title,
            wroteSidecar,
            wroteArtwork,
            sidecar,
            image,
            problem);

    /// <summary>One scene directory, as the database knows it.</summary>
    private sealed record Scene(
        string Directory,
        string VideoPath,
        Guid VideoId,
        DateTimeOffset? CheckedAt);

    /// <summary>And as the filesystem answered for it, a moment before prdb was asked.</summary>
    private sealed record Looked(Scene At, SidecarLook Sidecar, ArtworkState Image);

    /// <param name="Asked">
    /// The videos this run got an answer about, whether or not prdb still knows
    /// them. A scene outside this set was never asked about, and is left
    /// unstamped so that the next run begins there.
    /// </param>
    private sealed record Answers(
        IReadOnlyDictionary<Guid, SceneMetadata> Known,
        IReadOnlySet<Guid> Asked,
        string? Problem = null)
    {
        public static readonly Answers Nothing =
            new(new Dictionary<Guid, SceneMetadata>(), new HashSet<Guid>());

        public static Answers Stopped(string problem) => Nothing with { Problem = problem };

        public SceneMetadata? Of(Guid videoId) => Known.GetValueOrDefault(videoId);
    }

    private sealed record Library(
        string? Root,
        bool Artwork,
        string? ApiKey,
        string? Problem = null);
}

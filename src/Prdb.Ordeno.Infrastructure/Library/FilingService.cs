using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Ordeno.Core.Configuration;
using Prdb.Ordeno.Core.History;
using Prdb.Ordeno.Core.Identification;
using Prdb.Ordeno.Core.Library;
using Prdb.Ordeno.Core.Review;
using Prdb.Ordeno.Core.Scanning;
using Prdb.Ordeno.Infrastructure.History;
using Prdb.Ordeno.Infrastructure.Persistence;

namespace Prdb.Ordeno.Infrastructure.Library;

/// <summary>
/// The third step of the loop in <c>VISION.md</c>: what has been found and
/// recognised goes where the layout says.
/// </summary>
/// <remarks>
/// <para>
/// Two entry points that are the same code. <see cref="PlanAsync"/> works out
/// what would happen and touches nothing; <see cref="FileAsync"/> works the same
/// thing out again and carries it out. ADR 0022 turns on that being one
/// computation rather than two that agree, and on the second one being made
/// fresh: a directory can be occupied in the seconds between reading a screen
/// and pressing a button.
/// </para>
/// <para>
/// Nothing here decides where a file goes. That is <see cref="FilingPlanner"/>,
/// which writes nothing, and <see cref="LibraryMoves"/>, which decides nothing.
/// What this adds is the database either side of them: which files are worth
/// planning for, and what the library holds once one of them has moved.
/// </para>
/// </remarks>
public sealed class FilingService(
    OrdenoDbContext context,
    IDirectoryInspector inspector,
    IVideoQualities qualities,
    IVideoLookup videos,
    FilingPlanner planner,
    LibraryMoves moves,
    Sidecars sidecars,
    SceneArtwork artwork,
    OperationLog log,
    TimeProvider time,
    ILogger<FilingService> logger)
{
    /// <summary>
    /// How many videos are looked up at a time when asking what the library
    /// already holds of them. The same reason the scan batches: a parameter list
    /// has a limit, and a first pass over a library is thousands of files.
    /// </summary>
    private const int BatchSize = 500;

    /// <summary>
    /// What would happen, without anything happening. This is what the user
    /// reads before pressing the button.
    /// </summary>
    public async Task<FilingPreview> PlanAsync(CancellationToken cancellationToken = default)
    {
        var library = await ReadLibraryAsync(cancellationToken);

        if (library.Problem is not null)
        {
            return new FilingPreview([], library.Problem);
        }

        var candidates = await CandidatesAsync(cancellationToken);
        var filed = await FiledAsync(library.Root!, candidates, cancellationToken);
        var plans = new List<FilingPlan>(candidates.Count);

        foreach (var candidate in candidates)
        {
            plans.Add(await PlanForAsync(library, candidate, filed, cancellationToken));
        }

        return new FilingPreview(plans);
    }

    /// <summary>
    /// Carries the plan out, one file at a time, working each one out again as
    /// it gets to it.
    /// </summary>
    /// <remarks>
    /// One file at a time on purpose. A cross-filesystem move is minutes of
    /// copying, and doing several at once on a NAS turns one slow filing into
    /// several slower ones while somebody is trying to watch something off the
    /// same disks.
    /// </remarks>
    public async Task<FilingReport> FileAsync(CancellationToken cancellationToken = default)
    {
        // Opened before anything is looked at, so that a run refused by an
        // unusable library leaves the same trace as one that filed two hundred
        // files. "You asked, and this is why nothing happened" is an answer
        // somebody who was asleep needs as much as the other one.
        var runId = await log.StartAsync(RunKind.Filing);
        var library = await ReadLibraryAsync(cancellationToken);

        if (library.Problem is not null)
        {
            await log.FinishAsync(runId, account: null, library.Problem);

            return new FilingReport([], library.Problem);
        }

        // What a container killed mid-copy left behind, before anything new is
        // written next to it.
        moves.ClearStaging(library.Root!);

        var candidates = await CandidatesAsync(cancellationToken);
        var filed = await FiledAsync(library.Root!, candidates, cancellationToken);
        var described = await DescribeAsync(library, candidates, cancellationToken);
        var results = new List<FilingResult>(candidates.Count);

        for (var index = 0; index < candidates.Count; index++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                // The container is stopping. Whatever was in flight either
                // finished or left the original where it was; what is left is
                // reported as not reached rather than silently dropped.
                results.AddRange(candidates.Skip(index).Select(NotReached));

                var stopped = new FilingReport(results, "Filing was stopped before it finished.");
                await log.FinishAsync(runId, stopped.Account, stopped.Problem);

                return stopped;
            }

            results.Add(await CarryOutAsync(
                runId,
                library,
                candidates[index],
                filed,
                described,
                cancellationToken));
        }

        var report = new FilingReport(results);

        // Closed with what the screen says, and the log is trimmed behind it —
        // ADR 0028, where a trim happens after a run and never inside one.
        await log.FinishAsync(runId, report.Account);

        return report;
    }

    private async Task<FilingResult> CarryOutAsync(
        int runId,
        Library library,
        Candidate candidate,
        Dictionary<Guid, List<FiledCopy>> filed,
        Descriptions described,
        CancellationToken cancellationToken)
    {
        // Worked out again, now, rather than taken from the preview: ADR 0022.
        var plan = await PlanForAsync(library, candidate, filed, cancellationToken);

        if (!plan.Moves)
        {
            return new FilingResult(FilingResultState.Skipped, plan, plan.Message);
        }

        try
        {
            // ADR 0020: the file that is already filed carries a label before
            // the second quality is put next to it, and if that does not happen
            // then nothing else does either. The other order would leave a
            // directory where only half of what is in it is labelled.
            if (plan.Relabel is { } relabel)
            {
                var renamed = moves.Relabel(relabel.From, relabel.To);

                if (!renamed.Moved)
                {
                    return new FilingResult(FilingResultState.Failed, plan, renamed.Problem);
                }

                await RecordRelabelAsync(runId, relabel, plan, candidate.Reason);
            }

            var outcome = await moves.FileAsync(
                plan.SourcePath,
                plan.TargetPath!,
                library.Root!,
                plan.Movement,
                cancellationToken);

            if (!outcome.Moved)
            {
                return new FilingResult(FilingResultState.Failed, plan, outcome.Problem);
            }

            var operationId = await RecordFilingAsync(runId, library, plan, candidate, outcome, filed);

            // Last, and only now: the video is where the sidecar describes it,
            // and a sidecar that fails to be written costs the library a title
            // rather than a file. The image goes after both, because it is the
            // one of the three that spends somebody's connection — and it is the
            // one that can be left out entirely without anybody noticing.
            var sidecar = WriteSidecar(plan, described);
            var image = await WriteArtworkAsync(plan, described, cancellationToken);

            // What went in next to the video is added to the entry now rather
            // than written with it, because it happens after the move and the
            // entry is written with the move. A container killed in between
            // leaves an entry that knows about a file it does not mention, and
            // an undo that leaves that file alone — which is the safe half of
            // the two.
            await log.WroteAsync(operationId, sidecar.Written, image.Written);

            return new FilingResult(
                FilingResultState.Filed,
                plan,
                outcome.Problem,
                sidecar.Message,
                image.Message);
        }
        catch (OperationCanceledException)
        {
            return new FilingResult(
                FilingResultState.Stopped,
                plan,
                "The tool was asked to stop while this file was being moved. It was left exactly "
                + "as it was.");
        }
        catch (Exception exception)
        {
            // One file that goes wrong in a way nothing foresaw is one file. A
            // run that stops here would leave the rest of somebody's library
            // unfiled and no report of why.
            logger.LogError(exception, "Filing {Path} failed.", plan.SourcePath);

            return new FilingResult(
                FilingResultState.Failed,
                plan,
                "Something went wrong while filing this one. The container's log has the details.");
        }
    }

    private async Task<FilingPlan> PlanForAsync(
        Library library,
        Candidate candidate,
        Dictionary<Guid, List<FiledCopy>> filed,
        CancellationToken cancellationToken)
    {
        var scene = candidate.Scene;
        var quality = await qualities.ReadAsync(candidate.Path, cancellationToken);

        return planner.Plan(
            candidate.Id,
            candidate.Path,
            candidate.Name,
            library.Root!,
            library.MovementFrom(candidate.Path, inspector),
            scene,
            quality,
            scene is null ? [] : filed.GetValueOrDefault(scene.VideoId, []),
            library.Artwork);
    }

    /// <summary>
    /// The sidecar, once the video it describes is in place.
    /// </summary>
    /// <returns>
    /// What to tell the user about it, and what was written where — the second
    /// half for the operation log, which is what lets an undo take away a
    /// sidecar this run put in a directory and nothing else.
    /// </returns>
    /// <remarks>
    /// Nothing here can undo the move above, and nothing here is allowed to try.
    /// A video in the library with no sidecar shows its file name until the next
    /// filing writes one, which is a state the media server handles; a filing
    /// reported as failed because a small file could not be written would leave
    /// the user looking for a video that is already filed.
    /// </remarks>
    private SidecarWriting WriteSidecar(FilingPlan plan, Descriptions described)
    {
        if (!plan.Sidecar.Writes)
        {
            // Somebody else's, or unreadable. The planner has already said so in
            // words, and the row after the run says the same thing.
            return new SidecarWriting(plan.Sidecar.Message);
        }

        if (described.Of(plan.Scene!.VideoId) is not { } metadata)
        {
            // Nothing is written on the strength of a partial or failed lookup.
            // The absence of a sidecar is a state the media server handles; one
            // built from half an answer is a state it reads and believes.
            return new SidecarWriting(described.Problem
                ?? "prdb no longer knows the video this file was recognised as, so no "
                    + $"'{ScenePath.SidecarFileName}' was written next to it. The video is filed.");
        }

        var written = sidecars.Write(plan.Sidecar.Path!, MovieNfo.For(metadata));

        return new SidecarWriting(
            written.Problem,
            written.Wrote ? new WrittenSidecar(plan.Sidecar.Path!) : null);
    }

    /// <summary>
    /// The image, once the video it belongs to is in place — ADR 0027.
    /// </summary>
    /// <returns>
    /// What to tell the user about it, or <c>null</c> when there is nothing to
    /// say. Silent in three cases and not only in the obvious one: the image
    /// arrived, artwork is switched off, and prdb has no image for this scene.
    /// The last is the ordinary outcome for a scene nobody has photographed, and
    /// a warning under it would turn that into a problem.
    /// </returns>
    /// <remarks>
    /// Nothing here can undo the move above, and nothing here is allowed to try —
    /// the same rule as the sidecar, one step further from mattering. An item
    /// with no image is what section 5 of the layout document measured the
    /// library against.
    /// </remarks>
    private async Task<ArtworkWriting> WriteArtworkAsync(
        FilingPlan plan,
        Descriptions described,
        CancellationToken cancellationToken)
    {
        if (!plan.Artwork.Writes)
        {
            // Off, or there is a file at that name already. Either way the plan
            // said so before the run started.
            return new ArtworkWriting(plan.Artwork.Message);
        }

        // Nothing is downloaded on the strength of a partial or failed lookup,
        // and nothing is said about it either: the sidecar's own message on this
        // row already carries the reason prdb could not be asked.
        if (described.Of(plan.Scene!.VideoId)?.ImageUrl is not { } url)
        {
            return ArtworkWriting.Nothing;
        }

        var downloaded = await artwork.DownloadAsync(url, plan.Artwork.Path!, cancellationToken);

        // The length and the fingerprint go into the log because ADR 0027 left
        // the image itself unmarked. They are what an undo compares before it
        // removes one, and nothing else ever reads them.
        return new ArtworkWriting(
            downloaded.Problem,
            downloaded is { Wrote: true, Bytes: { } bytes, Fingerprint: { } fingerprint }
                ? new WrittenArtwork(plan.Artwork.Path!, bytes, fingerprint)
                : null);
    }

    /// <summary>
    /// What prdb says the scenes about to be filed are, asked now rather than
    /// read off the identification rows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The stored answer is for putting a name on a screen (ADR 0017). What goes
    /// into a sidecar is fetched when the sidecar is written, because that answer
    /// may be months old and a corrected title is most of what a user came here
    /// for.
    /// </para>
    /// <para>
    /// Once for the whole run, in batches of fifty, before anything moves: a
    /// library of thousands costs a handful of requests rather than one per file.
    /// It asks about every candidate rather than only the ones that turn out to
    /// move, because which ones those are is worked out file by file as the run
    /// reaches them — and finding out first would mean reading the header of
    /// every video twice.
    /// </para>
    /// <para>
    /// prdb being unreachable does not stop the run. The videos are filed and
    /// carry no sidecar, and each row says so.
    /// </para>
    /// </remarks>
    private async Task<Descriptions> DescribeAsync(
        Library library,
        IReadOnlyList<Candidate> candidates,
        CancellationToken cancellationToken)
    {
        var wanted = candidates
            .Select(candidate => candidate.Scene?.VideoId)
            .OfType<Guid>()
            .Distinct()
            .ToList();

        if (wanted.Count == 0)
        {
            return Descriptions.Nothing;
        }

        if (library.ApiKey is not { } apiKey)
        {
            return Descriptions.Stopped(
                "There is no prdb API key stored, so nothing could be asked about the scenes and "
                + $"no '{ScenePath.SidecarFileName}' was written. The videos are filed.");
        }

        var known = new Dictionary<Guid, SceneMetadata>();

        foreach (var batch in wanted.Chunk(IVideoLookup.MaxBatch))
        {
            var answer = await videos.DescribeAsync(apiKey, batch, cancellationToken);

            if (!answer.Answered)
            {
                logger.LogWarning(
                    "Filing could not ask prdb what it is filing: {Problem}",
                    answer.Message);

                return new Descriptions(
                    known,
                    $"{answer.Message} Nothing was written next to it: the video is in the library, "
                    + "and the next filing into that scene writes the metadata file.");
            }

            foreach (var video in answer.Videos)
            {
                // An answer with no title is left out rather than written badly,
                // and the row it belongs to says the video was filed without one.
                if (SceneMetadata.From(video) is { } metadata)
                {
                    known[metadata.VideoId] = metadata;
                }
            }
        }

        return new Descriptions(known);
    }

    /// <summary>
    /// The library now holds this file. The row is what the next filing of the
    /// same scene reads in order to tell a second quality from a second copy.
    /// </summary>
    /// <remarks>
    /// Nothing here takes the run's cancellation token, and that is the point:
    /// by the time this is called the file has moved, and a shutdown arriving
    /// now must not stop the tool writing down where it went. A library holding
    /// a video no row knows about is worse than a slightly late shutdown — the
    /// next run would see an occupied directory it cannot account for and file
    /// the scene around it, under a name carrying prdb's id, for a reason
    /// nobody could see.
    /// </remarks>
    private async Task<int> RecordFilingAsync(
        int runId,
        Library library,
        FilingPlan plan,
        Candidate candidate,
        MoveOutcome outcome,
        Dictionary<Guid, List<FiledCopy>> filed)
    {
        // Not the run's token, for the reason above.
        var whateverHappens = CancellationToken.None;

        var videoId = plan.Scene!.VideoId;
        var directory = plan.Directory!;
        var fileName = System.IO.Path.GetFileName(plan.TargetPath!);

        if (plan.Outcome is not FilingOutcome.SecondQuality)
        {
            // The planner found no copy of this scene still on disk, so any rows
            // saying otherwise describe files the user has since moved or
            // deleted. They are not history — this table says what is true now,
            // and the operation log (#19) is what keeps the rest.
            await context.FiledVideos
                .Where(row => row.VideoId == videoId && row.LibraryRoot == library.Root)
                .ExecuteDeleteAsync(whateverHappens);
        }

        // And whatever any other row claimed about this exact path, the file
        // that is at it now is this one. Such a row is stale by definition — the
        // planner would not have filed here if the file it named were still
        // there — and leaving it would make the insert below collide with a
        // record of something that no longer exists.
        await context.FiledVideos
            .Where(row => row.Directory == directory && row.FileName == fileName)
            .ExecuteDeleteAsync(whateverHappens);

        context.FiledVideos.Add(new FiledVideo
        {
            VideoId = videoId,
            LibraryRoot = library.Root!,
            Directory = directory,
            FileName = fileName,
            QualityLabel = plan.QualityLabel!,
            FiledAt = time.GetUtcNow(),
        });

        // In the same statement as the row above, which is ADR 0028's one hard
        // requirement of this path: the record that the library holds the file
        // and the record of how it got there are written together or not at all.
        var entry = log.Filed(
            runId,
            plan,
            candidate.Reason,
            candidate.SizeBytes,
            candidate.OsHash,
            outcome.CreatedDirectory);

        // The file is not in the download directory any more, so neither is the
        // tool's memory of having seen it there. Waiting for the next scan to
        // notice would leave a row that a second filing run would try to move
        // again and report as missing.
        await context.DiscoveredFiles
            .Where(file => file.Id == plan.FileId)
            .ExecuteDeleteAsync(whateverHappens);

        await context.SaveChangesAsync(whateverHappens);
        context.ChangeTracker.Clear();

        var copies = filed.TryGetValue(videoId, out var existing) ? existing : filed[videoId] = [];

        if (plan.Outcome is not FilingOutcome.SecondQuality)
        {
            copies.Clear();
        }

        copies.Add(new FiledCopy(videoId, directory, fileName, plan.QualityLabel!));

        return entry.Id;
    }

    /// <summary>
    /// The file that was already filed is called something else now (ADR 0020),
    /// and the row that says where this scene lives has to say so too — before
    /// the second quality lands, so that an interruption between the two leaves
    /// a record that matches the disk. Like the row below, it is written whether
    /// or not the run has been asked to stop: the rename has already happened.
    /// </summary>
    private async Task RecordRelabelAsync(
        int runId,
        FilingRelabel relabel,
        FilingPlan plan,
        OperationReason reason)
    {
        var directory = System.IO.Path.GetDirectoryName(relabel.From)!;
        var was = System.IO.Path.GetFileName(relabel.From);
        var now = System.IO.Path.GetFileName(relabel.To);

        await context.FiledVideos
            .Where(row => row.Directory == directory && row.FileName == was)
            .ExecuteUpdateAsync(row => row.SetProperty(filed => filed.FileName, now), CancellationToken.None);

        // Its own entry in the log, and written here rather than with the filing
        // that caused it: ADR 0020 asked for that, because an undo that returns
        // the newcomer and leaves this rename in place is half an undo. It is
        // undone after the newcomer leaves, which is what reading a run
        // backwards gives for nothing.
        log.Relabelled(runId, relabel, plan.Scene, reason);

        await context.SaveChangesAsync(CancellationToken.None);
        context.ChangeTracker.Clear();
    }

    /// <summary>
    /// Everything that has finished downloading and that something named a video
    /// for: a person, or failing that prdb.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two sources of truth in a fixed order — ADR 0023. A person's answer
    /// outranks prdb's wherever both exist, and a file somebody dismissed is
    /// named by neither: it is here, it is settled, and it stays where it is.
    /// </para>
    /// <para>
    /// A file nothing has named is the review queue's
    /// (<see href="https://github.com/prdb-net/prdb-ordeno/issues/16">#16</see>)
    /// and never appears here — ADR 0019.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<Candidate>> CandidatesAsync(CancellationToken cancellationToken)
    {
        var settled = Settling.SettledIfUnchangedSince(time.GetUtcNow());

        var sources = await context.SourceDirectories
            .AsNoTracking()
            .ToDictionaryAsync(source => source.Id, source => source.Path, cancellationToken);

        var rows = await (
            from file in context.DiscoveredFiles.AsNoTracking()
            join answer in context.FileIdentifications.AsNoTracking()
                on file.Id equals answer.DiscoveredFileId into answers
            from identification in answers.DefaultIfEmpty()
            join written in context.FileResolutions.AsNoTracking()
                on file.Id equals written.DiscoveredFileId into decisions
            from decision in decisions.DefaultIfEmpty()
            where file.SizeBytes > 0 && file.UnchangedSince <= settled
            where decision == null
                ? identification != null && identification.VideoId != null
                : decision.Kind == ResolutionKind.Assigned
            orderby file.Id
            select new
            {
                file.Id,
                file.SourceDirectoryId,
                file.Path,
                file.SizeBytes,
                file.OsHash,
                Recognition = identification == null
                    ? null
                    : new Recognition(
                        identification.Confidence,
                        identification.MatchedBy,
                        identification.VideoId,
                        identification.Title,
                        identification.ReleaseDate,
                        identification.SiteTitle,
                        identification.Candidates.Count,
                        identification.AskedAt),
                Decision = decision == null
                    ? null
                    : new Resolution(
                        decision.Kind,
                        decision.From,
                        decision.DecidedAt,
                        decision.VideoId,
                        decision.Title,
                        decision.ReleaseDate,
                        decision.SiteTitle),
            }).ToListAsync(cancellationToken);

        return
        [
            .. rows.Select(row => new Candidate(
                row.Id,
                row.Path,
                Below(sources.GetValueOrDefault(row.SourceDirectoryId), row.Path),
                Named(row.Recognition, row.Decision),
                row.SizeBytes,
                row.OsHash,
                // What the log records as the reason, read in the same order and
                // from the same two answers as the name above — ADR 0023 and
                // ADR 0028. It travels with the candidate because the rows it
                // comes from are deleted the moment the file is filed.
                OperationReason.From(row.Recognition, row.Decision))),
        ];
    }

    /// <summary>
    /// What this file is, in the order ADR 0023 fixes: what a person decided,
    /// and only then what prdb answered.
    /// </summary>
    private static Scene? Named(Recognition? recognition, Resolution? decision) =>
        decision is not null
            ? Scene.From(decision)
            : recognition is null ? null : Scene.From(recognition);

    /// <summary>
    /// What the library already holds of the scenes these files were recognised
    /// as. One lookup for the whole run rather than one per file, and none at
    /// all on a library that has never filed anything.
    /// </summary>
    private async Task<Dictionary<Guid, List<FiledCopy>>> FiledAsync(
        string libraryRoot,
        IReadOnlyList<Candidate> candidates,
        CancellationToken cancellationToken)
    {
        var wanted = candidates
            .Select(candidate => candidate.Scene?.VideoId)
            .OfType<Guid>()
            .Distinct()
            .ToList();

        var copies = new Dictionary<Guid, List<FiledCopy>>();

        foreach (var batch in wanted.Chunk(BatchSize))
        {
            var rows = await context.FiledVideos
                .AsNoTracking()
                .Where(row => row.LibraryRoot == libraryRoot && batch.Contains(row.VideoId))
                .OrderBy(row => row.Id)
                .ToListAsync(cancellationToken);

            foreach (var row in rows)
            {
                if (!copies.TryGetValue(row.VideoId, out var list))
                {
                    copies[row.VideoId] = list = [];
                }

                list.Add(new FiledCopy(row.VideoId, row.Directory, row.FileName, row.QualityLabel));
            }
        }

        return copies;
    }

    private async Task<Library> ReadLibraryAsync(CancellationToken cancellationToken)
    {
        var configuration = await context.Configuration.AsNoTracking().SingleAsync(cancellationToken);

        if (configuration.OnboardingCompletedAt is null || configuration.TargetDirectory is null)
        {
            return new Library(null, "Nothing is filed until the setup is finished.");
        }

        var inspection = inspector.Inspect(configuration.TargetDirectory, DirectoryRole.Target);

        return inspection.Usable
            ? new Library(
                inspection.Path,
                null,
                string.IsNullOrWhiteSpace(configuration.PrdbApiKey) ? null : configuration.PrdbApiKey,
                configuration.DownloadArtwork)
            : new Library(null, $"Nothing can be filed while the library is unusable: {inspection.Message}");
    }

    private FilingResult NotReached(Candidate candidate) =>
        new(
            FilingResultState.Stopped,
            FilingPlan.Blocked(
                candidate.Id,
                candidate.Path,
                candidate.Name,
                candidate.Scene,
                "The tool was asked to stop before this one was reached. Nothing happened to it."),
            "Not reached before the tool stopped.");

    /// <summary>The path below its source directory, which is what a person recognises.</summary>
    private static string Below(string? source, string path) =>
        source is not null && path.StartsWith(source, StringComparison.Ordinal)
            ? path[source.Length..].TrimStart(
                System.IO.Path.DirectorySeparatorChar,
                System.IO.Path.AltDirectorySeparatorChar)
            : System.IO.Path.GetFileName(path);

    /// <param name="Scene">
    /// What this file is, or <c>null</c> when what named it does not amount to
    /// one — which the planner turns into a reason rather than a move.
    /// </param>
    /// <param name="SizeBytes">
    /// What the scan measured. It goes into the log, where it is half of how an
    /// undo tells the file it filed from one that has changed since.
    /// </param>
    /// <param name="OsHash">The exact hash the scan read, where there was one. The other half.</param>
    /// <param name="Reason">Why the tool believes this file is that scene.</param>
    private sealed record Candidate(
        int Id,
        string Path,
        string Name,
        Scene? Scene,
        long SizeBytes,
        string? OsHash,
        OperationReason Reason);

    /// <param name="Written">
    /// What went in next to the video, for the log — <c>null</c> when nothing
    /// did, whether because the plan said so or because it could not be written.
    /// </param>
    private sealed record SidecarWriting(string? Message, WrittenSidecar? Written = null);

    /// <param name="Written">The same for the image, which is the one with a fingerprint on it.</param>
    private sealed record ArtworkWriting(string? Message, WrittenArtwork? Written = null)
    {
        public static readonly ArtworkWriting Nothing = new(Message: null);
    }

    /// <summary>
    /// What prdb said about the scenes this run is filing.
    /// </summary>
    /// <param name="Problem">
    /// Why some of it is missing, in words a row can carry. <c>null</c> when
    /// everything that could be asked about was.
    /// </param>
    private sealed record Descriptions(IReadOnlyDictionary<Guid, SceneMetadata> Known, string? Problem = null)
    {
        public static readonly Descriptions Nothing = new(new Dictionary<Guid, SceneMetadata>());

        /// <summary>Nothing came back, and this is what to say about it.</summary>
        public static Descriptions Stopped(string problem) => Nothing with { Problem = problem };

        public SceneMetadata? Of(Guid videoId) => Known.GetValueOrDefault(videoId);
    }

    /// <param name="Root">Where the library is, or <c>null</c> when it cannot be filed into.</param>
    /// <param name="ApiKey">
    /// The stored key, because what a sidecar says is asked for at the moment it
    /// is written rather than remembered from the identification.
    /// </param>
    /// <param name="Artwork">
    /// Whether somebody switched artwork on (ADR 0027), read once for the run
    /// like everything else here. False for an installation that never touched
    /// the setting, which is what off means.
    /// </param>
    private sealed record Library(
        string? Root,
        string? Problem,
        string? ApiKey = null,
        bool Artwork = false)
    {
        private readonly Dictionary<string, FileMovement> movements = [];

        /// <summary>
        /// Whether a video from this directory is renamed or copied, asked once
        /// per download directory. Working it out reads the mount table, and a
        /// first pass over a library would otherwise read it a thousand times to
        /// get a thousand identical answers.
        /// </summary>
        public FileMovement MovementFrom(string filePath, IDirectoryInspector inspector)
        {
            var directory = System.IO.Path.GetDirectoryName(filePath) ?? filePath;

            if (!movements.TryGetValue(directory, out var movement))
            {
                movements[directory] = movement = inspector.MovementBetween(directory, Root!);
            }

            return movement;
        }
    }
}

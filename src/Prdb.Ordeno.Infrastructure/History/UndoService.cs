using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Ordeno.Core.Configuration;
using Prdb.Ordeno.Core.History;
using Prdb.Ordeno.Core.Identification;
using Prdb.Ordeno.Core.Library;
using Prdb.Ordeno.Infrastructure.Library;
using Prdb.Ordeno.Infrastructure.Persistence;

namespace Prdb.Ordeno.Infrastructure.History;

/// <summary>
/// The way back — ADR 0029.
/// </summary>
/// <remarks>
/// <para>
/// Two entry points that are the same code, exactly as filing has:
/// <see cref="CheckAsync"/> works out what would happen and touches nothing, and
/// <see cref="UndoAsync"/> works the same thing out again, one file at a time,
/// as it reaches each of them. A file can be renamed, replaced or removed in the
/// seconds between reading a screen and pressing a button.
/// </para>
/// <para>
/// Nothing here decides whether a file may go back — <see cref="UndoPlanner"/>
/// does that and writes nothing — and nothing here moves a file itself:
/// <see cref="LibraryMoves"/> does, by the same rules it moved it there with.
/// </para>
/// </remarks>
public sealed class UndoService(
    OrdenoDbContext context,
    IDirectoryInspector inspector,
    LibraryMoves moves,
    Sidecars sidecars,
    SceneArtwork artwork,
    IFileHashes hashes,
    OperationLog log,
    TimeProvider time,
    ILogger<UndoService> logger)
{
    /// <summary>
    /// What undoing a run, or one operation of one, would do. Nothing is
    /// touched.
    /// </summary>
    public async Task<UndoPreview> CheckAsync(
        int? runId,
        int? operationId,
        CancellationToken cancellationToken = default)
    {
        var entries = await EntriesAsync(runId, operationId, cancellationToken);

        if (entries.Problem is not null)
        {
            return new UndoPreview([], entries.Problem);
        }

        var plans = new List<UndoPlan>(entries.Operations.Count);

        foreach (var operation in entries.Operations)
        {
            plans.Add(UndoPlanner.Plan(operation, await ObserveAsync(operation, cancellationToken)));
        }

        return new UndoPreview(plans);
    }

    /// <summary>
    /// Puts a run, or one operation of one, back — one file at a time, working
    /// each one out again as it gets to it.
    /// </summary>
    /// <remarks>
    /// One refusal does not stop the ones after it (ADR 0029): a partial undo is
    /// reported rather than hidden, and stopping at the first file somebody had
    /// moved by hand would leave the other hundred and ninety in the library with
    /// no explanation.
    /// </remarks>
    public async Task<UndoReport> UndoAsync(
        int? runId,
        int? operationId,
        CancellationToken cancellationToken = default)
    {
        var entries = await EntriesAsync(runId, operationId, cancellationToken);

        if (entries.Problem is not null)
        {
            return new UndoReport([], entries.Problem);
        }

        // What a container killed mid-copy left behind in the directories these
        // files are going back to, before anything new is written next to it.
        // Once per directory rather than per file: a download directory holds
        // thousands of entries and this lists it.
        foreach (var directory in entries.Operations
            .Select(operation => System.IO.Path.GetDirectoryName(operation.From))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal))
        {
            moves.ClearReturning(directory);
        }

        var undoRunId = await log.StartAsync(RunKind.Undo);
        var results = new List<UndoResult>(entries.Operations.Count);

        for (var index = 0; index < entries.Operations.Count; index++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                results.AddRange(entries.Operations.Skip(index).Select(NotReached));

                var stopped = new UndoReport(results, "The undo was stopped before it finished.");
                await log.FinishAsync(undoRunId, Account(results), stopped.Problem);

                return stopped;
            }

            results.Add(await CarryOutAsync(undoRunId, entries.Operations[index], cancellationToken));
        }

        var report = new UndoReport(results);
        await log.FinishAsync(undoRunId, Account(results));

        return report;
    }

    private async Task<UndoResult> CarryOutAsync(
        int undoRunId,
        LoggedOperation operation,
        CancellationToken cancellationToken)
    {
        // Worked out again, now, rather than taken from the check: the same rule
        // ADR 0022 puts on filing, for the same reason.
        var plan = UndoPlanner.Plan(operation, await ObserveAsync(operation, cancellationToken));

        if (!plan.Returns)
        {
            return new UndoResult(UndoResultState.Refused, plan, plan.Message);
        }

        try
        {
            // Asked of the filesystem rather than read off the entry: what was a
            // rename when the file was filed is a copy today if the user has
            // remounted the library somewhere else, and the careful path is the
            // one to be wrong towards.
            var movement = inspector.MovementBetween(
                System.IO.Path.GetDirectoryName(operation.To)!,
                System.IO.Path.GetDirectoryName(operation.From)!);

            var outcome = await moves.ReturnAsync(
                operation.To,
                operation.From,
                movement,
                cancellationToken);

            if (!outcome.Moved)
            {
                return new UndoResult(UndoResultState.Failed, plan, outcome.Problem);
            }

            // The rows first, then what was written next to the video: the
            // question "is anything of this scene still filed here" is answered
            // by the table, and the answer has to be the one after this file
            // left.
            await RecordAsync(undoRunId, operation);

            return new UndoResult(
                UndoResultState.Returned,
                plan,
                plan.Message,
                await TakeAwayAsync(operation));
        }
        catch (OperationCanceledException)
        {
            return new UndoResult(
                UndoResultState.Stopped,
                plan,
                "The tool was asked to stop while this file was being moved back. It was left "
                + "exactly as it was.");
        }
        catch (Exception exception)
        {
            // One file that goes wrong in a way nothing foresaw is one file. A
            // run that stopped here would leave the rest of a batch in the
            // library and no report of why.
            logger.LogError(exception, "Undoing {Path} failed.", operation.To);

            return new UndoResult(
                UndoResultState.Failed,
                plan,
                "Something went wrong while putting this one back. The container's log has the "
                + "details.");
        }
    }

    /// <summary>
    /// What is at the path the operation filed to, and what is where it would go
    /// back to.
    /// </summary>
    private async Task<UndoObservation> ObserveAsync(
        LoggedOperation operation,
        CancellationToken cancellationToken)
    {
        var renamedBy = await RenamedByAsync(operation, cancellationToken);

        try
        {
            var found = new FileInfo(operation.To);

            if (!found.Exists)
            {
                return new UndoObservation(
                    System.IO.Directory.Exists(operation.To)
                        ? FiledFileState.Unreadable
                        : FiledFileState.Missing,
                    RenamedBy: renamedBy);
            }

            return new UndoObservation(
                FiledFileState.Present,
                found.Length,
                // Only where the entry has one to compare against. Reading
                // 128 KiB to compare it with nothing is two hundred pointless
                // reads on a run of two hundred.
                operation.OsHash is null ? null : hashes.OsHashOf(operation.To).Hash,
                Occupied(operation.From),
                System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(operation.From)),
                renamedBy);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Could not look at {Path} to put it back.", operation.To);

            return new UndoObservation(FiledFileState.Unreadable, RenamedBy: renamedBy);
        }
    }

    /// <summary>
    /// Whether a later run took the name this operation put a file at — which is
    /// what a relabel does to the file it renames.
    /// </summary>
    /// <remarks>
    /// The log answers this rather than the filesystem, and it is the reason the
    /// way back is chronological in reverse between runs as well as inside one
    /// (ADR 0029). Whatever is at that path now belongs to the later run, and
    /// undoing that one first is what makes this one possible.
    /// </remarks>
    private async Task<string?> RenamedByAsync(
        LoggedOperation operation,
        CancellationToken cancellationToken)
    {
        var later = await (
            from entry in context.Operations.AsNoTracking()
            join run in context.OperationRuns.AsNoTracking() on entry.RunId equals run.Id
            where entry.Id > operation.Id && entry.FromPath == operation.To && entry.UndoneAt == null
            orderby entry.Id
            select new { run.Id, run.StartedAt }).FirstOrDefaultAsync(cancellationToken);

        return later is null
            ? null
            : $"the run of {later.StartedAt.UtcDateTime:yyyy-MM-dd HH:mm} UTC";
    }

    /// <summary>
    /// What the library says about this file now that it has gone: the row that
    /// said it was filed, and the entry that said what happened to it.
    /// </summary>
    /// <remarks>
    /// None of it takes the run's cancellation token, for the reason
    /// <c>FilingService</c> does not either: the file has already moved, and a
    /// shutdown arriving now must not stop the tool writing down that it did.
    /// </remarks>
    private async Task RecordAsync(int undoRunId, LoggedOperation operation)
    {
        var whateverHappens = CancellationToken.None;
        var directory = System.IO.Path.GetDirectoryName(operation.To)!;
        var fileName = System.IO.Path.GetFileName(operation.To);

        if (operation.Kind is OperationKind.Relabelled)
        {
            // The library still holds this file; it is called what it was called
            // before the second quality arrived.
            await context.FiledVideos
                .Where(row => row.Directory == directory && row.FileName == fileName)
                .ExecuteUpdateAsync(
                    row => row.SetProperty(
                        filed => filed.FileName,
                        System.IO.Path.GetFileName(operation.From)),
                    whateverHappens);
        }
        else
        {
            // And here it does not hold it at all. The row goes rather than
            // being kept as history — that is what the entry below is for.
            await context.FiledVideos
                .Where(row => row.Directory == directory && row.FileName == fileName)
                .ExecuteDeleteAsync(whateverHappens);
        }

        await context.Operations
            .Where(entry => entry.Id == operation.Id)
            .ExecuteUpdateAsync(
                entry => entry
                    .SetProperty(row => row.UndoneAt, time.GetUtcNow())
                    .SetProperty(row => row.UndoneByRunId, undoRunId),
                whateverHappens);
    }

    /// <summary>
    /// The sidecar, the image and the scene directory, once the video they
    /// belong to has left.
    /// </summary>
    /// <returns>
    /// What is still there and why, or <c>null</c> when there is nothing to say.
    /// </returns>
    /// <remarks>
    /// Every condition here is a way of not deleting somebody else's work, and
    /// every one of them is checked now rather than taken from the log: the
    /// sidecar has to still carry the marker ADR 0024 puts in it, the image has
    /// to still be the bytes this run wrote, and the directory has to be one this
    /// operation made and have nothing left in it.
    /// </remarks>
    private async Task<string?> TakeAwayAsync(LoggedOperation operation)
    {
        if (operation.Kind is OperationKind.Relabelled)
        {
            return null;
        }

        var directory = operation.Directory;

        // Another quality of the same scene, or another scene the layout put in
        // the same directory. Either way what is next to it describes something
        // that is still there.
        if (await context.FiledVideos.AnyAsync(row => row.Directory == directory, CancellationToken.None))
        {
            return null;
        }

        var left = new List<string>();

        if (operation.Sidecar is { } sidecar && sidecars.Remove(sidecar.Path) is { } keptSidecar)
        {
            left.Add(keptSidecar);
        }

        if (operation.Artwork is { } image
            && artwork.Remove(image.Path, image.Bytes, image.Fingerprint) is { } keptImage)
        {
            left.Add(keptImage);
        }

        if (operation.CreatedDirectory && RemoveDirectory(directory) is { } keptDirectory)
        {
            left.Add(keptDirectory);
        }

        return left.Count == 0 ? null : string.Join(" ", left);
    }

    /// <summary>
    /// The scene directory, if this operation made it and nothing is left in it.
    /// </summary>
    /// <remarks>
    /// The site directory above it is left alone. An empty one is not in
    /// anybody's way — <c>SceneDirectories</c> only ever asks about the scene
    /// directory itself — and removing directories the tool did not make is the
    /// habit this rule exists to avoid.
    /// </remarks>
    private string? RemoveDirectory(string directory)
    {
        try
        {
            if (!System.IO.Directory.Exists(directory))
            {
                return null;
            }

            if (System.IO.Directory.EnumerateFileSystemEntries(directory).Any())
            {
                return $"'{directory}' still has something in it, so it was left where it is.";
            }

            System.IO.Directory.Delete(directory);

            logger.LogInformation("Removed {Path}, which this tool made.", directory);

            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Could not remove the scene directory {Path}.", directory);

            return $"'{directory}' could not be removed: {exception.Message}";
        }
    }

    /// <summary>
    /// The entries to work through, newest first — ADR 0029's reverse order,
    /// which is what puts a second quality back before the file it relabelled is
    /// renamed back.
    /// </summary>
    private async Task<Entries> EntriesAsync(
        int? runId,
        int? operationId,
        CancellationToken cancellationToken)
    {
        if (runId is null && operationId is null)
        {
            // Nothing was named. It cannot happen through the endpoints, and an
            // undo that quietly worked on everything if it did is not a mistake
            // worth leaving available.
            return new Entries([], "Nothing was named to put back.");
        }

        var rows = await Rows(runId, operationId)
            .AsNoTracking()
            .OrderByDescending(entry => entry.Id)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return new Entries(
                [],
                runId is not null
                    ? "That run is not in the log any more. The log keeps the most recent runs and "
                        + "drops the oldest whole, so there is nothing here to put back."
                    : "That operation is not in the log any more.");
        }

        return new Entries([.. rows.Select(Read)]);
    }

    private IQueryable<OperationEntry> Rows(int? runId, int? operationId) => operationId is { } one
        ? context.Operations.Where(entry => entry.Id == one)
        : context.Operations.Where(entry => entry.RunId == runId);

    /// <summary>The row as the planner reads it.</summary>
    internal static LoggedOperation Read(OperationEntry entry) => new(
        entry.Id,
        entry.RunId,
        entry.Kind,
        entry.VideoId is { } videoId && entry.SceneSite is { } site && entry.SceneTitle is { } title
            ? new Scene(videoId, site, title, entry.SceneReleaseDate)
            : null,
        entry.FromPath,
        entry.ToPath,
        entry.QualityLabel,
        entry.Movement,
        entry.SizeBytes,
        entry.OsHash,
        entry.CreatedDirectory,
        entry.SidecarPath is { } sidecar ? new WrittenSidecar(sidecar) : null,
        entry is { ArtworkPath: { } image, ArtworkBytes: { } bytes, ArtworkFingerprint: { } fingerprint }
            ? new WrittenArtwork(image, bytes, fingerprint)
            : null,
        new OperationReason(entry.DecidedBy, entry.Confidence, entry.MatchedBy, entry.DecidedAt),
        entry.At,
        entry.UndoneAt);

    private static bool Occupied(string path) => File.Exists(path) || System.IO.Directory.Exists(path);

    private static UndoResult NotReached(LoggedOperation operation) =>
        new(
            UndoResultState.Stopped,
            new UndoPlan(
                UndoOutcome.Refused,
                operation,
                "The tool was asked to stop before this one was reached. Nothing happened to it."),
            "Not reached before the tool stopped.");

    /// <summary>
    /// What the undo run's own row says it did. The same sentence the screen
    /// shows, from the same code, for the reason a filing run's is.
    /// </summary>
    private static string Account(IReadOnlyList<UndoResult> results) =>
        UndoRun.Never.Undone(DateTimeOffset.UnixEpoch, results).WhatItDid!;

    /// <param name="Problem">Why there is nothing to work through, when there is not.</param>
    private sealed record Entries(IReadOnlyList<LoggedOperation> Operations, string? Problem = null);
}

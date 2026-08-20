using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Ordeno.Core.History;
using Prdb.Ordeno.Core.Library;
using Prdb.Ordeno.Infrastructure.Persistence;

namespace Prdb.Ordeno.Infrastructure.History;

/// <summary>
/// What the tool did to somebody's files, written down as it does it —
/// ADR 0028.
/// </summary>
/// <remarks>
/// <para>
/// The entries are added to the context and not saved here, on purpose:
/// <c>FilingService</c> saves them in the same call as the row that says the
/// library holds the file. A file the tool moved and did not log is the one file
/// with no way back, and it is created by exactly the interruption undo exists
/// for.
/// </para>
/// <para>
/// Nothing in here takes the run's cancellation token, for the same reason. By
/// the time any of it is called the file has already moved.
/// </para>
/// </remarks>
public sealed class OperationLog(
    OrdenoDbContext context,
    TimeProvider time,
    ILogger<OperationLog> logger)
{
    /// <summary>Opens a run, and answers with the id its entries hang off.</summary>
    /// <param name="askedBy">
    /// Whether somebody asked for this run or the timer did — ADR 0031. It
    /// decides what an empty run leaves behind, in <see cref="FinishAsync"/>.
    /// </param>
    public async Task<int> StartAsync(RunKind kind, AskedBy askedBy = AskedBy.Person)
    {
        var run = new OperationRun { Kind = kind, AskedBy = askedBy, StartedAt = time.GetUtcNow() };

        context.OperationRuns.Add(run);
        await context.SaveChangesAsync(CancellationToken.None);
        context.ChangeTracker.Clear();

        return run.Id;
    }

    /// <summary>
    /// A video that has just moved into the library. Added to the context rather
    /// than saved, so that it lands in the same statement as the
    /// <see cref="FiledVideo"/> row.
    /// </summary>
    /// <param name="reason">Why the tool believed this was the right place for it.</param>
    /// <param name="sizeBytes">What the scan measured before the move.</param>
    /// <param name="osHash">The exact hash it read, where there was one.</param>
    /// <param name="createdDirectory">Whether the move made the scene directory.</param>
    public OperationEntry Filed(
        int runId,
        FilingPlan plan,
        OperationReason reason,
        long? sizeBytes,
        string? osHash,
        bool createdDirectory,
        WrittenSidecar? sidecar = null,
        WrittenArtwork? artwork = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(reason);

        var entry = new OperationEntry
        {
            RunId = runId,
            Kind = OperationKind.Filed,
            VideoId = plan.Scene?.VideoId,
            SceneTitle = plan.Scene?.Title,
            SceneSite = plan.Scene?.Site,
            SceneReleaseDate = plan.Scene?.ReleaseDate,
            FromPath = plan.SourcePath,
            ToPath = plan.TargetPath!,
            QualityLabel = plan.QualityLabel,
            Movement = plan.Movement,
            SizeBytes = sizeBytes,
            OsHash = osHash,
            CreatedDirectory = createdDirectory,
            SidecarPath = sidecar?.Path,
            ArtworkPath = artwork?.Path,
            ArtworkBytes = artwork?.Bytes,
            ArtworkFingerprint = artwork?.Fingerprint,
            DecidedBy = reason.DecidedBy,
            Confidence = reason.Confidence,
            MatchedBy = reason.MatchedBy,
            DecidedAt = reason.DecidedAt,
            At = time.GetUtcNow(),
        };

        context.Operations.Add(entry);

        return entry;
    }

    /// <summary>
    /// What went in next to the video, added to the entry once it is there.
    /// </summary>
    /// <remarks>
    /// A second write rather than part of the first, because the sidecar and the
    /// image are written after the move and the entry is written with the move
    /// (ADR 0024's order, and ADR 0027's). A container killed in between leaves
    /// an entry that does not mention a file the run wrote, and an undo that
    /// leaves that file where it is — the safe half of the two, and the reason
    /// this is not the other way round.
    /// </remarks>
    public async Task WroteAsync(int operationId, WrittenSidecar? sidecar, WrittenArtwork? artwork)
    {
        if (sidecar is null && artwork is null)
        {
            return;
        }

        await context.Operations
            .Where(operation => operation.Id == operationId)
            .ExecuteUpdateAsync(
                operation => operation
                    .SetProperty(row => row.SidecarPath, sidecar == null ? null : sidecar.Path)
                    .SetProperty(row => row.ArtworkPath, artwork == null ? null : artwork.Path)
                    .SetProperty(row => row.ArtworkBytes, artwork == null ? null : artwork.Bytes)
                    .SetProperty(
                        row => row.ArtworkFingerprint,
                        artwork == null ? null : artwork.Fingerprint),
                CancellationToken.None);
    }

    /// <summary>
    /// A file the library already held, renamed to carry its quality (ADR 0020).
    /// Its own entry, because an undo that returns the newcomer and leaves this
    /// rename in place is half an undo.
    /// </summary>
    public void Relabelled(int runId, FilingRelabel relabel, Scene? scene, OperationReason reason)
    {
        ArgumentNullException.ThrowIfNull(relabel);
        ArgumentNullException.ThrowIfNull(reason);

        context.Operations.Add(new OperationEntry
        {
            RunId = runId,
            Kind = OperationKind.Relabelled,
            VideoId = scene?.VideoId,
            SceneTitle = scene?.Title,
            SceneSite = scene?.Site,
            SceneReleaseDate = scene?.ReleaseDate,
            FromPath = relabel.From,
            ToPath = relabel.To,
            // The rename happens inside one directory, so it is the movement
            // that cannot half-happen — and it is read back before the file that
            // caused it, which is the whole point of recording it separately.
            Movement = Core.Configuration.FileMovement.Rename,
            CreatedDirectory = false,
            DecidedBy = reason.DecidedBy,
            Confidence = reason.Confidence,
            MatchedBy = reason.MatchedBy,
            DecidedAt = reason.DecidedAt,
            At = time.GetUtcNow(),
        });
    }

    /// <summary>
    /// Closes a run with what it did, and trims the log behind it.
    /// </summary>
    /// <param name="account">
    /// The one line the screen showed while it was happening. Stored rather than
    /// recomputed: it counts files nothing happened to, which leave no entry.
    /// </param>
    /// <param name="changedFiles">
    /// Whether this run changed a file, for a run whose changes are not entries.
    /// A refresh has none by decision (ADR 0033) and still keeps its row when it
    /// rewrote something, so it answers this rather than being counted. Left
    /// <c>null</c>, the entries are the answer, which is what filing and undo
    /// want.
    /// </param>
    /// <remarks>
    /// A run nobody asked for that changed nothing is removed instead of closed —
    /// ADR 0031, amending ADR 0028. That ADR keeps the row for an empty run
    /// because it answers the first question of somebody who was asleep, and
    /// that is owed to whoever asked; nobody asked for a tick of the clock, and
    /// a row every quarter of an hour would push three years of nights out of a
    /// thousand-run log inside a fortnight. A nightly refresh that found nothing
    /// to correct is the same row for the same reason.
    /// </remarks>
    public async Task FinishAsync(
        int runId,
        string? account,
        string? problem = null,
        bool? changedFiles = null)
    {
        if (await LeavesNothingAsync(runId, changedFiles))
        {
            await context.OperationRuns
                .Where(run => run.Id == runId)
                .ExecuteDeleteAsync(CancellationToken.None);

            return;
        }

        await context.OperationRuns
            .Where(run => run.Id == runId)
            .ExecuteUpdateAsync(
                run => run
                    .SetProperty(row => row.FinishedAt, time.GetUtcNow())
                    .SetProperty(row => row.Account, account)
                    .SetProperty(row => row.Problem, problem),
                CancellationToken.None);

        await TrimAsync();
    }

    /// <summary>
    /// Whether this run is one the log has nothing to say about: nobody asked for
    /// it, and it changed no filesystem.
    /// </summary>
    /// <remarks>
    /// Asked at the end rather than assumed at the start, because the run row has
    /// to exist while the run is happening — the entries hang off it, and a
    /// container killed mid-run leaves the honest record of a run that did not
    /// finish.
    /// </remarks>
    private async Task<bool> LeavesNothingAsync(int runId, bool? changedFiles) =>
        // A run that says it changed something keeps its row whether or not the
        // change was an entry. Everything else is the question this always
        // asked: nobody asked for it, and nothing it did is in the log.
        changedFiles is not true
        && await context.OperationRuns
            .AsNoTracking()
            .AnyAsync(run => run.Id == runId
                && run.AskedBy == AskedBy.Timer
                && !context.Operations.Any(operation => operation.RunId == run.Id));

    /// <summary>
    /// Drops the oldest runs, whole, until the log is inside both of
    /// <see cref="HistoryLimits"/> — ADR 0028.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Never inside a run. A run in the log can be undone as a run, or it is not
    /// in the log at all; half a batch is the one state worth ruling out,
    /// because it is the state that looks complete and is not.
    /// </para>
    /// <para>
    /// Called where a run is closed, so an installation that files nothing never
    /// runs it, and a run that files thousands runs it once.
    /// </para>
    /// </remarks>
    public async Task TrimAsync()
    {
        var runs = await context.OperationRuns
            .AsNoTracking()
            .OrderBy(run => run.Id)
            .Select(run => new
            {
                run.Id,
                Operations = context.Operations.Count(operation => operation.RunId == run.Id),
            })
            .ToListAsync(CancellationToken.None);

        var overRuns = runs.Count - HistoryLimits.Runs;
        var overOperations = runs.Sum(run => run.Operations) - HistoryLimits.Operations;

        if (overRuns <= 0 && overOperations <= 0)
        {
            return;
        }

        var dropping = new List<int>();

        foreach (var run in runs)
        {
            if (overRuns <= 0 && overOperations <= 0)
            {
                break;
            }

            dropping.Add(run.Id);
            overRuns--;
            overOperations -= run.Operations;
        }

        // The entries go with the run, in the schema. What an undo run reversed
        // keeps its stamp: the reference is set to null rather than cascading,
        // so trimming an undo cannot take the filing it undid with it.
        var removed = await context.OperationRuns
            .Where(run => dropping.Contains(run.Id))
            .ExecuteDeleteAsync(CancellationToken.None);

        logger.LogInformation(
            "Trimmed {Runs} runs off the operation log, which is now within {MaxRuns} runs and "
            + "{MaxOperations} operations.",
            removed,
            HistoryLimits.Runs,
            HistoryLimits.Operations);
    }
}

using Prdb.Ordeno.Core.Configuration;
using Prdb.Ordeno.Core.History;

namespace Prdb.Ordeno.Host.History;

/// <summary>
/// One change to a filesystem, as the screen shows it.
/// </summary>
/// <param name="Kind"><c>filed</c> or <c>relabelled</c>.</param>
/// <param name="What">What happened, in one line.</param>
/// <param name="Why">
/// Why the tool believed it was right — the rung, the grade, or that a person
/// decided. It is the half of the log a bug report quotes.
/// </param>
/// <param name="Movement"><c>rename</c>, <c>copyThenDelete</c> or <c>unknown</c>.</param>
/// <param name="Sidecar">
/// The <c>movie.nfo</c> this operation wrote, or <c>null</c> where it wrote none.
/// It is on the row because it is what an undo would take away with the video.
/// </param>
/// <param name="Artwork">The <c>fanart.jpg</c> it downloaded, under the same rule.</param>
/// <param name="UndoneAt">When it was put back, or <c>null</c> while it stands.</param>
public sealed record LoggedOperationState(
    int Id,
    string Kind,
    string? Scene,
    string Name,
    string From,
    string To,
    string? Quality,
    string Movement,
    string What,
    string Why,
    string? Sidecar,
    string? Artwork,
    DateTimeOffset At,
    DateTimeOffset? UndoneAt);

/// <summary>
/// One run, and the entries it wrote.
/// </summary>
/// <param name="Kind"><c>filing</c> or <c>undo</c>.</param>
/// <param name="AskedByTimer">
/// Whether nobody asked for this run — the tool filed on its own (ADR 0031).
/// Never true of an undo: there is no timer behind the way back.
/// </param>
/// <param name="Account">What it did, in the one line the screen showed at the time.</param>
/// <param name="Operations">How many entries it wrote; <paramref name="Entries"/> may be fewer.</param>
/// <param name="CanBeUndone">
/// Whether there is anything left in it to put back. An undo run is never one of
/// these: the way back out of an undo is filing again.
/// </param>
public sealed record LoggedRunState(
    int Id,
    string Kind,
    bool AskedByTimer,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string Account,
    string? Problem,
    int Operations,
    int Undone,
    bool CanBeUndone,
    IReadOnlyList<LoggedOperationState> Entries);

/// <summary>
/// One page of the log, newest first — ADR 0028.
/// </summary>
/// <param name="EntriesShown">
/// How many entries of one run are sent with it. A run whose
/// <c>operations</c> is larger than this has more than the screen is showing.
/// </param>
public sealed record HistoryState(
    IReadOnlyList<LoggedRunState> Runs,
    int Page,
    int Pages,
    int PageSize,
    int Total,
    int EntriesShown);

/// <summary>
/// What undoing one operation would do.
/// </summary>
/// <param name="Outcome"><c>returns</c> or <c>refused</c>.</param>
/// <param name="Refusal">
/// Which refusal it is — <c>alreadyUndone</c>, <c>missing</c>, <c>changed</c>,
/// <c>unreadable</c>, <c>renamedLater</c>, <c>noWayBack</c> or <c>occupied</c> —
/// and <c>null</c> when nothing is refused.
/// </param>
public sealed record UndoPlanState(
    int OperationId,
    string Name,
    string? Scene,
    string Outcome,
    string? Refusal,
    string Message);

/// <summary>
/// What became of one operation when the undo ran.
/// </summary>
/// <param name="State"><c>returned</c>, <c>refused</c>, <c>failed</c> or <c>stopped</c>.</param>
/// <param name="Leftovers">
/// What is still in the library that the tool would not remove — a sidecar
/// somebody else wrote, an image that is no longer the one it downloaded, a
/// directory with something else in it. <c>null</c> when there is nothing to say.
/// </param>
public sealed record UndoneFileState(
    int OperationId,
    string Name,
    string? Scene,
    string State,
    string? Message,
    string? Leftovers);

/// <summary>
/// What the way back is doing, and what came of the last time it was asked.
/// </summary>
/// <remarks>
/// Two lists rather than one, and both are kept, for the reason the filing screen
/// keeps two: somebody who has just put a batch back and wants to know what is
/// left needs the answer to both questions at once.
/// </remarks>
/// <param name="Undoing">
/// Whether what is under way is moving files or only working out what it would
/// move.
/// </param>
public sealed record UndoState(
    bool Running,
    bool Undoing,
    int? RunId,
    int? OperationId,
    DateTimeOffset? CheckedAt,
    DateTimeOffset? UndoneAt,
    string? Problem,
    IReadOnlyList<UndoPlanState> Plan,
    int PlanTotal,
    int WouldReturn,
    string? WhatItWouldDo,
    IReadOnlyList<UndoneFileState> Results,
    int ResultTotal,
    string? WhatItDid)
{
    /// <summary>
    /// How many rows of each list go to the browser, exactly as the filing screen
    /// caps its own: the sentences carry the scale, the lists carry the examples.
    /// </summary>
    public const int Limit = 200;

    /// <summary>
    /// What is going on, and — when a request has just been refused — why
    /// nothing started.
    /// </summary>
    /// <param name="problem">
    /// Said instead of what the last run reported. It is the answer to a button
    /// that did nothing, which is a state the screen would otherwise have to
    /// infer from the absence of a change.
    /// </param>
    public static UndoState Of(UndoRun run, string? problem = null)
    {
        ArgumentNullException.ThrowIfNull(run);

        return new UndoState(
            Running: run.Running,
            Undoing: run.Undoing,
            RunId: run.RunId,
            OperationId: run.OperationId,
            CheckedAt: run.CheckedAt,
            UndoneAt: run.UndoneAt,
            Problem: problem ?? run.Problem,
            Plan: [.. run.Plan.Take(Limit).Select(Planned)],
            PlanTotal: run.Plan.Count,
            WouldReturn: run.WouldReturn,
            WhatItWouldDo: run.WhatItWouldDo,
            Results: [.. run.Results.Take(Limit).Select(Undone)],
            ResultTotal: run.Results.Count,
            WhatItDid: run.WhatItDid);
    }

    private static UndoPlanState Planned(UndoPlan plan) => new(
        plan.Operation.Id,
        plan.Operation.Name,
        plan.Operation.Scene?.InWords,
        plan.Returns ? "returns" : "refused",
        Name(plan.Refusal),
        plan.Message);

    private static UndoneFileState Undone(UndoResult result) => new(
        result.Plan.Operation.Id,
        result.Plan.Operation.Name,
        result.Plan.Operation.Scene?.InWords,
        Name(result.State),
        result.Message,
        result.Leftovers);

    private static string? Name(UndoRefusal refusal) => refusal switch
    {
        UndoRefusal.AlreadyUndone => "alreadyUndone",
        UndoRefusal.Missing => "missing",
        UndoRefusal.Changed => "changed",
        UndoRefusal.Unreadable => "unreadable",
        UndoRefusal.RenamedLater => "renamedLater",
        UndoRefusal.NoWayBack => "noWayBack",
        UndoRefusal.Occupied => "occupied",
        _ => null,
    };

    private static string Name(UndoResultState state) => state switch
    {
        UndoResultState.Returned => "returned",
        UndoResultState.Refused => "refused",
        UndoResultState.Failed => "failed",
        _ => "stopped",
    };
}

/// <summary>
/// The log as the browser reads it. Every state crosses this boundary as a name
/// rather than as a number, the way the rest of the API does.
/// </summary>
internal static class HistoryStates
{
    public static HistoryState Of(OperationHistory history)
    {
        ArgumentNullException.ThrowIfNull(history);

        return new HistoryState(
            [.. history.Runs.Select(Run)],
            history.Page,
            history.Pages,
            HistoryLimits.PageSize,
            history.Total,
            HistoryLimits.EntriesShown);
    }

    private static LoggedRunState Run(LoggedRun run) => new(
        run.Id,
        run.Kind is RunKind.Undo ? "undo" : "filing",
        run.AskedBy is AskedBy.Timer,
        run.StartedAt,
        run.FinishedAt,
        run.InWords,
        run.Problem,
        run.Operations,
        run.Undone,
        run.CanBeUndone,
        [.. run.Entries.Select(Entry)]);

    private static LoggedOperationState Entry(LoggedOperation operation) => new(
        operation.Id,
        operation.Kind is OperationKind.Relabelled ? "relabelled" : "filed",
        operation.Scene?.InWords,
        operation.Name,
        operation.From,
        operation.To,
        operation.QualityLabel,
        Name(operation.Movement),
        operation.InWords,
        operation.Reason.InWords,
        operation.Sidecar?.Path,
        operation.Artwork?.Path,
        operation.At,
        operation.UndoneAt);

    private static string Name(FileMovement movement) => movement switch
    {
        FileMovement.Rename => "rename",
        FileMovement.CopyThenDelete => "copyThenDelete",
        _ => "unknown",
    };
}

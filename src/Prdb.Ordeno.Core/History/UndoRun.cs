using System.Globalization;

namespace Prdb.Ordeno.Core.History;

/// <summary>
/// What the way back is doing, and what came of the last time it was asked.
/// </summary>
/// <remarks>
/// <para>
/// The same shape as <c>FilingRun</c>, and for the same reasons: it is held in
/// memory and forgotten on a restart, because what survives a restart is in the
/// database and a container that has just come up has undone nothing; and what
/// was checked is kept next to what happened, because somebody who has just put
/// a run back and asks what is left wants both answers at once.
/// </para>
/// <para>
/// It carries what it is about — a whole run, or one operation of one — because
/// the screen has a button on every row and has to know which of them is the one
/// that is working.
/// </para>
/// </remarks>
/// <param name="Undoing">
/// Whether what is under way is moving files or only working out what it would
/// move. It is the difference between a screen that is thinking and one that is
/// touching somebody's library.
/// </param>
public sealed record UndoRun(
    bool Running,
    bool Undoing,
    int? RunId,
    int? OperationId,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? Problem,
    IReadOnlyList<UndoPlan> Plan,
    DateTimeOffset? CheckedAt,
    IReadOnlyList<UndoResult> Results,
    DateTimeOffset? UndoneAt)
{
    /// <summary>Nothing has been checked or put back since the tool started.</summary>
    public static readonly UndoRun Never = new(
        Running: false,
        Undoing: false,
        RunId: null,
        OperationId: null,
        null,
        null,
        null,
        [],
        null,
        [],
        null);

    public UndoRun Started(DateTimeOffset at, bool undoing, int? runId, int? operationId) =>
        this with
        {
            Running = true,
            Undoing = undoing,
            RunId = runId,
            OperationId = operationId,
            StartedAt = at,
            FinishedAt = null,
            Problem = null,
        };

    /// <summary>A check that finished. Nothing has been moved.</summary>
    public UndoRun Checked(DateTimeOffset at, IReadOnlyList<UndoPlan> plan, string? problem = null) =>
        this with
        {
            Running = false,
            Undoing = false,
            FinishedAt = at,
            Problem = problem,
            Plan = plan,
            CheckedAt = at,
        };

    /// <summary>
    /// An undo that finished. What was checked is replaced by what the run itself
    /// worked out, so the screen never shows a preview of something that has
    /// already happened.
    /// </summary>
    public UndoRun Undone(DateTimeOffset at, IReadOnlyList<UndoResult> results, string? problem = null) =>
        this with
        {
            Running = false,
            Undoing = false,
            FinishedAt = at,
            Problem = problem,
            Plan = [.. results.Where(result => !result.Returned).Select(result => result.Plan)],
            CheckedAt = at,
            Results = results,
            UndoneAt = at,
        };

    public int WouldReturn => Plan.Count(plan => plan.Returns);

    public int WasReturned => Results.Count(result => result.Returned);

    /// <summary>
    /// What the check says, in one line. It is the sentence somebody reads before
    /// pressing a button that moves their files back, so it counts what would
    /// happen rather than what was found.
    /// </summary>
    public string? WhatItWouldDo
    {
        get
        {
            if (CheckedAt is null)
            {
                return null;
            }

            if (Plan.Count == 0)
            {
                return "There is nothing here to put back.";
            }

            var going = Plan.Count(plan => plan.Returns);
            var refused = Plan.Count - going;

            var parts = new List<string>();

            if (going > 0)
            {
                parts.Add(going == 1
                    ? "1 file would go back to the directory it came from"
                    : $"{Number(going)} files would go back to the directories they came from");
            }

            if (refused > 0)
            {
                parts.Add(refused == 1
                    ? "1 cannot be put back and says why"
                    : $"{Number(refused)} cannot be put back and say why");
            }

            return Join(parts) + ". Nothing has been moved yet.";
        }
    }

    /// <summary>What the last undo did, in one line. <c>null</c> until one has happened.</summary>
    public string? WhatItDid
    {
        get
        {
            if (UndoneAt is null)
            {
                return null;
            }

            var returned = Results.Count(result => result.Returned);
            var refused = Results.Count(result => result.State is UndoResultState.Refused);
            var failed = Results.Count(result => result.State is UndoResultState.Failed);
            var stopped = Results.Count(result => result.State is UndoResultState.Stopped);
            var leftovers = Results.Count(result => result.Returned && result.Leftovers is not null);

            var parts = new List<string>
            {
                returned == 1 ? "1 file went back" : $"{Number(returned)} files went back",
            };

            if (leftovers > 0)
            {
                parts.Add(leftovers == 1
                    ? "1 left something behind in the library that the tool would not remove"
                    : $"{Number(leftovers)} left something behind in the library that the tool "
                        + "would not remove");
            }

            if (refused > 0)
            {
                parts.Add(refused == 1
                    ? "1 was left exactly as it was, and the row says why"
                    : $"{Number(refused)} were left exactly as they were, and the rows say why");
            }

            if (failed > 0)
            {
                parts.Add(failed == 1
                    ? "1 could not be moved back and is still in the library"
                    : $"{Number(failed)} could not be moved back and are still in the library");
            }

            if (stopped > 0)
            {
                parts.Add($"{Number(stopped)} were not reached before the tool was asked to stop");
            }

            return Join(parts) + ".";
        }
    }

    private static string Join(IReadOnlyList<string> parts) => parts.Count switch
    {
        0 => "Nothing would happen",
        1 => parts[0],
        _ => string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1],
    };

    private static string Number(int number) => number.ToString("N0", CultureInfo.InvariantCulture);
}

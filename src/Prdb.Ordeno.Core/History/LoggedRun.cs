using System.Globalization;

namespace Prdb.Ordeno.Core.History;

/// <summary>What a run was doing.</summary>
public enum RunKind
{
    /// <summary>Filing, asked for by somebody (ADR 0022).</summary>
    Filing,

    /// <summary>Putting a run, or one operation of one, back where it came from.</summary>
    Undo,
}

/// <summary>
/// One run of the tool over somebody's files, and what it did — ADR 0028.
/// </summary>
/// <remarks>
/// <para>
/// The run is the unit of the way back, which is why the entries hang off it: a
/// batch filed overnight is undone as a batch, and undoing two hundred files one
/// row at a time is not a way back.
/// </para>
/// <para>
/// A run that moved nothing still has a row. It costs one row and it answers the
/// question somebody who was asleep actually asks, which is not "what happened"
/// but "did anything happen".
/// </para>
/// </remarks>
/// <param name="Account">
/// What the run did, in the one line the filing screen already builds.
/// <c>null</c> while it is still going.
/// </param>
/// <param name="Problem">What stopped it, when something did.</param>
/// <param name="Entries">
/// The operations, newest first, up to <see cref="HistoryLimits.EntriesShown"/>
/// of them. <paramref name="Operations"/> is how many there are.
/// </param>
/// <param name="Undone">
/// How many of them have been put back. A run where that equals
/// <paramref name="Operations"/> is one there is no way back from, because there
/// is nothing left to take back.
/// </param>
public sealed record LoggedRun(
    int Id,
    RunKind Kind,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string? Account,
    string? Problem,
    int Operations,
    int Undone,
    IReadOnlyList<LoggedOperation> Entries)
{
    /// <summary>Whether anything in it can still be put back.</summary>
    public bool CanBeUndone => Kind is RunKind.Filing && Undone < Operations;

    /// <summary>
    /// What the run did, for somebody scanning the page. The stored account when
    /// there is one — it is what the screen said at the time — and a count when
    /// there is not, which is what a run interrupted by a container restart
    /// leaves behind.
    /// </summary>
    public string InWords => Account ?? (Operations switch
    {
        0 when FinishedAt is null =>
            "This run did not finish. Whatever it had done is below; anything it had not reached "
            + "was not touched.",
        0 => "Nothing was moved.",
        1 => "1 file was moved.",
        _ => $"{Operations.ToString("N0", CultureInfo.InvariantCulture)} files were moved.",
    });
}

/// <summary>
/// One page of the log, newest first.
/// </summary>
/// <remarks>
/// Paged rather than capped, like the review queue: this is a record somebody
/// scrolls back through until they find the night they are looking for, and a
/// list that silently stops is one they cannot reach the end of.
/// </remarks>
public sealed record OperationHistory(
    IReadOnlyList<LoggedRun> Runs,
    int Page,
    int Pages,
    int Total)
{
    public static readonly OperationHistory Nothing = new([], 1, 1, 0);
}

/// <summary>
/// What keeps the log small enough to live in the same SQLite file as everything
/// else after three years — ADR 0028.
/// </summary>
/// <remarks>
/// A count and not an age, and the ADR has the arithmetic: an age bounds nothing,
/// because the week somebody points the tool at a NAS full of two hundred
/// thousand files fits inside every age window there is — and that is precisely
/// the week an undo is most likely to be wanted.
/// </remarks>
public static class HistoryLimits
{
    /// <summary>
    /// How many operations are kept. A few hundred bytes each, almost all of it
    /// paths, so this is something under ten megabytes — a hundred
    /// two-hundred-file nights.
    /// </summary>
    public const int Operations = 20_000;

    /// <summary>
    /// How many runs are kept. The best part of three years of a nightly one,
    /// and what stops an installation that files nothing every night from
    /// keeping rows forever.
    /// </summary>
    public const int Runs = 1_000;

    /// <summary>How many runs one page of the screen holds.</summary>
    public const int PageSize = 20;

    /// <summary>
    /// How many of a run's entries are sent with it. A first pass over a library
    /// is thousands, and a screen full helps nobody — the account carries the
    /// scale and the rows carry the examples, exactly as the filing screen does.
    /// </summary>
    public const int EntriesShown = 200;
}

namespace Prdb.Ordeno.Core.History;

/// <summary>What an undo would do with one operation.</summary>
public enum UndoOutcome
{
    /// <summary>The file goes back where it came from.</summary>
    Returns,

    /// <summary>
    /// Nothing happens, and the reason is on the plan. ADR 0029: an operation
    /// whose reversal is not plainly safe does nothing at all — no partial
    /// attempt, no best effort.
    /// </summary>
    Refused,
}

/// <summary>
/// Why an operation cannot be put back. Named rather than merely described,
/// because each of these is a different thing to do about it and a test that
/// checks a sentence checks the wrong half.
/// </summary>
public enum UndoRefusal
{
    None,

    /// <summary>It has been put back already. The record says so.</summary>
    AlreadyUndone,

    /// <summary>Nothing is at the path the entry names.</summary>
    Missing,

    /// <summary>Something is, and it is not the file that was filed.</summary>
    Changed,

    /// <summary>It could not be looked at, so nothing is claimed either way.</summary>
    Unreadable,

    /// <summary>A later run renamed it. The way back out of that one comes first.</summary>
    RenamedLater,

    /// <summary>The directory it came from is not there.</summary>
    NoWayBack,

    /// <summary>Something is already where it would go back to.</summary>
    Occupied,
}

/// <summary>
/// What is at the path an operation filed to, as the filesystem answered a
/// moment ago.
/// </summary>
/// <remarks>
/// The undo's equivalent of the plan the filing screen shows, and the reason
/// this project still touches no I/O (ADR 0012): the questions are asked
/// outside, the answers are decided here, and the run asks them again as it
/// acts.
/// </remarks>
/// <param name="SizeBytes">How long the file at that path is, when there is one.</param>
/// <param name="OsHash">
/// Its exact hash, computed only where the entry has one to compare against —
/// there is no point reading 128 KiB to compare it with nothing.
/// </param>
/// <param name="SourceOccupied">Whether anything is at the path it would go back to.</param>
/// <param name="SourceDirectoryExists">
/// Whether the directory it came from is still there. A share that is not
/// mounted looks like an empty path, and putting two hundred files into what is
/// really a mountpoint is how a NAS fills its system disk.
/// </param>
/// <param name="RenamedBy">
/// What later run took that name, in words, when the log knows of one.
/// <c>null</c> otherwise.
/// </param>
public sealed record UndoObservation(
    FiledFileState State,
    long? SizeBytes = null,
    string? OsHash = null,
    bool SourceOccupied = false,
    bool SourceDirectoryExists = true,
    string? RenamedBy = null);

/// <summary>What is at the path an operation put a file at.</summary>
public enum FiledFileState
{
    Present,

    Missing,

    /// <summary>A permission, a share that went away. Not the same as missing.</summary>
    Unreadable,
}

/// <summary>
/// What undoing one operation would do, worked out without touching anything.
/// </summary>
/// <param name="Message">
/// What the row says: where the file would go back to, or why it cannot. Never
/// <c>null</c> — an undo that says nothing about a file it will not touch is the
/// hidden partial ADR 0029 forbids.
/// </param>
public sealed record UndoPlan(
    UndoOutcome Outcome,
    LoggedOperation Operation,
    string Message,
    UndoRefusal Refusal = UndoRefusal.None)
{
    public bool Returns => Outcome is UndoOutcome.Returns;
}

/// <summary>What an undo would do, and why it might not be able to do any of it.</summary>
/// <param name="Problem">
/// Something that stops the whole undo rather than one file — the run is not in
/// the log any more, the library cannot be looked at. <c>null</c> when the plans
/// below are the answer.
/// </param>
public sealed record UndoPreview(IReadOnlyList<UndoPlan> Plans, string? Problem = null)
{
    public static readonly UndoPreview Nothing = new([]);
}

/// <summary>How one plan turned out once it was carried out.</summary>
public enum UndoResultState
{
    /// <summary>The file is back where it came from.</summary>
    Returned,

    /// <summary>Nothing was touched, as the plan said. Not a failure.</summary>
    Refused,

    /// <summary>Something went wrong while moving it back. The file is still filed.</summary>
    Failed,

    /// <summary>The container was asked to stop before this one was reached.</summary>
    Stopped,
}

/// <param name="Message">What to tell the user, whether it worked or not.</param>
/// <param name="Leftovers">
/// What could not be taken away with the file — a sidecar, an image, the scene
/// directory. Named rather than swallowed: the video is back either way, and
/// somebody who wants the directory gone needs to know what is still in it.
/// </param>
public sealed record UndoResult(
    UndoResultState State,
    UndoPlan Plan,
    string? Message = null,
    string? Leftovers = null)
{
    public bool Returned => State is UndoResultState.Returned;
}

/// <param name="Problem">What stopped the whole undo, when something did.</param>
public sealed record UndoReport(IReadOnlyList<UndoResult> Results, string? Problem = null)
{
    public static readonly UndoReport Nothing = new([]);
}

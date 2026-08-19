namespace Prdb.Ordeno.Core.History;

/// <summary>
/// Works out whether one operation can be put back, and writes nothing at all.
/// </summary>
/// <remarks>
/// <para>
/// The undo's half of ADR 0022's shape: what the screen shows and what the run
/// performs are one computation, made twice — once to be read, once at the
/// moment of acting. A file can be renamed, replaced or removed in the seconds
/// between the two, and undoing on the strength of a check that has gone stale
/// is what the preview exists to prevent.
/// </para>
/// <para>
/// Every branch here refuses. ADR 0029 has no best effort in it: an operation
/// whose reversal is not plainly safe is left alone with a sentence, because the
/// alternative is a tool that guesses about a file somebody cannot get back.
/// </para>
/// </remarks>
public static class UndoPlanner
{
    public static UndoPlan Plan(LoggedOperation operation, UndoObservation observed)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(observed);

        if (operation.Undone)
        {
            return Refuse(
                operation,
                UndoRefusal.AlreadyUndone,
                $"'{operation.Name}' has already been put back. Nothing was touched.");
        }

        // Before anything about the file itself: the log knows this name was
        // taken by a later run, so whatever is at it now belongs to that run. The
        // way back is chronological in reverse, at this scale as well as within
        // a run.
        if (observed.RenamedBy is { } later)
        {
            return Refuse(
                operation,
                UndoRefusal.RenamedLater,
                $"'{operation.Name}' was renamed afterwards by {later}. Undo that run first, and "
                + "this one can go back.");
        }

        switch (observed.State)
        {
            case FiledFileState.Missing:
                return Refuse(
                    operation,
                    UndoRefusal.Missing,
                    $"There is nothing at '{operation.To}' any more. Something else has happened to "
                    + "that file since, and the tool does not go looking for it.");

            case FiledFileState.Unreadable:
                return Refuse(
                    operation,
                    UndoRefusal.Unreadable,
                    $"'{operation.To}' could not be looked at, so nothing was moved. A share that "
                    + "is not mounted reads exactly like this.");
        }

        if (Changed(operation, observed) is { } changed)
        {
            return Refuse(operation, changed.Refusal, changed.Message);
        }

        if (!observed.SourceDirectoryExists)
        {
            return Refuse(
                operation,
                UndoRefusal.NoWayBack,
                $"The directory this came from is not there: '{System.IO.Path.GetDirectoryName(operation.From)}'. "
                + "Nothing was moved — putting files into a directory that is really an unmounted "
                + "share is how a disk fills up with copies nobody can see.");
        }

        if (observed.SourceOccupied)
        {
            return Refuse(
                operation,
                UndoRefusal.Occupied,
                $"Something is already at '{operation.From}'. Nothing was moved: a reversal that "
                + "overwrites is not a reversal.");
        }

        return new UndoPlan(UndoOutcome.Returns, operation, Returns(operation));
    }

    /// <summary>
    /// Whether what is at the path is the file that was filed. Size first,
    /// because it is free and it is how a file that was replaced usually differs;
    /// then the exact hash, where the entry has one to compare with.
    /// </summary>
    private static (UndoRefusal Refusal, string Message)? Changed(
        LoggedOperation operation,
        UndoObservation observed)
    {
        if (operation.SizeBytes is { } size && observed.SizeBytes != size)
        {
            return (UndoRefusal.Changed,
                $"'{operation.Name}' is not the file that was filed — it is a different length now. "
                + "Whatever it is, it is somebody's work and it was left exactly where it is.");
        }

        if (operation.OsHash is not { } hash)
        {
            return null;
        }

        if (observed.OsHash is null)
        {
            return (UndoRefusal.Unreadable,
                $"'{operation.Name}' could not be read to check that it is still the file that was "
                + "filed, so nothing was moved.");
        }

        return string.Equals(observed.OsHash, hash, StringComparison.OrdinalIgnoreCase)
            ? null
            : (UndoRefusal.Changed,
                $"'{operation.Name}' is the right length and not the right file. It has been "
                + "replaced since it was filed, and it was left exactly where it is.");
    }

    /// <summary>
    /// What would happen, in the words the screen shows before somebody presses
    /// the button — and the same words the result repeats afterwards.
    /// </summary>
    private static string Returns(LoggedOperation operation)
    {
        if (operation.Kind is OperationKind.Relabelled)
        {
            return $"'{operation.Name}' is renamed back to '{operation.PreviousName}'.";
        }

        var goes = operation.Movement is Configuration.FileMovement.CopyThenDelete
            ? $"'{operation.Name}' is copied back to '{operation.From}', checked, and removed from "
                + "the library"
            : $"'{operation.Name}' goes back to '{operation.From}'";

        var with = (operation.Sidecar, operation.Artwork) switch
        {
            (not null, not null) =>
                $". The '{System.IO.Path.GetFileName(operation.Sidecar.Path)}' and "
                + $"'{System.IO.Path.GetFileName(operation.Artwork.Path)}' this run wrote go with "
                + "it, if they are still the files it wrote",
            (not null, null) =>
                $". The '{System.IO.Path.GetFileName(operation.Sidecar.Path)}' this run wrote goes "
                + "with it, if it is still the file it wrote",
            (null, not null) =>
                $". The '{System.IO.Path.GetFileName(operation.Artwork.Path)}' this run downloaded "
                + "goes with it, if it is still the file it downloaded",
            _ => string.Empty,
        };

        var directory = operation.CreatedDirectory
            ? ", and the scene directory is removed if nothing else is left in it"
            : string.Empty;

        return goes + with + directory + ".";
    }

    private static UndoPlan Refuse(LoggedOperation operation, UndoRefusal refusal, string message) =>
        new(UndoOutcome.Refused, operation, message, refusal);
}

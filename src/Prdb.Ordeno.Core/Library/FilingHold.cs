using System.Globalization;

namespace Prdb.Ordeno.Core.Library;

/// <summary>
/// What an undo left behind about a file it put back — ADR 0030.
/// </summary>
/// <remarks>
/// <para>
/// A hold is not a decision about the video and not a dismissal (ADR 0023). It
/// says what happened to one file at one path: it was filed, somebody took it
/// back, and until they say otherwise no run files it again — the timer's
/// (ADR 0031) as much as the one behind the button, because the plan must not
/// depend on who is reading it.
/// </para>
/// <para>
/// It carries where the file had been filed rather than only when. "You put this
/// back" is the answer to why nothing is happening; what it had been filed as is
/// the answer to whether releasing it would be a good idea.
/// </para>
/// </remarks>
/// <param name="HeldAt">When the undo put the file back.</param>
/// <param name="FiledAt">When the run that filed it did so.</param>
/// <param name="FiledTo">The path in the library the file went to and came back from.</param>
public sealed record FilingHold(DateTimeOffset HeldAt, DateTimeOffset FiledAt, string FiledTo)
{
    /// <summary>
    /// What the row says, which is the whole of what the user has to know: that
    /// this is the tool remembering rather than the tool failing, and what it is
    /// remembering.
    /// </summary>
    public string InWords =>
        $"You put this file back on {Moment(HeldAt)}, after it was filed as "
        + $"'{System.IO.Path.GetFileName(FiledTo)}' on {Moment(FiledAt)}. It stays where it is "
        + "until you release it — nothing files it again on its own.";

    private static string Moment(DateTimeOffset at) =>
        at.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC";
}

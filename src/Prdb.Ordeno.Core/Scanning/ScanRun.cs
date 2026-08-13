namespace Prdb.Ordeno.Core.Scanning;

/// <summary>
/// How often the tool looks, when nobody asks it to.
/// </summary>
/// <remarks>
/// Scanning is periodic rather than event-driven on purpose — ADR 0016.
/// Both numbers are constants rather than settings: a setting is worth adding
/// once somebody has a reason to change it, and until then it is one more thing
/// in the UI to be wrong about.
/// </remarks>
public static class ScanSchedule
{
    /// <summary>
    /// Long enough that a NAS spinning its disks up for it is a non-event, short
    /// enough that a download finished at ten past is filed before the quarter
    /// hour. A file still being written costs another interval, which is the
    /// trade this direction is deliberately on the safe side of.
    /// </summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The wait before the first scan after a start. A container that has just
    /// come up is usually a container whose volumes have just been mounted, and
    /// a scan racing that would report a perfectly good share as unreachable.
    /// </summary>
    public static readonly TimeSpan FirstScanDelay = TimeSpan.FromSeconds(20);
}

/// <summary>
/// The last time the tool went looking, or the one it is doing now. It is kept
/// in memory and forgotten on a restart, which is the truth: a container that
/// has just started has not scanned anything yet, whatever its database
/// remembers from before.
/// </summary>
/// <param name="Problem">
/// Why the last scan did not finish, in words for the user, or <c>null</c> if it
/// did.
/// </param>
public sealed record ScanRun(
    bool Running,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? Problem)
{
    /// <summary>Nothing has been scanned since the tool started.</summary>
    public static readonly ScanRun Never = new(Running: false, null, null, null);

    public ScanRun Started(DateTimeOffset at) => new(Running: true, at, null, null);

    public ScanRun Finished(DateTimeOffset at, string? problem = null) =>
        new(Running: false, StartedAt, at, problem);
}

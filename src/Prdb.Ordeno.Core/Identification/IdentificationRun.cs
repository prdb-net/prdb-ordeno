namespace Prdb.Ordeno.Core.Identification;

/// <summary>
/// When the tool asks prdb, when nobody asks it to.
/// </summary>
public static class IdentificationSchedule
{
    /// <summary>
    /// The most files one request may carry — the endpoint's own limit. It is
    /// what makes a first pass over a library a handful of requests rather than
    /// one per file, so it is not a number to lower for tidiness.
    /// </summary>
    public const int MaxBatch = 200;

    /// <summary>
    /// How much of the hourly quota a run leaves behind before stopping. prdb
    /// reports what is left on every metered answer, so the pacing costs
    /// nothing; the reserve is there because identification is not the only
    /// thing that will ever want a request, and a tool that spends the last one
    /// on itself is a tool that answers "rate limited" to the person in front of
    /// it.
    /// </summary>
    public const int QuotaReserve = 5;

    /// <summary>
    /// How often a run happens. The same interval as scanning, because a scan is
    /// what turns a settling file into one worth asking about — a run finds
    /// nothing to do until one has happened, and does nothing when it finds
    /// nothing.
    /// </summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Deliberately longer than the scan's first delay: on a fresh start the
    /// first scan is what produces the work, and asking prdb before it has run
    /// is a request that answers nothing.
    /// </summary>
    public static readonly TimeSpan FirstRunDelay = TimeSpan.FromMinutes(1);
}

/// <summary>
/// What one run did, as the thing that ran it reports back.
/// </summary>
/// <param name="Problem">
/// Why it stopped early, in words for the user. A run that stopped left every
/// file exactly as it found it — there is no half-identified state to undo.
/// </param>
public sealed record IdentificationOutcome(
    int Asked,
    string? Problem = null,
    DateTimeOffset? NotBefore = null)
{
    public static readonly IdentificationOutcome Nothing = new(0);
}

/// <summary>
/// The last time the tool asked prdb, or the one it is in the middle of. Held in
/// memory and forgotten on a restart, like the scan's: what survives a restart is
/// in the database, and a container that has just come up has asked nothing.
/// </summary>
/// <param name="Asked">How many files the last run sent.</param>
/// <param name="Problem">
/// What stopped the last run, in words for the user, or <c>null</c> if nothing
/// did. A run that stopped left every file exactly as it was.
/// </param>
/// <param name="NotBefore">
/// When the next run may go ahead. Set when prdb asked to be left alone; until
/// then a tick does nothing rather than spending a request on being refused
/// again.
/// </param>
public sealed record IdentificationRun(
    bool Running,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    int Asked,
    string? Problem,
    DateTimeOffset? NotBefore)
{
    public static readonly IdentificationRun Never =
        new(Running: false, null, null, 0, null, null);

    public IdentificationRun Started(DateTimeOffset at) =>
        new(Running: true, at, null, 0, null, NotBefore);

    public IdentificationRun Finished(
        DateTimeOffset at,
        int asked,
        string? problem = null,
        DateTimeOffset? notBefore = null) =>
        new(Running: false, StartedAt, at, asked, problem, notBefore);

    public bool MayRunAt(DateTimeOffset now) => NotBefore is null || NotBefore <= now;
}

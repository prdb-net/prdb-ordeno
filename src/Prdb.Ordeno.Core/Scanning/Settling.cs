namespace Prdb.Ordeno.Core.Scanning;

/// <summary>
/// When a file has stopped being written to, as far as the tool is willing to
/// claim. VISION.md would rather wait another cycle than act on a growing file,
/// so this is deliberately the pessimistic answer.
/// </summary>
/// <remarks>
/// The judgement is made by comparing two observations rather than by comparing
/// a timestamp on the file with the clock in the container. That distinction
/// matters more than it looks: the media sits on an SMB or NFS share whose clock
/// belongs to the NAS, and a share running a few minutes ahead would make every
/// file look as though it had just been written — permanently, on some setups.
/// Two observations of our own are immune to that, because both come from the
/// same clock.
/// </remarks>
public static class Settling
{
    /// <summary>
    /// How long a file has to have looked the same before it counts as
    /// finished. It is short because the scan interval already provides most of
    /// the waiting: the practical delay is one scan, not one minute.
    /// </summary>
    public static readonly TimeSpan QuietPeriod = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The moment a file must have last changed before, to have settled by
    /// <paramref name="now"/>. A cutoff rather than a predicate, so that the
    /// same rule can be asked of a single file here and of a whole table in one
    /// query.
    /// </summary>
    public static DateTimeOffset SettledIfUnchangedSince(DateTimeOffset now) => now - QuietPeriod;

    /// <summary>
    /// Whether one file has settled. <paramref name="unchangedSince"/> is when
    /// the tool first saw the size and modification time the file has now.
    /// </summary>
    /// <remarks>
    /// A file of zero bytes never settles. A download client that pre-creates
    /// the final name leaves one behind for as long as it takes to write the
    /// first block, and an empty file is not a video by any reading.
    /// </remarks>
    public static bool HasSettled(long sizeBytes, DateTimeOffset unchangedSince, DateTimeOffset now) =>
        sizeBytes > 0 && unchangedSince <= SettledIfUnchangedSince(now);
}

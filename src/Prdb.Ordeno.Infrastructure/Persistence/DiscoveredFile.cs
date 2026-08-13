namespace Prdb.Ordeno.Infrastructure.Persistence;

/// <summary>
/// One video the tool has found in a source directory. The row is the tool's
/// memory of an observation, not a claim about the file: it says what was there
/// and when, and nothing at all about what the video is.
/// </summary>
/// <remarks>
/// Rows come and go with the files. One that disappears from disk between two
/// scans leaves the table, because a row pointing at nothing would only be a
/// question for whoever finds it later.
/// </remarks>
public sealed class DiscoveredFile
{
    public int Id { get; set; }

    /// <summary>
    /// The directory it was found under. Removing that directory from the
    /// configuration takes its files with it — the schema cascades, so this
    /// holds even for a delete that never loads the rows.
    /// </summary>
    public int SourceDirectoryId { get; set; }

    /// <summary>The full path inside the container, which is the only name that identifies it.</summary>
    public required string Path { get; set; }

    public long SizeBytes { get; set; }

    /// <summary>
    /// The modification time the filesystem reports. It is compared with the
    /// previous observation and never with the container's own clock: on an SMB
    /// or NFS share this timestamp comes from the NAS, and the two clocks do not
    /// have to agree — see <c>Settling</c>.
    /// </summary>
    public DateTimeOffset LastWriteAt { get; set; }

    public DateTimeOffset FirstSeenAt { get; set; }

    /// <summary>The scan that last saw it. A row older than the current scan is a file that is gone.</summary>
    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>
    /// When the tool first saw the size and modification time this row now
    /// carries. This is what "has it stopped growing" is measured from, so a
    /// file that changes goes back to the beginning of its quiet period.
    /// </summary>
    public DateTimeOffset UnchangedSince { get; set; }
}

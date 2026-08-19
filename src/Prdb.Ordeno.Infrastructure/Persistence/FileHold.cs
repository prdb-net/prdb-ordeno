namespace Prdb.Ordeno.Infrastructure.Persistence;

/// <summary>
/// A file an undo put back, which no run files again until somebody releases it
/// — ADR 0030.
/// </summary>
/// <remarks>
/// <para>
/// Keyed to the path rather than to a <see cref="DiscoveredFile"/>, because
/// there is no row to key it to: the one that said where the file was is deleted
/// when it is filed, and ADR 0029 deliberately does not put it back. The path is
/// what identifies a file in a download directory everywhere else in this tool.
/// </para>
/// <para>
/// It goes when the file does. The scan drops it when the bytes at that path
/// change — the same statement that forgets prdb's answer and a person's
/// decision — and when a directory it walked no longer holds the file. A
/// directory that could not be read keeps its holds, for the reason it keeps its
/// rows: "I could not look" and "there is nothing there" are not the same
/// answer.
/// </para>
/// </remarks>
public sealed class FileHold
{
    public int Id { get; set; }

    /// <summary>Where the file went back to, which is where the scan will find it again.</summary>
    public required string Path { get; set; }

    /// <summary>When the run that filed it did so.</summary>
    public DateTimeOffset FiledAt { get; set; }

    /// <summary>
    /// Where in the library it had been put. It is on the row because "you put
    /// this back" is the answer to why nothing is happening, and what it had been
    /// filed as is the answer to whether releasing it is a good idea.
    /// </summary>
    public required string FiledTo { get; set; }

    /// <summary>
    /// When the undo put it back. Also what keeps a scan already under way from
    /// sweeping away a hold written after it started looking.
    /// </summary>
    public DateTimeOffset HeldAt { get; set; }
}

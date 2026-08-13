namespace Prdb.Ordeno.Core.Scanning;

/// <summary>
/// One file as the filesystem reports it right now. Three values, because those
/// are the three the tool can read without opening anything.
/// </summary>
public sealed record ObservedFile(string Path, long SizeBytes, DateTimeOffset LastWriteAt);

/// <summary>
/// Walks a source directory and reports the videos in it — ADR 0012 keeps the
/// filesystem out of Core, so this is where the walk is asked for rather than
/// performed.
/// </summary>
public interface ISourceWalker
{
    /// <summary>
    /// Every candidate under <paramref name="root"/>, lazily, so a directory of
    /// four thousand files is processed in batches rather than materialised in
    /// one list.
    /// </summary>
    /// <param name="excluded">
    /// A directory to stay out of, and everything under it: the library. The
    /// configuration already refuses a library inside a download directory, but
    /// it compares the paths as they were typed, and a symlink or a second bind
    /// mount can make two different paths the same place. Filing a video and
    /// then finding it again as a new download is the one loop this tool must
    /// not have.
    /// </param>
    IEnumerable<ObservedFile> Walk(string root, string? excluded, CancellationToken cancellationToken = default);
}

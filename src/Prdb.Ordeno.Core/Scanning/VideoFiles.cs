namespace Prdb.Ordeno.Core.Scanning;

/// <summary>
/// What in a download directory is worth looking at at all. Everything here is
/// decided from a name, which is the only thing that can be decided cheaply
/// while walking thousands of entries — whether a file has finished being
/// written is a separate question, and <see cref="Settling"/> answers it.
/// </summary>
public static class VideoFiles
{
    /// <summary>
    /// The extensions a video arrives under. Deliberately a list rather than
    /// "anything ffprobe can open": opening every file in a download directory
    /// to find out what it is would cost a process per file, and the tool has to
    /// walk a directory of four thousand of them without becoming an event.
    /// </summary>
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".m4v", ".avi", ".mov", ".wmv", ".mpg", ".mpeg",
        ".m2ts", ".ts", ".flv", ".webm", ".divx", ".vob",
    };

    /// <summary>
    /// Directories that exist on a NAS share and never hold a download. Walking
    /// them is not wrong, only pointless — and <c>@eaDir</c> in particular holds
    /// a thumbnail copy of everything around it, which is exactly the kind of
    /// thing that would turn up in the review queue as a mystery.
    /// </summary>
    /// <remarks>
    /// Anything beginning with a dot is skipped as well, which covers
    /// <c>.Trash-1000</c>, <c>.stfolder</c> and whatever the next
    /// synchronisation tool invents.
    /// </remarks>
    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "@eaDir",       // Synology's index and thumbnails
        "#recycle",     // Synology's recycle bin on a share
        "#snapshot",    // Synology's snapshots, read-only copies of everything
        ".@__thumb",    // QNAP's thumbnails
        "$RECYCLE.BIN", // what a Windows client leaves on an SMB share
        "System Volume Information",
        "lost+found",
    };

    /// <summary>
    /// Whether a file name is a video the tool would consider filing.
    /// </summary>
    /// <remarks>
    /// The unfinished names download clients use need no list of their own: they
    /// replace or extend the extension — <c>video.mkv.part</c> for Firefox,
    /// <c>video.mkv.!qB</c> for qBittorrent, <c>.crdownload</c> for Chrome — so
    /// the last extension is no longer a video one and the file falls out here.
    /// The case that does not fall out here is a client writing straight to the
    /// final name, and that one is what <see cref="Settling"/> is for.
    /// </remarks>
    public static bool IsCandidate(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        // A leading dot is a hidden file, and on a share written from a Mac it is
        // usually "._" plus the name of the video next to it: a resource fork of
        // a few kilobytes that would otherwise be identified as its own video.
        if (fileName.StartsWith('.'))
        {
            return false;
        }

        return Extensions.Contains(Path.GetExtension(fileName));
    }

    /// <summary>
    /// Whether a directory should be walked into. Answered on the name alone,
    /// before anything inside it is listed.
    /// </summary>
    public static bool IsWorthWalking(string directoryName) =>
        !directoryName.StartsWith('.') && !IgnoredDirectories.Contains(directoryName);
}

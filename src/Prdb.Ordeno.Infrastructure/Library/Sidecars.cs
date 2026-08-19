using System.Text;

using Microsoft.Extensions.Logging;

using Prdb.Ordeno.Core.Library;

namespace Prdb.Ordeno.Infrastructure.Library;

/// <summary>How writing one sidecar ended.</summary>
public enum SidecarWriteState
{
    /// <summary>There was none, and now there is.</summary>
    Written,

    /// <summary>There was one of the tool's own, and it now says what prdb says today.</summary>
    Replaced,

    /// <summary>
    /// There is one the tool did not write, or could not read. Nothing was
    /// written and nothing was lost.
    /// </summary>
    Kept,

    /// <summary>
    /// It could not be written. The video is filed and has no sidecar, which is a
    /// state the media server handles.
    /// </summary>
    Failed,
}

/// <param name="Problem">What to tell the user. <c>null</c> when there is nothing to say.</param>
public sealed record SidecarOutcome(SidecarWriteState State, string? Problem = null)
{
    public bool Wrote => State is SidecarWriteState.Written or SidecarWriteState.Replaced;
}

/// <summary>
/// The sidecar on disk: whose it is, and how it is replaced without ever being
/// half of anything.
/// </summary>
/// <remarks>
/// <para>
/// It decides nothing about <em>whether</em> to write — <c>FilingPlanner</c> does
/// that, and the answer is on the plan the user read. What it does decide is what
/// happens the moment before it writes, because the two are different moments:
/// somebody may have put their own <c>movie.nfo</c> in that directory in between,
/// and a plan that said "replace" is not a licence to overwrite whatever is there
/// now.
/// </para>
/// <para>
/// A rewrite is a write and a rename, never a truncate. Section 7 of the layout
/// document measured that Jellyfin does not care which of the two it was — it
/// compares modification times and nothing else — so the reason for the careful
/// shape is this repository's own: a container killed between opening a file for
/// writing and finishing it leaves a truncated sidecar, and a truncated XML
/// document is one Jellyfin discards whole, silently, falling back to the file
/// name.
/// </para>
/// </remarks>
public sealed class Sidecars(ILogger<Sidecars> logger) : ISidecars
{
    /// <summary>
    /// How much of an existing sidecar is read to find out whose it is. The
    /// marker is a comment at the top of the document, and a sidecar somebody
    /// generated elsewhere can be megabytes.
    /// </summary>
    private const int HeadBytes = 8 * 1024;

    public SidecarState StateOf(string absolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);

        try
        {
            if (!File.Exists(absolutePath))
            {
                // A directory at that name is in the way as surely as a file is,
                // and it is certainly not something the tool put there.
                return Directory.Exists(absolutePath) ? SidecarState.Foreign : SidecarState.Missing;
            }

            return MovieNfo.IsOurs(Head(absolutePath)) ? SidecarState.Ours : SidecarState.Foreign;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Could not look at the sidecar {Path}.", absolutePath);

            return SidecarState.Unknown;
        }
    }

    /// <summary>
    /// Puts <paramref name="document"/> at <paramref name="absolutePath"/>,
    /// replacing what this tool wrote there before and nothing else.
    /// </summary>
    /// <param name="document">
    /// A document from <see cref="MovieNfo.For"/>. It must carry the marker: a
    /// sidecar the tool writes without one is a file it will refuse to touch
    /// again, which is a bug that only shows up years later.
    /// </param>
    public SidecarOutcome Write(string absolutePath, string document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(document);

        if (!MovieNfo.IsOurs(document))
        {
            throw new ArgumentException(
                "A sidecar this tool writes carries its marker, or it can never be replaced.",
                nameof(document));
        }

        // Asked again, now. The plan the user read was worked out earlier, and
        // this is the last moment before a file is written over.
        var state = StateOf(absolutePath);

        if (state is SidecarState.Foreign or SidecarState.Unknown)
        {
            return new SidecarOutcome(
                SidecarWriteState.Kept,
                $"'{Path.GetFileName(absolutePath)}' in that directory was not written by this tool, "
                + "or could not be read. It was left exactly as it is, so the video is filed with "
                + "the metadata that was already there.");
        }

        // Dotted, so that neither Jellyfin's scanner nor this tool's own walk
        // reads a document that is still being written. In the same directory, so
        // that putting it in place is a rename on one filesystem.
        var staged = Path.Combine(
            Path.GetDirectoryName(absolutePath)!,
            $".{Path.GetFileName(absolutePath)}.{Guid.NewGuid():n}.tmp");

        try
        {
            Stage(staged, document);

            // Over the old one in one step. Until this line the old sidecar is
            // whole and the new one is not read by anything; after it, the other
            // way round. There is no moment where neither is true.
            File.Move(staged, absolutePath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Discard(staged);

            logger.LogWarning(exception, "Could not write the sidecar {Path}.", absolutePath);

            return new SidecarOutcome(
                SidecarWriteState.Failed,
                $"The video is filed, and '{Path.GetFileName(absolutePath)}' could not be written "
                + $"next to it: {exception.Message}. The media server will show the file name until "
                + "there is one.");
        }

        logger.LogInformation("Wrote {Path}.", absolutePath);

        return new SidecarOutcome(
            state is SidecarState.Ours ? SidecarWriteState.Replaced : SidecarWriteState.Written);
    }

    /// <summary>
    /// Takes away a sidecar this tool wrote, and nothing else — ADR 0029.
    /// </summary>
    /// <returns>
    /// What to tell the user about what is left behind, or <c>null</c> when the
    /// file is gone or was never there.
    /// </returns>
    /// <remarks>
    /// Whose it is, is asked here rather than taken from the log: the marker
    /// ADR 0024 puts in the document is the answer, a user who deleted that line
    /// has taken the file back, and an undo is exactly as bound by that as a
    /// rewrite is.
    /// </remarks>
    public string? Remove(string absolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);

        switch (StateOf(absolutePath))
        {
            case SidecarState.Missing:
                return null;

            case SidecarState.Foreign:
                return $"'{Path.GetFileName(absolutePath)}' in that directory was not written by "
                    + "this tool, so it was left exactly where it is.";

            case SidecarState.Unknown:
                return $"'{Path.GetFileName(absolutePath)}' in that directory could not be read, so "
                    + "it was left exactly where it is.";
        }

        try
        {
            File.Delete(absolutePath);

            logger.LogInformation("Removed {Path}, which this tool wrote.", absolutePath);

            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Could not remove the sidecar {Path}.", absolutePath);

            return $"'{Path.GetFileName(absolutePath)}' could not be removed: {exception.Message}";
        }
    }

    private static void Stage(string staged, string document)
    {
        using var writing = new FileStream(
            staged,
            new FileStreamOptions
            {
                // CreateNew: the name carries a fresh guid, so anything already
                // there is a bug rather than a collision to work around.
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
            });

        // GetBytes and not a StreamWriter: the document declares UTF-8 and a
        // byte order mark in front of that declaration is what some readers
        // choke on.
        writing.Write(Encoding.UTF8.GetBytes(document));

        // To the disk rather than to the operating system's cache. What is being
        // defended against is a NAS losing power between the rename and the
        // write reaching it, which would leave the directory pointing at a file
        // with nothing in it.
        writing.Flush(flushToDisk: true);
    }

    private string? Head(string absolutePath)
    {
        using var reading = new FileStream(
            absolutePath,
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.ReadWrite,
            });

        var buffer = new byte[HeadBytes];
        var read = reading.ReadAtLeast(buffer, HeadBytes, throwOnEndOfStream: false);

        // The marker is ASCII and sits at the top, so a multi-byte character cut
        // in half at the end of the buffer costs nothing.
        return Encoding.UTF8.GetString(buffer, 0, read);
    }

    private void Discard(string staged)
    {
        try
        {
            File.Delete(staged);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A dotted file in a scene directory that nothing reads. Saying so is
            // all there is to do about it.
            logger.LogWarning(exception, "Could not remove the unfinished sidecar {Path}.", staged);
        }
    }
}

using Microsoft.Extensions.Logging;

using Prdb.Ordeno.Core.Configuration;

namespace Prdb.Ordeno.Infrastructure.Library;

/// <summary>How one move ended.</summary>
public enum MoveState
{
    Moved,

    /// <summary>The file was not where it was expected. Nothing was written.</summary>
    SourceMissing,

    /// <summary>Something is already at the name. A path that is taken is never an overwrite.</summary>
    TargetTaken,

    /// <summary>
    /// The copy arrived and is not the file that was sent. The original is
    /// untouched and the half-written copy is gone — ADR 0021.
    /// </summary>
    NotVerified,

    /// <summary>A permission, a full disk, a share that went away.</summary>
    Failed,
}

/// <param name="Problem">What to tell the user. <c>null</c> when it worked.</param>
public sealed record MoveOutcome(MoveState State, string? Problem = null)
{
    public bool Moved => State is MoveState.Moved;

    public static readonly MoveOutcome Done = new(MoveState.Moved);
}

/// <summary>
/// The one place in this tool that moves a file somebody cannot get back.
/// </summary>
/// <remarks>
/// <para>
/// It decides nothing. Where a file goes, whether it goes at all and what it is
/// called there are settled by <c>FilingPlanner</c> before this is called, which
/// is what makes a preview and a run the same thing (ADR 0022). This carries a
/// plan out, and every branch in it is about what a failure leaves behind rather
/// than about what to do.
/// </para>
/// <para>
/// Two shapes, per ADR 0002. Within one filesystem a rename, which is instant
/// and cannot half-happen. Across two, a copy into a staging directory, a
/// verification, a rename into place and only then the delete — in that order,
/// with each step conditional on the one before, so that a crash at any point
/// leaves the original where it was.
/// </para>
/// </remarks>
public sealed class LibraryMoves(ILogger<LibraryMoves> logger)
{
    /// <summary>
    /// Where a cross-filesystem copy is written before it is put in place.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Under the library root rather than in the scene directory, and this is
    /// not tidiness. A directory with anything at all in it counts as occupied
    /// (<c>SceneDirectories</c>), so a container killed mid-copy would leave a
    /// part file that makes the scene's own directory look like somebody else's
    /// the next time the same file is filed — and the scene would be filed
    /// around it, under a name carrying prdb's id, for a reason nobody could
    /// see.
    /// </para>
    /// <para>
    /// Dotted, so that Jellyfin's scanner and this tool's own walk both pass
    /// over it. Under the root, so that putting the finished copy in place is a
    /// rename on one filesystem.
    /// </para>
    /// </remarks>
    public const string StagingDirectoryName = ".prdb-ordeno-incoming";

    /// <summary>
    /// A file already in the library, renamed to carry its quality (ADR 0020).
    /// Both names are in one directory, so this is the rename that cannot
    /// half-happen — no bytes are read, copied or deleted.
    /// </summary>
    public MoveOutcome Relabel(string from, string to)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(from);
        ArgumentException.ThrowIfNullOrWhiteSpace(to);

        if (!File.Exists(from))
        {
            return new MoveOutcome(
                MoveState.SourceMissing,
                $"'{Path.GetFileName(from)}' was to be renamed first and is no longer there. "
                + "Nothing was moved.");
        }

        if (Exists(to))
        {
            return new MoveOutcome(
                MoveState.TargetTaken,
                $"'{Path.GetFileName(to)}' already exists, so the file that is filed was not "
                + "renamed and nothing was moved.");
        }

        try
        {
            File.Move(from, to);

            logger.LogInformation("Renamed {From} to {To}.", from, Path.GetFileName(to));

            return MoveOutcome.Done;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Could not rename {From}.", from);

            return new MoveOutcome(
                MoveState.Failed,
                $"'{Path.GetFileName(from)}' could not be renamed: {exception.Message}. Nothing "
                + "was moved.");
        }
    }

    /// <summary>
    /// A video out of a download directory and into the library.
    /// </summary>
    /// <param name="movement">
    /// Which of the two shapes this is. Anything other than
    /// <see cref="FileMovement.Rename"/> takes the careful path: the tool that
    /// cannot prove the two are on one filesystem must not find out by calling a
    /// rename that quietly turns into an unverified copy.
    /// </param>
    public async Task<MoveOutcome> FileAsync(
        string source,
        string target,
        string libraryRoot,
        FileMovement movement,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryRoot);

        if (!File.Exists(source))
        {
            return new MoveOutcome(
                MoveState.SourceMissing,
                "The file was gone by the time it was to be moved. Nothing was written.");
        }

        if (Exists(target))
        {
            // Not an overwrite, ever. What is there was either put there by
            // somebody else or is this scene already filed, and both are
            // answers the planner gives rather than something to write over.
            return new MoveOutcome(
                MoveState.TargetTaken,
                $"Something is already at '{Path.GetFileName(target)}'. Nothing was written — a "
                + "path that is taken is not an overwrite.");
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Could not create the directory for {Target}.", target);

            return new MoveOutcome(
                MoveState.Failed,
                $"The directory this scene goes in could not be created: {exception.Message}");
        }

        return movement is FileMovement.Rename
            ? Rename(source, target)
            : await CopyThenDeleteAsync(source, target, libraryRoot, cancellationToken);
    }

    /// <summary>
    /// Removes what a container killed mid-copy left behind. Called at the start
    /// of a run rather than at the end of one, because the run that would have
    /// cleaned up is exactly the run that did not finish.
    /// </summary>
    public void ClearStaging(string libraryRoot)
    {
        var staging = Path.Combine(libraryRoot, StagingDirectoryName);

        if (!Directory.Exists(staging))
        {
            return;
        }

        foreach (var leftover in Enumerate(staging))
        {
            try
            {
                File.Delete(leftover);

                logger.LogInformation(
                    "Removed {Path}, left behind by a copy that did not finish.",
                    leftover);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception, "Could not remove the leftover copy {Path}.", leftover);
            }
        }
    }

    private IEnumerable<string> Enumerate(string staging)
    {
        try
        {
            return Directory.EnumerateFiles(staging).ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Could not look in {Path} for leftover copies.", staging);

            return [];
        }
    }

    private MoveOutcome Rename(string source, string target)
    {
        try
        {
            File.Move(source, target);

            logger.LogInformation("Filed {Source} as {Target}.", source, target);

            return MoveOutcome.Done;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Could not file {Source}.", source);

            return new MoveOutcome(MoveState.Failed, $"The file could not be moved: {exception.Message}");
        }
    }

    /// <summary>
    /// Copy, verify, put in place, delete — and nothing out of that order.
    /// </summary>
    /// <remarks>
    /// The copy is written under a name nothing reads and only becomes the
    /// library's file once it has been checked, so an interruption at any point
    /// leaves the download directory holding the original and the library
    /// holding nothing half-finished.
    /// </remarks>
    private async Task<MoveOutcome> CopyThenDeleteAsync(
        string source,
        string target,
        string libraryRoot,
        CancellationToken cancellationToken)
    {
        string staged;

        try
        {
            var staging = Directory.CreateDirectory(Path.Combine(libraryRoot, StagingDirectoryName));
            staged = Path.Combine(staging.FullName, $"{Guid.NewGuid():n}.part");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Could not prepare the staging directory under {Root}.", libraryRoot);

            return new MoveOutcome(
                MoveState.Failed,
                $"The library could not be written to: {exception.Message}");
        }

        try
        {
            await CopyAsync(source, staged, cancellationToken);

            var check = CopyVerification.Check(source, staged);

            if (!check.Same)
            {
                Discard(staged);

                return NotVerified(check);
            }

            // Into place, from the same filesystem: a rename, so the library
            // never holds a partly written file under a name it reads.
            File.Move(staged, target);
        }
        catch (OperationCanceledException)
        {
            // The container is stopping. The original has not been touched, and
            // what is thrown away here is a copy nothing has read.
            Discard(staged);

            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Discard(staged);

            logger.LogWarning(exception, "Could not copy {Source} into the library.", source);

            return new MoveOutcome(
                MoveState.Failed,
                $"The file could not be copied into the library: {exception.Message}. It is still "
                + "in the download directory, exactly as it was.");
        }

        // Last, and only now. Everything above can fail without costing the user
        // anything; this is the step that cannot be taken back, and it happens
        // after the copy has been read back and found to be the same file.
        try
        {
            File.Delete(source);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Copied {Source} into the library but could not delete it.", source);

            return new MoveOutcome(
                MoveState.Moved,
                "The video is in the library, and the copy in the download directory could not be "
                + $"deleted: {exception.Message}. Nothing is lost; there are two copies until that "
                + "one is removed.");
        }

        logger.LogInformation("Copied {Source} to {Target} and removed the original.", source, target);

        return MoveOutcome.Done;
    }

    private static async Task CopyAsync(string source, string staged, CancellationToken cancellationToken)
    {
        await using var reading = new FileStream(
            source,
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            });

        await using var writing = new FileStream(
            staged,
            new FileStreamOptions
            {
                // CreateNew: the name carries a fresh guid, so anything already
                // there is a bug rather than a collision to work around.
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous,
            });

        await reading.CopyToAsync(writing, cancellationToken);

        // To the disk rather than to the operating system's cache. The
        // verification that follows is meant to read what was written, and on a
        // share the difference between the two is where an out-of-space error
        // turns up.
        await writing.FlushAsync(cancellationToken);
        writing.Flush(flushToDisk: true);
    }

    /// <summary>
    /// ADR 0021: what cannot be confirmed is not deleted. The user is left with
    /// the file they had, which is the outcome to default to.
    /// </summary>
    private MoveOutcome NotVerified(CopyCheck check)
    {
        logger.LogWarning("A copy into the library was not verified: {Because}.", check.Because);

        return new MoveOutcome(
            MoveState.NotVerified,
            $"The copy could not be confirmed to be the same file — {check.Because}. Nothing was "
            + "deleted, and the video is still in the download directory.");
    }

    /// <summary>
    /// A file the library never showed and nothing has read. Removing it is the
    /// half of "never both halves of a copy" that this side is responsible for.
    /// </summary>
    private void Discard(string staged)
    {
        try
        {
            File.Delete(staged);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // ClearStaging picks it up at the start of the next run. It is under
            // a dotted directory, so nothing reads it in the meantime.
            logger.LogWarning(exception, "Could not remove the unfinished copy {Path}.", staged);
        }
    }

    /// <summary>
    /// A name is taken whether what is at it is a file or a directory. Asking
    /// only about files would let a directory in the way turn into an exception
    /// the user reads as a bug.
    /// </summary>
    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
}

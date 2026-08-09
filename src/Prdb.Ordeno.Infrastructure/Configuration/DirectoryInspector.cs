using Prdb.Ordeno.Core.Configuration;

namespace Prdb.Ordeno.Infrastructure.Configuration;

/// <summary>
/// Asks the filesystem rather than the user. Every check here is performed the
/// way the tool will later perform the real thing — listing a directory, and
/// creating a file in it — because a permission bit read and a permission bit
/// exercised are not the same claim.
/// </summary>
public sealed class DirectoryInspector : IDirectoryInspector
{
    /// <summary>
    /// Written and removed to find out whether the target can be written to.
    /// Named so that a user who finds one left behind by a killed container can
    /// tell what it was.
    /// </summary>
    private const string WriteProbePrefix = ".prdb-ordeno-write-check-";

    public DirectoryInspection Inspect(string path, DirectoryRole role)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new DirectoryInspection(path ?? string.Empty, role, DirectoryProblem.Empty);
        }

        var trimmed = path.Trim();

        if (!Path.IsPathRooted(trimmed))
        {
            return new DirectoryInspection(trimmed, role, DirectoryProblem.NotAbsolute);
        }

        string full;
        try
        {
            full = Path.GetFullPath(trimmed);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new DirectoryInspection(trimmed, role, DirectoryProblem.NotAbsolute);
        }

        if (File.Exists(full))
        {
            return new DirectoryInspection(full, role, DirectoryProblem.NotADirectory);
        }

        if (!Directory.Exists(full))
        {
            return new DirectoryInspection(full, role, DirectoryProblem.Missing);
        }

        if (!CanList(full))
        {
            return new DirectoryInspection(full, role, DirectoryProblem.NotReadable);
        }

        // Only the target is written to. Probing a source for write access would
        // fail a perfectly good read-only download share.
        if (role is DirectoryRole.Target && !CanCreateFiles(full))
        {
            return new DirectoryInspection(full, role, DirectoryProblem.NotWritable);
        }

        return DirectoryInspection.Fine(full, role);
    }

    public FileMovement MovementBetween(string sourcePath, string targetPath)
    {
        var source = MountPointOf(sourcePath);
        var target = MountPointOf(targetPath);

        if (source is null || target is null)
        {
            return FileMovement.Unknown;
        }

        return string.Equals(source, target, StringComparison.Ordinal)
            ? FileMovement.Rename
            : FileMovement.CopyThenDelete;
    }

    /// <summary>
    /// Enumerating one entry is enough: the first step is where a directory the
    /// process may not read refuses.
    /// </summary>
    private static bool CanList(string path)
    {
        try
        {
            using var entries = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
            entries.MoveNext();

            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    private static bool CanCreateFiles(string path)
    {
        var probe = Path.Combine(path, WriteProbePrefix + Guid.NewGuid().ToString("n"));

        try
        {
            // DeleteOnClose so that a process killed between the two lines does
            // not leave the file behind for the user to wonder about.
            using var file = new FileStream(
                probe,
                new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Options = FileOptions.DeleteOnClose,
                });

            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// The mount the path sits on, which is what decides whether a rename is
    /// possible: Linux refuses one across two mounts even when both are the same
    /// filesystem underneath, so comparing mounts answers the real question
    /// rather than an approximation of it.
    /// </summary>
    /// <remarks>
    /// <c>null</c> when no mount claims the path — an answer of "unknown" that
    /// the caller turns into a sentence, rather than a guess at the fast path.
    /// A symlinked directory is resolved to what it points at; a symlink further
    /// up the path is not, which is the one case this can still get wrong.
    /// </remarks>
    private static string? MountPointOf(string path)
    {
        string full;
        try
        {
            full = Path.GetFullPath(path);
            full = Directory.ResolveLinkTarget(full, returnFinalTarget: true)?.FullName ?? full;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var candidate = WithTrailingSeparator(full);
        string? deepest = null;

        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        foreach (var drive in drives)
        {
            string mount;
            try
            {
                mount = WithTrailingSeparator(drive.RootDirectory.FullName);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            // The longest mount that contains the path is the one it is on: /data
            // wins over / for /data/library, which is exactly the case that
            // separates a fast rename from an hour of copying.
            if (candidate.StartsWith(mount, StringComparison.Ordinal)
                && (deepest is null || mount.Length > deepest.Length))
            {
                deepest = mount;
            }
        }

        return deepest;
    }

    private static string WithTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
}

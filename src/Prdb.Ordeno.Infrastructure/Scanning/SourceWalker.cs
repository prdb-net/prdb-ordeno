using Microsoft.Extensions.Logging;

using Prdb.Ordeno.Core.Scanning;

namespace Prdb.Ordeno.Infrastructure.Scanning;

/// <summary>
/// Walks a download directory the way a tool that has to survive a NAS does:
/// iteratively, one directory at a time, and never letting a single unreadable
/// subdirectory end the walk.
/// </summary>
/// <remarks>
/// <para>
/// This does its own recursion instead of asking for
/// <see cref="SearchOption.AllDirectories"/>, for two reasons that both come
/// from the same place. A share is allowed to contain a directory this process
/// may not enter, and the framework's enumeration turns that into an exception
/// that ends everything found so far; and a share is allowed to contain a
/// symlink pointing back up its own tree, which is an endless walk rather than
/// an error. Here the first is a warning and the second is a directory already
/// visited.
/// </para>
/// <para>
/// Nothing in this class opens a file. Everything it reports comes from the
/// directory listing, because the alternative is a stat storm on spinning disks
/// somebody is trying to watch a film from.
/// </para>
/// </remarks>
public sealed class SourceWalker(ILogger<SourceWalker> logger) : ISourceWalker
{
    public IEnumerable<ObservedFile> Walk(
        string root,
        string? excluded,
        CancellationToken cancellationToken = default)
    {
        var start = Canonical(root);
        var library = excluded is null ? null : Canonical(excluded);

        // Where the walk has already been, in canonical form. This is what makes
        // a symlink loop finite: the second time a directory is reached, by
        // whatever path, it is not walked again.
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(start);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var directory = pending.Pop();
            if (!visited.Add(directory))
            {
                continue;
            }

            foreach (var entry in Entries(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (entry is DirectoryInfo subdirectory)
                {
                    if (!VideoFiles.IsWorthWalking(subdirectory.Name))
                    {
                        continue;
                    }

                    var canonical = Canonical(subdirectory.FullName);

                    if (library is not null && IsAtOrInside(canonical, library))
                    {
                        logger.LogDebug(
                            "Not walking into {Directory}: it is the library, or inside it.",
                            subdirectory.FullName);

                        continue;
                    }

                    pending.Push(canonical);

                    continue;
                }

                if (entry is FileInfo file
                    && VideoFiles.IsCandidate(file.Name)
                    && Observe(file) is { } observed)
                {
                    yield return observed;
                }
            }
        }
    }

    /// <summary>
    /// One directory's entries, or none of them. An unreadable directory is a
    /// permission the user can fix and a fact worth logging; it is not a reason
    /// to abandon the other three thousand files.
    /// </summary>
    private IReadOnlyList<FileSystemInfo> Entries(string directory)
    {
        try
        {
            return new DirectoryInfo(directory).EnumerateFileSystemInfos().ToList();
        }
        catch (DirectoryNotFoundException)
        {
            // Removed while the walk was in it. Nothing to report: the rows for
            // whatever was in there are cleaned up by the scan itself.
            return [];
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            logger.LogWarning(
                exception,
                "Skipped {Directory} while scanning: it could not be listed. The tool runs as the "
                + "user given by PUID and PGID.",
                directory);

            return [];
        }
    }

    /// <summary>
    /// Size and modification time, or <c>null</c> if the file is already gone —
    /// a download directory is the one place where a file can disappear between
    /// being listed and being looked at.
    /// </summary>
    private static ObservedFile? Observe(FileInfo file)
    {
        try
        {
            return new ObservedFile(file.FullName, file.Length, file.LastWriteTimeUtc);
        }
        catch (Exception exception) when (exception is FileNotFoundException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// The path with every symlink on its last element resolved, which is what
    /// two paths have to be compared as. A symlink further up the path is not
    /// resolved, and is the one arrangement this can still be fooled by.
    /// </summary>
    private static string Canonical(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);

            return Directory.ResolveLinkTarget(full, returnFinalTarget: true)?.FullName ?? full;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Path.GetFullPath(path);
        }
    }

    private static bool IsAtOrInside(string path, string parent)
    {
        var trimmedPath = Path.TrimEndingDirectorySeparator(path);
        var trimmedParent = Path.TrimEndingDirectorySeparator(parent);

        return string.Equals(trimmedPath, trimmedParent, StringComparison.Ordinal)
            || trimmedPath.StartsWith(trimmedParent + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }
}

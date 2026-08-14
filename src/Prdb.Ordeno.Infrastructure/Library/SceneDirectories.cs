using Prdb.Ordeno.Core.Library;

namespace Prdb.Ordeno.Infrastructure.Library;

/// <summary>
/// Looks at one scene directory on the filesystem the container can see.
/// </summary>
public sealed class SceneDirectories : ISceneDirectories
{
    public SceneDirectoryState StateOf(string absolutePath)
    {
        try
        {
            if (!Directory.Exists(absolutePath))
            {
                // A file sitting where the scene directory goes is in the way as
                // surely as a full directory is. It is rare and it is not
                // nothing: the move would fail, and calling the name free would
                // put that failure in front of the user as a bug rather than as
                // a name to look at.
                return File.Exists(absolutePath) ? SceneDirectoryState.Occupied : SceneDirectoryState.Free;
            }

            // Enumerating rather than counting: the answer is "is there anything
            // at all", and a directory that turns out to hold four thousand
            // entries should cost one of them to find out. A leftover .nfo or a
            // Synology thumbnail counts — this is a target directory, and
            // anything in it means something happened here before.
            return Directory.EnumerateFileSystemEntries(absolutePath).Any()
                ? SceneDirectoryState.Occupied
                : SceneDirectoryState.Free;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A share that went away, or a directory this user may not list.
            // Neither is an empty directory, and saying so would file into it.
            return SceneDirectoryState.Unknown;
        }
    }
}

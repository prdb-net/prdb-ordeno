namespace Prdb.Ordeno.Core.Configuration;

/// <summary>
/// Answers what the filesystem inside the container is really like, so that
/// nothing here has to touch it — ADR 0012.
/// </summary>
public interface IDirectoryInspector
{
    /// <summary>
    /// Looks at one directory in the light of what it is for: a source has to be
    /// readable, the target has to be writable as well.
    /// </summary>
    DirectoryInspection Inspect(string path, DirectoryRole role);

    /// <summary>
    /// Whether a video filed from <paramref name="sourcePath"/> into
    /// <paramref name="targetPath"/> is renamed or copied — see
    /// <see cref="FileMovement"/>. Answers <see cref="FileMovement.Unknown"/>
    /// rather than guessing when it cannot tell.
    /// </summary>
    FileMovement MovementBetween(string sourcePath, string targetPath);
}

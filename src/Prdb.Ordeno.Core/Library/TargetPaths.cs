namespace Prdb.Ordeno.Core.Library;

/// <summary>
/// Turns a recognised scene into the place it is to be filed: the layout's own
/// name where that is free, and the same name carrying prdb's scene id where it
/// is not.
/// </summary>
/// <remarks>
/// This is the step between "prdb says this file is scene X" and moving it, and
/// it writes nothing. Asking what would happen and doing it are therefore the
/// same call, which is what lets the UI show the move before it is made.
/// </remarks>
public sealed class TargetPaths(ISceneDirectories directories)
{
    /// <summary>
    /// Where <paramref name="scene"/> goes in the library at
    /// <paramref name="libraryRoot"/>, for a video currently named
    /// <paramref name="sourceFileName"/> — from which only the extension is
    /// taken, since nothing else about the release name survives filing.
    /// </summary>
    public FilingTarget For(string libraryRoot, Scene scene, string sourceFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryRoot);

        var extension = Path.GetExtension(sourceFileName);
        var preferred = JellyfinPaths.For(scene, extension);

        var state = directories.StateOf(preferred.DirectoryUnder(libraryRoot));

        // A directory that exists and is empty is free: a filing that stopped
        // half way, or a directory the user made, is not somebody else's scene.
        if (state is SceneDirectoryState.Free)
        {
            return FilingTarget.Ready(preferred, libraryRoot);
        }

        // Something is there. Sidestepping is only right for a collision — two
        // scenes the layout gives one name — so a directory that could not be
        // looked at stops here rather than being filed around.
        if (state is SceneDirectoryState.Unknown)
        {
            return new FilingTarget(FilingTargetOutcome.Blocked, null, preferred, libraryRoot);
        }

        var distinguished = JellyfinPaths.For(scene, extension, distinguish: true);

        // The id in that name is this scene's, so a directory already carrying it
        // is this scene already filed — a duplicate or a re-file, and neither is
        // this code's decision to make.
        return directories.StateOf(distinguished.DirectoryUnder(libraryRoot)) is SceneDirectoryState.Free
            ? new FilingTarget(FilingTargetOutcome.CollisionBroken, distinguished, preferred, libraryRoot)
            : new FilingTarget(FilingTargetOutcome.Blocked, null, preferred, libraryRoot);
    }
}

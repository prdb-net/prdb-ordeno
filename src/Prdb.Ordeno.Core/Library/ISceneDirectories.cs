namespace Prdb.Ordeno.Core.Library;

/// <summary>
/// What is already at a computed path. Three answers rather than two, because
/// "something is in the way" and "the tool could not look" lead to different
/// decisions and reporting one as the other is how a library gets merged.
/// </summary>
public enum SceneDirectoryState
{
    /// <summary>Nothing there, or a directory with nothing in it. Both are free to file into.</summary>
    Free,

    /// <summary>A directory with something in it. Whatever that is, it is not this filing's.</summary>
    Occupied,

    /// <summary>It could not be looked at — a permission, a share that went away.</summary>
    Unknown,
}

/// <summary>
/// Answers what is at a path, so that nothing in this project has to touch a
/// filesystem — ADR 0012.
/// </summary>
public interface ISceneDirectories
{
    /// <summary>
    /// Looks at one scene directory. Answers <see cref="SceneDirectoryState.Unknown"/>
    /// rather than guessing: a share that cannot be listed is not an empty
    /// directory, and treating it as one is how a filing writes into somebody
    /// else's scene.
    /// </summary>
    SceneDirectoryState StateOf(string absolutePath);
}

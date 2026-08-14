namespace Prdb.Ordeno.Core.Library;

/// <summary>
/// How the search for a place to file a scene ended.
/// </summary>
public enum FilingTargetOutcome
{
    /// <summary>The name the layout gives this scene was free.</summary>
    Ready,

    /// <summary>
    /// It was not, so prdb's scene id was appended and that name was free. Two
    /// scenes wanting one directory is a correctness problem rather than an
    /// untidy one: filed into the same directory they become one Jellyfin entry
    /// with two versions, and one of them stops existing as a thing of its own.
    /// </summary>
    CollisionBroken,

    /// <summary>
    /// Nothing was filed. Either the id-carrying name is taken as well — which
    /// says this very scene is already filed there, since that id is in the
    /// name — or the directory could not be looked at.
    /// </summary>
    Blocked,
}

/// <summary>
/// Where a scene is to be filed, and what had to be done to get there. Produced
/// without writing anything, so that "what is it about to do" and "do it" are
/// the same computation.
/// </summary>
/// <param name="Path">The names to file under; <c>null</c> when the outcome is <see cref="FilingTargetOutcome.Blocked"/>.</param>
/// <param name="Preferred">
/// What the layout wanted, kept even when it was not free. It is what a report
/// has to quote in order to be intelligible: the user knows the scene, not the
/// id that ended up in the directory name.
/// </param>
public sealed record FilingTarget(
    FilingTargetOutcome Outcome,
    ScenePath? Path,
    ScenePath Preferred,
    string LibraryRoot)
{
    public bool CanBeFiled => Path is not null;

    /// <summary>The scene directory this would be filed into, or <c>null</c> if it would not be.</summary>
    public string? Directory => Path?.DirectoryUnder(LibraryRoot);

    /// <summary>Where the video itself would land, labelled when it is one quality among several.</summary>
    public string? VideoFile(string? versionLabel = null) => Path?.VideoFileUnder(LibraryRoot, versionLabel);

    /// <summary>
    /// What to tell the user, naming the scene rather than the outcome.
    /// <c>null</c> when there is nothing worth saying, which is the ordinary
    /// case.
    /// </summary>
    public string? Message => Outcome switch
    {
        FilingTargetOutcome.Ready => null,

        FilingTargetOutcome.CollisionBroken =>
            $"'{Preferred.SceneDirectory}' already holds something else, so this scene was filed "
            + $"as '{Path?.SceneDirectory}' — the same name with prdb's scene id on the end. Two "
            + "scenes sharing one directory would have become a single entry in the library.",

        _ =>
            $"'{Preferred.SceneDirectory}' is taken and so is the name carrying this scene's prdb "
            + "id, or neither could be looked at. Nothing was moved.",
    };

    public static FilingTarget Ready(ScenePath path, string libraryRoot) =>
        new(FilingTargetOutcome.Ready, path, path, libraryRoot);
}

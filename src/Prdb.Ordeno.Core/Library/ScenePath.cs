namespace Prdb.Ordeno.Core.Library;

/// <summary>
/// Where one scene goes, as names rather than as a path: a site directory, a
/// scene directory below it, and everything inside that directory derived from
/// the scene directory's own name.
/// </summary>
/// <remarks>
/// <para>
/// The invariant this type exists to hold: **the video file name begins with the
/// scene directory name, character for character.** Section 6 of the layout
/// document measured what happens when it does not — a second quality stays a
/// second entry, the resolution token is stripped from both display names, and
/// the library shows two items with identical names. Nothing here composes the
/// two names side by side; the file name is the directory name plus a suffix,
/// which is why they cannot drift apart.
/// </para>
/// <para>
/// The same rule read from the other side is a hazard, not a convenience: it
/// fires on any directory whose file names share its name as a prefix. That is
/// what makes one directory per scene mandatory rather than tidy.
/// </para>
/// </remarks>
/// <param name="Extension">
/// The video's extension, with its dot, lowercased — the file arrives as
/// <c>.MKV</c> often enough, and a library that is half uppercase reads as an
/// accident.
/// </param>
public sealed record ScenePath(string SiteDirectory, string SceneDirectory, string Extension)
{
    /// <summary>
    /// The sidecar's name, which is the same in every scene directory — section 4:
    /// a Movies library reads <c>movie.nfo</c>, and the per-file form collides
    /// with the version grouping. <see cref="MovieNfo"/> is what goes in it;
    /// knowing where it goes is here, because it is a path and this is what holds
    /// paths.
    /// </summary>
    /// <remarks>
    /// One name in every directory is also why a hand-written sidecar cannot be
    /// stepped around the way a taken directory name can — ADR 0024.
    /// </remarks>
    public const string SidecarFileName = "movie.nfo";

    /// <summary>
    /// The names read back off a scene directory the tool filed into earlier,
    /// rather than computed from a scene.
    /// </summary>
    /// <remarks>
    /// A second quality goes next to the first, so its name is derived from the
    /// directory that is <em>there</em> — which may carry a scene id from a
    /// broken collision, or be truncated differently from what the layout would
    /// produce for the same scene today. Recomputing it would put a file into
    /// that directory whose name does not begin with the directory's own, and
    /// section 6 of the layout document is what that costs: two entries with
    /// identical names instead of one with two versions.
    /// </remarks>
    /// <param name="sceneDirectory">The absolute path of the scene directory.</param>
    /// <param name="extension">The extension of the file being named, with its dot.</param>
    public static ScenePath At(string sceneDirectory, string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sceneDirectory);

        var trimmed = sceneDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return new ScenePath(
            Path.GetFileName(Path.GetDirectoryName(trimmed)) ?? string.Empty,
            Path.GetFileName(trimmed),
            extension);
    }

    /// <summary>The video file, as it is named when there is only one of it.</summary>
    public string VideoFileName => SceneDirectory + Extension;

    /// <summary>
    /// The video file named as one quality among several. The bracketed form is
    /// not decoration: without it the two files are not grouped, and the library
    /// shows two items with identical names instead of one item with two
    /// versions.
    /// </summary>
    /// <remarks>
    /// A scene filed once keeps the plain name — section 6 measured that an
    /// unlabelled file is accepted as a version alongside a labelled one, so
    /// nothing has to be renamed when a second quality turns up years later.
    /// </remarks>
    public string VideoFileNameFor(string? versionLabel)
    {
        var label = LibraryNames.Sanitise(versionLabel);

        return string.IsNullOrEmpty(label)
            ? VideoFileName
            : $"{SceneDirectory} - [{label}]{Extension}";
    }

    /// <summary>The scene directory, below the library root the user configured.</summary>
    public string DirectoryUnder(string libraryRoot) =>
        Path.Combine(libraryRoot, SiteDirectory, SceneDirectory);

    public string VideoFileUnder(string libraryRoot, string? versionLabel = null) =>
        Path.Combine(DirectoryUnder(libraryRoot), VideoFileNameFor(versionLabel));

    public string SidecarUnder(string libraryRoot) =>
        Path.Combine(DirectoryUnder(libraryRoot), SidecarFileName);
}

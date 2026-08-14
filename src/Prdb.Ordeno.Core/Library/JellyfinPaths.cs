using System.Globalization;
using System.Text;

namespace Prdb.Ordeno.Core.Library;

/// <summary>
/// The Jellyfin layout, as names: <c>&lt;Site&gt;/&lt;Site&gt; - &lt;yyyy-MM-dd&gt; -
/// &lt;Title&gt;/</c>, and the same without the middle segment where prdb knows no
/// release date (ADR 0019).
/// </summary>
/// <remarks>
/// Nothing here reads a filesystem: the same scene and the same metadata always
/// produce the same names, which is what re-filing, refreshing a sidecar and the
/// operation log all need in order to find what was written earlier.
/// </remarks>
public static class JellyfinPaths
{
    /// <summary>
    /// The names for one scene. <paramref name="distinguish"/> appends prdb's
    /// scene id, which is how a collision is broken — see
    /// <see cref="TargetPaths"/>, which decides when that is called for.
    /// </summary>
    /// <param name="extension">
    /// Taken from the file being filed rather than assumed, since the tool files
    /// what it finds and finds fourteen extensions.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The scene has no site or no title. That is not a shape to fall back for:
    /// ADR 0019 does not file such a file at all, so reaching here with one is a
    /// caller that skipped the question rather than a scene to be named somehow.
    /// </exception>
    public static ScenePath For(Scene scene, string extension, bool distinguish = false)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentException.ThrowIfNullOrWhiteSpace(scene.Site);
        ArgumentException.ThrowIfNullOrWhiteSpace(scene.Title);

        // A site or title made of nothing but reserved characters sanitises to
        // nothing, and an empty path component is worse than an ugly one.
        var fallback = scene.VideoId.ToString("d", CultureInfo.InvariantCulture);
        var site = Or(LibraryNames.Sanitise(scene.Site), fallback);
        var title = Or(LibraryNames.Sanitise(scene.Title), fallback);

        var date = scene.ReleaseDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // ADR 0019: with no date the segment goes, separator and all. Nothing
        // takes its place — a placeholder is either believed by Jellyfin or is a
        // false-looking name for something that is simply not known.
        var name = date is null
            ? $"{site} - {title}"
            : $"{site} - {date} - {title}";

        var suffix = distinguish ? $" [{scene.VideoId:d}]" : string.Empty;

        return new ScenePath(
            LibraryNames.Fit(site, LibraryNames.ComponentBudgetBytes),
            LibraryNames.Fit(name, LibraryNames.SceneDirectoryBudgetBytes - Utf8Length(suffix)) + suffix,
            Extension(extension));
    }

    private static string Or(string name, string fallback) =>
        string.IsNullOrEmpty(name) ? fallback : name;

    private static int Utf8Length(string text) => Encoding.UTF8.GetByteCount(text);

    /// <summary>
    /// The extension as it will be written. A file arriving without one keeps
    /// none: the scanner offers no such file, and inventing <c>.mkv</c> for it
    /// would put a name on disk that lies about what the container is.
    /// </summary>
    private static string Extension(string? extension)
    {
        var name = LibraryNames.Sanitise(extension?.TrimStart('.'));

        return string.IsNullOrEmpty(name) ? string.Empty : "." + name.ToLowerInvariant();
    }
}

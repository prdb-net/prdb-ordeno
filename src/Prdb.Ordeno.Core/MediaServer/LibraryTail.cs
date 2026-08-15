namespace Prdb.Ordeno.Core.MediaServer;

/// <summary>
/// The end of a path, which is the only part this container and the media
/// server's agree on.
/// </summary>
/// <remarks>
/// <para>
/// Both run with their own mounts, and neither configuration names the other's:
/// the directory this tool wrote to as <c>/library/Site/Scene</c> is
/// <c>/media/Site/Scene</c> over there, and the server reports the second. What
/// is identical either side of the two mounts is everything below the library
/// root — the site directory, the scene directory and the file name — because
/// only the prefix differs. Matching on that finds the item, and what is left in
/// front of the match is the substitution, discovered rather than configured
/// (ADR 0018).
/// </para>
/// <para>
/// Always with a leading separator. Without one, a tail of
/// <c>Site/Scene/Scene.mkv</c> would also match a server path ending in
/// <c>OtherSite/Scene/Scene.mkv</c>, and the item refreshed would be somebody
/// else's scene.
/// </para>
/// </remarks>
public static class LibraryTail
{
    /// <summary>
    /// What to look for on the server, given a file this tool filed.
    /// </summary>
    /// <returns>
    /// <c>null</c> when the path is not below the library root, which is nothing
    /// to go looking for rather than something to guess a tail from.
    /// </returns>
    public static string? Of(string libraryRoot, string absolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);

        var root = Separated(libraryRoot).TrimEnd('/');
        var path = Separated(absolutePath);

        return path.Length > root.Length + 1 && path.StartsWith(root + '/', StringComparison.Ordinal)
            ? path[root.Length..]
            : null;
    }

    /// <summary>
    /// The items held at that tail. A list rather than one item: the same
    /// directory can sit in two libraries, and both entries show the sidecar that
    /// has just been rewritten.
    /// </summary>
    public static IReadOnlyList<MediaServerItem> Match(
        IEnumerable<MediaServerItem> items,
        string tail)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentException.ThrowIfNullOrWhiteSpace(tail);

        return [.. items.Where(item => Separated(item.Path).EndsWith(tail, StringComparison.Ordinal))];
    }

    /// <summary>
    /// What the server has in front of the tail — the library root as it sees it.
    /// It is worth saying out loud once, at the connection test, because it is
    /// the part of the setup nobody configured and nobody can see.
    /// </summary>
    public static string Substitution(MediaServerItem item, string tail)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(tail);

        var path = Separated(item.Path);

        return path.EndsWith(tail, StringComparison.Ordinal)
            ? path[..^tail.Length]
            : path;
    }

    /// <summary>
    /// Windows separators flattened to the one both sides of a Docker mount use.
    /// The tool runs in Linux, and the server reports Linux paths; a backslash
    /// here would come from a share written from somewhere else.
    /// </summary>
    private static string Separated(string path) => path.Replace('\\', '/');
}

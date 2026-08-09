namespace Prdb.Ordeno.Core.Configuration;

/// <summary>
/// The shape a library is written in — one per media server.
/// </summary>
/// <remarks>
/// ADR 0008: the first release ships Jellyfin's layout and no other, validated
/// against a real library. One member is the decision, not an oversight; the
/// second arrives with a server somebody has actually run.
/// </remarks>
public enum LibraryLayout
{
    Jellyfin,
}

/// <summary>What the user is choosing between, in the words the UI shows.</summary>
public sealed record LibraryLayoutChoice(LibraryLayout Layout, string Name, string Description);

public static class LibraryLayouts
{
    /// <summary>
    /// Every layout the user may pick. The list is what onboarding renders, so a
    /// second layout becomes an option by being added here rather than by
    /// someone remembering to update a dropdown.
    /// </summary>
    public static IReadOnlyList<LibraryLayoutChoice> All { get; } =
    [
        new(
            LibraryLayout.Jellyfin,
            "Jellyfin",
            "Directories and file names Jellyfin reads, with an .nfo sidecar next to each "
            + "video. Emby understands most of it, Plex does not — those layouts follow in a "
            + "later release."),
    ];

    /// <summary>
    /// The name stored in the database and sent over the wire. A name rather
    /// than a number, so the column reads as itself.
    /// </summary>
    public static string NameOf(LibraryLayout layout) =>
        All.First(choice => choice.Layout == layout).Name;

    /// <summary>
    /// The layout a stored or submitted name refers to, or <c>null</c> if it
    /// refers to none — a database written by a newer version reaches this too,
    /// not only a user typing into the API.
    /// </summary>
    /// <remarks>
    /// Matched against the catalogue above rather than parsed as an enum:
    /// <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/> also accepts
    /// the underlying number, so "0" would quietly become the first layout in
    /// the list.
    /// </remarks>
    public static LibraryLayout? Parse(string? name) =>
        All.FirstOrDefault(choice => string.Equals(choice.Name, name, StringComparison.OrdinalIgnoreCase))
            ?.Layout;
}

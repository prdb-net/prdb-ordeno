namespace Prdb.Ordeno.Core.Library;

/// <summary>
/// The scene a video was recognised as, in the terms a path is built from.
/// </summary>
/// <remarks>
/// The site and the title are required and the date is not —
/// <see href="https://github.com/prdb-net/prdb-ordeno/blob/main/docs/adr/0019-a-missing-date-drops-the-segment.md">ADR 0019</see>.
/// A file prdb cannot name is not filed at all, so the absence of a title is a
/// question the review queue answers rather than a shape this has to have an
/// answer for.
/// </remarks>
/// <param name="VideoId">
/// prdb's id for the scene. It is here because it is what a colliding name is
/// broken with, and because it is the receipt on the whole path: two directories
/// carrying it are demonstrably two scenes.
/// </param>
public sealed record Scene(Guid VideoId, string Site, string Title, DateOnly? ReleaseDate = null);

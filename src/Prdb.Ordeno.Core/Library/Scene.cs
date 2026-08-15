using Prdb.Ordeno.Core.Identification;

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
public sealed record Scene(Guid VideoId, string Site, string Title, DateOnly? ReleaseDate = null)
{
    /// <summary>
    /// The scene an answer from prdb names, or <c>null</c> when it names none
    /// well enough to file. This is the whole of the question "may this file go
    /// into the library", asked in one place so that the filing path reads a rule
    /// rather than catching an exception.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three answers produce <c>null</c>, and the review queue already describes
    /// each of them in <see cref="Recognition.InWords"/>: nothing matched, several
    /// videos matched equally well, and the site rung matched. ADR 0019 sends all
    /// three to that queue rather than into the library.
    /// </para>
    /// <para>
    /// The fourth is a recognised video carrying no title or no site, and it is
    /// here for completeness rather than because it happens. prdb requires both:
    /// <c>Video.SiteId</c> is not nullable in its schema, <c>site</c> and
    /// <c>title</c> are required members of the video document the identify
    /// endpoint returns, and this tool always asks for that document. A null in
    /// either is therefore a malformed answer, not a scene shaped differently —
    /// which is why it goes to the queue rather than getting a layout of its own.
    /// The release date is the one part of that document prdb does leave optional,
    /// and that is the case ADR 0019 answers.
    /// </para>
    /// </remarks>
    public static Scene? From(Recognition recognition)
    {
        ArgumentNullException.ThrowIfNull(recognition);

        if (recognition.State is not RecognitionState.Recognised || recognition.VideoId is not { } videoId)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(recognition.SiteTitle)
            || string.IsNullOrWhiteSpace(recognition.Title)
                ? null
                : new Scene(videoId, recognition.SiteTitle, recognition.Title, recognition.ReleaseDate);
    }
}

using Prdb.Ordeno.Core.Review;

namespace Prdb.Ordeno.Core.Library;

/// <summary>
/// What a sidecar is written from: what prdb knows about one scene, in the terms
/// the media server reads back.
/// </summary>
/// <remarks>
/// <para>
/// It is fetched when the sidecar is written rather than read off the
/// identification row. That row is stored so a screen can name a file without
/// spending a request (ADR 0017), and it is a copy of an answer that may be
/// months old — a title prdb has corrected since is exactly what the sidecar
/// exists to carry.
/// </para>
/// <para>
/// Narrower than <see cref="VideoSummary"/>, and narrower than what section 4 of
/// the layout document says Jellyfin reads. Everything prdb has an answer for is
/// here; the fields it has no answer for — a plot, a genre, a runtime — are left
/// out rather than invented, and a sidecar without them is a perfectly good item.
/// </para>
/// </remarks>
/// <param name="Studio">
/// The site, which is what a studio is here. <c>null</c> where prdb named none,
/// in which case no <c>&lt;studio&gt;</c> is written — an empty one is a
/// browsable studio entry with no name in it.
/// </param>
public sealed record SceneMetadata(
    Guid VideoId,
    string Title,
    DateOnly? ReleaseDate,
    string? Studio,
    IReadOnlyList<string> Performers)
{
    /// <summary>
    /// The metadata for a video prdb described, or <c>null</c> when what came
    /// back has no title.
    /// </summary>
    /// <remarks>
    /// A title is the one thing a sidecar cannot be written without: it is the
    /// name of the item, and a <c>&lt;movie&gt;</c> without one is how a filed
    /// video ends up in the library called nothing. prdb makes it a required
    /// member of the video document, so this is a malformed answer rather than a
    /// scene shaped differently — and the answer to a malformed one is no
    /// sidecar at all, which the media server handles.
    /// </remarks>
    public static SceneMetadata? From(VideoSummary video)
    {
        ArgumentNullException.ThrowIfNull(video);

        return string.IsNullOrWhiteSpace(video.Title)
            ? null
            : new SceneMetadata(
                video.VideoId,
                video.Title,
                video.ReleaseDate,
                string.IsNullOrWhiteSpace(video.SiteTitle) ? null : video.SiteTitle,
                [.. video.Performers.Where(name => !string.IsNullOrWhiteSpace(name))]);
    }
}

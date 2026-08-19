using System.Globalization;

namespace Prdb.Ordeno.Core.Review;

/// <summary>
/// One video, as somebody choosing between several sees it.
/// </summary>
/// <param name="Performers">
/// Who is in it. Two scenes from one site in one month are told apart by this
/// more often than by their titles, which is exactly the case the queue exists
/// for.
/// </param>
/// <param name="ImageUrl">
/// Where the first of prdb's images for this scene is, ready to request — an
/// absolute URL, scheme and host included, whatever the field it arrives in is
/// called. <c>null</c> where prdb has no image, and <c>null</c> for every video
/// a search answered with: only the batch endpoint carries images, which is why
/// this has a default rather than being asked of every caller.
/// </param>
/// <remarks>
/// The image is not shown to anybody choosing between videos. It is here because
/// the answer that carries it is the one filing already asks for (ADR 0027), and
/// a second request to prdb for something already in hand is the pattern
/// ADR 0001 exists to avoid.
/// </remarks>
public sealed record VideoSummary(
    Guid VideoId,
    string? Title,
    DateOnly? ReleaseDate,
    Guid? SiteId,
    string? SiteTitle,
    IReadOnlyList<string> Performers,
    string? ImageUrl = null)
{
    /// <summary>
    /// The video in one line, the way a candidate button or a search result
    /// reads. Everything prdb leaves out is left out rather than replaced with a
    /// placeholder — a row that says "Unknown — Unknown" is harder to skim than a
    /// short one.
    /// </summary>
    public string InWords
    {
        get
        {
            var parts = new List<string>(3);

            if (!string.IsNullOrWhiteSpace(SiteTitle))
            {
                parts.Add(SiteTitle);
            }

            if (ReleaseDate is { } date)
            {
                parts.Add(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            }

            if (Performers.Count > 0)
            {
                parts.Add(string.Join(", ", Performers));
            }

            var title = string.IsNullOrWhiteSpace(Title) ? "A video prdb knows" : Title;

            return parts.Count == 0 ? title : $"{title} — {string.Join(", ", parts)}";
        }
    }
}

/// <summary>
/// What prdb answered when it was asked about videos rather than about files.
/// </summary>
/// <remarks>
/// A failure comes back rather than being thrown, for the reason
/// <see cref="Prdb.Ordeno.Core.Identification.IdentificationAnswer"/> gives: the
/// caller has something specific to do with it, which here is telling the person
/// in front of the screen that prdb could not be asked. It carries no retry
/// schedule, because somebody is waiting for this one and the retry is them
/// pressing the button again.
/// </remarks>
/// <param name="Total">How many videos match in total, of which <paramref name="Videos"/> is one page.</param>
public sealed record VideoLookupAnswer(
    bool Answered,
    IReadOnlyList<VideoSummary> Videos,
    int Total,
    string? Message = null)
{
    public static VideoLookupAnswer From(IReadOnlyList<VideoSummary> videos, int? total = null) =>
        new(true, videos, total ?? videos.Count);

    public static VideoLookupAnswer Stopped(string message) => new(false, [], 0, message);
}

/// <summary>
/// Asks prdb about videos: which ones match what somebody typed, and what the
/// videos it named as candidates actually are.
/// </summary>
/// <remarks>
/// Separate from <see cref="Prdb.Ordeno.Core.Identification.IVideoIdentification"/>
/// because it answers a different question and is asked by a person rather than
/// by a timer. Nothing here identifies a file: it looks videos up, and what is
/// done with the one that comes back is a decision recorded elsewhere.
/// </remarks>
public interface IVideoLookup
{
    /// <summary>
    /// How many videos are asked about at once. prdb's batch endpoint takes
    /// fifty, and this is that limit rather than a page size chosen here.
    /// </summary>
    public const int MaxBatch = 50;

    /// <summary>
    /// The videos matching what somebody typed, newest first, optionally within
    /// one site.
    /// </summary>
    Task<VideoLookupAnswer> SearchAsync(
        string apiKey,
        string query,
        Guid? siteId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// What a handful of videos are, by id. Up to <see cref="MaxBatch"/> at a
    /// time; ids prdb does not know are left out of the answer rather than
    /// failing it.
    /// </summary>
    Task<VideoLookupAnswer> DescribeAsync(
        string apiKey,
        IReadOnlyList<Guid> videoIds,
        CancellationToken cancellationToken = default);
}

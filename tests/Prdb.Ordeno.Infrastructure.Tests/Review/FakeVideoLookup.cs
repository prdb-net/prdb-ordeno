using Prdb.Ordeno.Core.Review;

namespace Prdb.Ordeno.Infrastructure.Tests.Review;

/// <summary>
/// prdb's corpus as a test decides it: a handful of videos, and whether asking
/// about them works at the moment. It replaces the endpoint and nothing else.
/// </summary>
/// <remarks>
/// Both questions are counted, because half of what the queue promises is about
/// how often it asks: a candidate is described once and then read from the
/// database, and a search is a request somebody deliberately spent.
/// </remarks>
internal sealed class FakeVideoLookup : IVideoLookup
{
    private readonly Dictionary<Guid, VideoSummary> videos = [];

    /// <summary>What every call answers with instead, when prdb is having a bad day.</summary>
    public string? Stopped { get; set; }

    /// <summary>
    /// What the answers carry about the quota. The refresh paces off it and
    /// stops before it has spent somebody's hour (ADR 0032), so a test needs to
    /// be able to say what prdb reported.
    /// </summary>
    public Core.Identification.RateLimitReading? Quota { get; set; }

    public List<IReadOnlyList<Guid>> Described { get; } = [];

    public List<string> Searched { get; } = [];

    public List<string> ApiKeys { get; } = [];

    /// <summary>One video, as the caller has already decided it looks.</summary>
    public VideoSummary Knows(VideoSummary video)
    {
        ArgumentNullException.ThrowIfNull(video);

        videos[video.VideoId] = video;

        return video;
    }

    public VideoSummary Knows(
        string title,
        string site = "A Site",
        string? performers = null,
        DateOnly? releaseDate = null)
    {
        var video = new VideoSummary(
            Guid.NewGuid(),
            title,
            releaseDate ?? new DateOnly(2024, 5, 1),
            Guid.NewGuid(),
            site,
            performers is null ? [] : [performers]);

        videos[video.VideoId] = video;

        return video;
    }

    public Task<VideoLookupAnswer> SearchAsync(
        string apiKey,
        string query,
        Guid? siteId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        ApiKeys.Add(apiKey);
        Searched.Add(query);

        if (Stopped is not null)
        {
            return Task.FromResult(VideoLookupAnswer.Stopped(Stopped));
        }

        var found = videos.Values
            .Where(video => video.Title?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)
            .Where(video => siteId is null || video.SiteId == siteId)
            .Take(pageSize)
            .ToList();

        return Task.FromResult(VideoLookupAnswer.From(found));
    }

    public Task<VideoLookupAnswer> DescribeAsync(
        string apiKey,
        IReadOnlyList<Guid> videoIds,
        CancellationToken cancellationToken = default)
    {
        ApiKeys.Add(apiKey);
        Described.Add(videoIds);

        if (Stopped is not null)
        {
            return Task.FromResult(VideoLookupAnswer.Stopped(Stopped));
        }

        // An id prdb does not know is left out of the answer rather than failing
        // it, exactly as the real endpoint does.
        return Task.FromResult(VideoLookupAnswer.From(
            [.. videoIds.Select(id => videos.GetValueOrDefault(id)).OfType<VideoSummary>()],
            rateLimit: Quota));
    }
}

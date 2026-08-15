using Prdb.Ordeno.Core.Identification;
using Prdb.Ordeno.Core.Review;
using Prdb.Ordeno.Host.Scanning;

namespace Prdb.Ordeno.Host.Review;

/// <summary>
/// One video, as a button or a search result.
/// </summary>
/// <param name="Performers">
/// Who is in it, ready to put on a line. It is what tells two candidates from
/// one site in one month apart, which is the case this screen exists for.
/// </param>
public sealed record VideoState(
    Guid VideoId,
    string Answer,
    string? Title,
    DateOnly? ReleaseDate,
    Guid? SiteId,
    string? SiteTitle,
    string? Performers);

/// <summary>
/// One of the videos prdb named when it declined to choose.
/// </summary>
/// <param name="Video">
/// What that video is, or <c>null</c> when prdb has not said. Choosing it is
/// still possible: the id is the answer, and the words only make it quick.
/// </param>
public sealed record ReviewCandidateState(Guid VideoId, string Answer, VideoState? Video);

/// <summary>
/// What a person decided about a file.
/// </summary>
/// <param name="Kind"><c>assigned</c> or <c>dismissed</c>. Both are answers.</param>
/// <param name="From">
/// <c>candidate</c>, <c>search</c>, or <c>null</c> for a dismissal, which came
/// from neither.
/// </param>
public sealed record ResolutionState(
    string Kind,
    string? From,
    DateTimeOffset DecidedAt,
    string Answer,
    Guid? VideoId,
    string? Title,
    DateOnly? ReleaseDate,
    string? SiteTitle);

/// <summary>
/// One file waiting for a person, with everything the tool knows about it.
/// </summary>
public sealed record ReviewEntryState(
    int FileId,
    string Name,
    string Path,
    long SizeBytes,
    DateTimeOffset FirstSeenAt,
    RecognisedState? Recognised,
    IReadOnlyList<ReviewCandidateState> Candidates,
    ResolutionState? Decision);

/// <summary>
/// One site's worth of what is waiting. <paramref name="SiteId"/> is
/// <c>null</c> for the files no site could be read from, which are their own
/// group rather than the absence of one.
/// </summary>
public sealed record ReviewSiteState(Guid? SiteId, string Name, int Waiting);

public sealed record ReviewSummaryState(
    int Waiting,
    int Ambiguous,
    int SiteOnly,
    int Unrecognised,
    int Assigned,
    int Dismissed,
    string WhatIsWaiting);

/// <summary>
/// One page of the queue. It is paged rather than capped: this is work somebody
/// has to get to the end of, and a list that silently stops is one that cannot
/// be emptied.
/// </summary>
public sealed record ReviewQueueState(
    string Filter,
    Guid? Site,
    IReadOnlyList<ReviewEntryState> Entries,
    int Page,
    int Pages,
    int PageSize,
    int Total,
    ReviewSummaryState Summary,
    IReadOnlyList<ReviewSiteState> Sites,
    string? Problem);

/// <summary>
/// What came of one decision: the row as it now is, and how much is left.
/// </summary>
/// <param name="Entry">
/// <c>null</c> when the decision covered more than one file, or when the file is
/// not in a download directory any more.
/// </param>
public sealed record ReviewDecisionState(
    bool Made,
    ReviewEntryState? Entry,
    ReviewSummaryState Summary,
    string? Problem);

/// <summary>
/// What prdb has under what somebody typed. <paramref name="Total"/> is how many
/// there are; the list is the first page of them.
/// </summary>
public sealed record VideoSearchState(
    bool Answered,
    IReadOnlyList<VideoState> Videos,
    int Total,
    string? Problem);

public sealed record AssignRequest(Guid VideoId);

public sealed record DismissManyRequest(IReadOnlyList<int> FileIds);

/// <summary>
/// The queue as the browser reads it. Every state crosses this boundary as a
/// name rather than as a number, the way the rest of the API does — a number in
/// a generated type is a number the browser has to hold a second copy of the
/// meaning of.
/// </summary>
internal static class ReviewState
{
    public static ReviewQueueState Of(ReviewQueue queue) => new(
        Name(queue.Filter),
        queue.Site,
        [.. queue.Entries.Select(Entry)],
        queue.Page,
        queue.Pages,
        ReviewQueue.PageSize,
        queue.Total,
        Summary(queue.Summary),
        [.. queue.Sites.Select(site => new ReviewSiteState(site.SiteId, site.InWords, site.Waiting))],
        queue.Problem);

    public static ReviewDecisionState Of(ReviewDecision decision) => new(
        decision.Made,
        decision.Entry is null ? null : Entry(decision.Entry),
        Summary(decision.Summary),
        decision.Problem);

    public static VideoSearchState Of(VideoLookupAnswer answer) => new(
        answer.Answered,
        [.. answer.Videos.Select(Video)],
        answer.Total,
        answer.Message);

    /// <summary>
    /// The filter a query string names, or the queue itself when it names
    /// nothing the tool knows. An unknown name is not worth a refusal: the
    /// question was "show me the queue", and that is what it gets.
    /// </summary>
    public static ReviewFilter FilterCalled(string? name) => name?.ToLowerInvariant() switch
    {
        "assigned" => ReviewFilter.Assigned,
        "dismissed" => ReviewFilter.Dismissed,
        _ => ReviewFilter.Waiting,
    };

    private static ReviewEntryState Entry(ReviewEntry entry) => new(
        entry.FileId,
        entry.Name,
        entry.Path,
        entry.SizeBytes,
        entry.FirstSeenAt,
        DownloadsState.Recognised(entry.Recognised),
        [.. entry.Candidates.Select(Candidate)],
        entry.Decision is null ? null : Decision(entry.Decision));

    private static ReviewCandidateState Candidate(ReviewCandidate candidate) => new(
        candidate.VideoId,
        candidate.InWords,
        candidate.Video is null ? null : Video(candidate.Video));

    private static VideoState Video(VideoSummary video) => new(
        video.VideoId,
        video.InWords,
        video.Title,
        video.ReleaseDate,
        video.SiteId,
        video.SiteTitle,
        video.Performers.Count == 0 ? null : string.Join(", ", video.Performers));

    private static ResolutionState Decision(Resolution resolution) => new(
        resolution.Kind is ResolutionKind.Assigned ? "assigned" : "dismissed",
        resolution.From switch
        {
            ResolvedFrom.Candidate => "candidate",
            ResolvedFrom.Search => "search",
            _ => null,
        },
        resolution.DecidedAt,
        resolution.InWords,
        resolution.VideoId,
        resolution.Title,
        resolution.ReleaseDate,
        resolution.SiteTitle);

    private static ReviewSummaryState Summary(ReviewSummary summary) => new(
        summary.Waiting,
        summary.Ambiguous,
        summary.SiteOnly,
        summary.Unrecognised,
        summary.Assigned,
        summary.Dismissed,
        summary.WhatIsWaiting);

    private static string Name(ReviewFilter filter) => filter switch
    {
        ReviewFilter.Assigned => "assigned",
        ReviewFilter.Dismissed => "dismissed",
        _ => "waiting",
    };
}

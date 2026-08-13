namespace Prdb.Ordeno.Core.Identification;

/// <summary>
/// One file, as prdb is asked about it.
/// </summary>
/// <param name="Ref">
/// What the answer is mapped back by. It is assigned here rather than read out
/// of the response, because the endpoint answers per <c>ref</c> and a row that
/// cannot be found again would be an answer applied to the wrong file.
/// </param>
/// <param name="FileName">
/// The name alone, not the path. prdb strips the directory anyway, and the
/// directories on somebody's NAS are not part of the question being asked.
/// </param>
public sealed record FileToIdentify(
    string Ref,
    string FileName,
    long SizeBytes,
    string? OsHash,
    string? PerceptualHash);

/// <summary>
/// What prdb answered about one file.
/// </summary>
/// <param name="Candidates">
/// The videos that fitted equally well, when <see cref="MatchConfidence.Ambiguous"/>
/// says several did. Empty otherwise. Nothing here narrows this to one video.
/// </param>
/// <param name="Title">
/// Enough of the video to put on a screen — asked for with the answer so that
/// showing the queue costs no further request and works while prdb is down.
/// It is a copy of what prdb said at that moment and nothing files from it;
/// what a sidecar is written from is fetched again when it is written.
/// </param>
public sealed record RecognisedFile(
    string Ref,
    MatchConfidence Confidence,
    MatchRung? MatchedBy,
    Guid? VideoId,
    string? Title,
    DateOnly? ReleaseDate,
    Guid? SiteId,
    string? SiteTitle,
    IReadOnlyList<Guid> Candidates);

/// <summary>Whether prdb answered, and if not, what stopped it.</summary>
public enum IdentificationStatus
{
    /// <summary>prdb answered. Every file in the batch has a result, including the ones it did not recognise.</summary>
    Answered,

    /// <summary>prdb said no: the key is wrong, or the account cannot use the API.</summary>
    Refused,

    /// <summary>The quota is spent. Nothing is wrong; the rest of the work happens later.</summary>
    RateLimited,

    /// <summary>prdb did not answer at all.</summary>
    Unreachable,
}

/// <summary>
/// The answer to one batch.
/// </summary>
/// <remarks>
/// A failure is returned rather than thrown because the caller has to do
/// something specific with it: leave the files exactly as they were and come
/// back later. AGENTS.md's one hard rule is what this shape is for — there is no
/// path from here to a guess at what a file is.
/// </remarks>
/// <param name="RetryAfter">How long prdb asked to be left alone, when it said so.</param>
public sealed record IdentificationAnswer(
    IdentificationStatus Status,
    IReadOnlyList<RecognisedFile> Results,
    string? Message = null,
    TimeSpan? RetryAfter = null,
    RateLimitReading? RateLimit = null)
{
    public bool Answered => Status is IdentificationStatus.Answered;

    public static IdentificationAnswer From(
        IReadOnlyList<RecognisedFile> results,
        RateLimitReading? rateLimit = null) =>
        new(IdentificationStatus.Answered, results, RateLimit: rateLimit);

    public static IdentificationAnswer Stopped(
        IdentificationStatus status,
        string message,
        TimeSpan? retryAfter = null) =>
        new(status, [], message, retryAfter);
}

/// <summary>
/// What prdb said about the quota on the way past. Every metered answer carries
/// it, so pacing costs nothing — asking <c>GET /rate-limit</c> instead would
/// spend a request to find out whether there are requests left.
/// </summary>
/// <param name="Remaining">Requests left in the hour, or <c>null</c> if the answer carried no reading.</param>
public sealed record RateLimitReading(int? Remaining, TimeSpan? ResetIn);

/// <summary>
/// Asks prdb what a batch of files is. One implementation, over
/// <c>POST /videos/identify</c> — the ladder is not rebuilt here (ADR 0001).
/// </summary>
public interface IVideoIdentification
{
    Task<IdentificationAnswer> IdentifyAsync(
        string apiKey,
        IReadOnlyList<FileToIdentify> files,
        CancellationToken cancellationToken = default);
}

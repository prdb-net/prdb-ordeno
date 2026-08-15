using System.Globalization;

using Prdb.Ordeno.Core.Identification;

namespace Prdb.Ordeno.Core.Review;

/// <summary>
/// Which of the queue's three lists is being read.
/// </summary>
/// <remarks>
/// Waiting is the queue; the other two are what became of it. They are one
/// screen with a filter rather than three, because the question somebody has
/// after dismissing forty files is "what did I just dismiss", and it must not be
/// somewhere else.
/// </remarks>
public enum ReviewFilter
{
    /// <summary>What prdb could not settle and nobody has decided about yet.</summary>
    Waiting,

    /// <summary>Files somebody named a video for. They are filed on the next run.</summary>
    Assigned,

    /// <summary>Files somebody said are not to be filed. They stay where they are.</summary>
    Dismissed,
}

/// <summary>
/// One of the videos prdb named when it declined to choose, as a button.
/// </summary>
/// <param name="Video">
/// What that video is, or <c>null</c> while prdb has not been asked yet — and
/// afterwards too, if it did not know the id. Choosing is still possible either
/// way: the id is the answer, and the words are only what makes choosing quick.
/// </param>
public sealed record ReviewCandidate(Guid VideoId, VideoSummary? Video)
{
    public string InWords => Video?.InWords ?? "A video prdb named but has not described.";
}

/// <summary>
/// One file waiting for a person, with the evidence the tool has about it.
/// </summary>
/// <param name="Name">The path below its download directory, which is what a person recognises.</param>
/// <param name="Recognised">
/// What prdb answered. It is always something here — the queue is what prdb's
/// answers could not settle, not what has not been asked about — and a row shows
/// it because "the site is known" and "nothing matched" are different starting
/// points for the same job.
/// </param>
/// <param name="Decision">What a person decided, once they have. Null while the row is waiting.</param>
public sealed record ReviewEntry(
    int FileId,
    string Name,
    string Path,
    long SizeBytes,
    DateTimeOffset FirstSeenAt,
    Recognition? Recognised,
    IReadOnlyList<ReviewCandidate> Candidates,
    Resolution? Decision = null);

/// <summary>
/// One site's worth of the queue, for narrowing it down.
/// </summary>
/// <remarks>
/// The first day is thousands of files, and the ones prdb could read a site out
/// of arrive in clumps: one site's downloads are one naming convention, one
/// person's evening. Working through them together is the difference between a
/// queue somebody empties and one they close.
/// </remarks>
/// <param name="SiteId">Null for the files no site could be read from, which are their own group.</param>
public sealed record ReviewSite(Guid? SiteId, string? SiteTitle, int Waiting)
{
    public string InWords => SiteTitle ?? "No site";
}

/// <summary>
/// How much is waiting, and what became of the rest.
/// </summary>
public sealed record ReviewSummary(
    int Ambiguous,
    int SiteOnly,
    int Unrecognised,
    int Assigned,
    int Dismissed)
{
    public static readonly ReviewSummary Nothing = new(0, 0, 0, 0, 0);

    public int Waiting => Ambiguous + SiteOnly + Unrecognised;

    /// <summary>
    /// The line at the top of the queue. It says what is there and, when there is
    /// nothing, why that is good news rather than an empty screen.
    /// </summary>
    public string WhatIsWaiting
    {
        get
        {
            if (Waiting == 0)
            {
                return Assigned + Dismissed == 0
                    ? "Nothing is waiting. Everything the tool has found, prdb could name."
                    : "Nothing is waiting. Everything else here has been settled.";
            }

            var parts = new List<string>(3);

            if (Ambiguous > 0)
            {
                parts.Add($"{Number(Ambiguous)} where prdb found more than one match");
            }

            if (SiteOnly > 0)
            {
                parts.Add($"{Number(SiteOnly)} where only the site is known");
            }

            if (Unrecognised > 0)
            {
                parts.Add($"{Number(Unrecognised)} it could not place at all");
            }

            return $"{Count(Waiting, "file")} waiting for you: {Join(parts)}.";
        }
    }

    private static string Join(IReadOnlyList<string> parts) => parts.Count switch
    {
        0 => string.Empty,
        1 => parts[0],
        _ => string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1],
    };

    private static string Count(int number, string singular) =>
        number == 1 ? $"{Number(number)} {singular}" : $"{Number(number)} {singular}s";

    private static string Number(int number) => number.ToString("N0", CultureInfo.InvariantCulture);
}

/// <summary>
/// One page of the queue, and the counts that say how big the whole of it is.
/// </summary>
/// <remarks>
/// Paged rather than capped, unlike the downloads screen. That screen shows
/// examples of what the tool found; this one is work somebody has to get to the
/// end of, and a list that silently stops at two hundred is a list that cannot
/// be emptied.
/// </remarks>
/// <param name="Sites">
/// Counted over everything waiting, not over this page — the filter has to know
/// about the site on page eighty.
/// </param>
public sealed record ReviewQueue(
    ReviewFilter Filter,
    Guid? Site,
    IReadOnlyList<ReviewEntry> Entries,
    int Page,
    int Total,
    ReviewSummary Summary,
    IReadOnlyList<ReviewSite> Sites,
    string? Problem = null)
{
    /// <summary>
    /// Rows per page. It is also the number of candidates that can be described
    /// in one request to prdb — fifty, which is
    /// <see cref="IVideoLookup.MaxBatch"/> — so a page of rows that each carry
    /// candidates costs a handful of requests rather than an unbounded number.
    /// </summary>
    public const int PageSize = 50;

    public static ReviewQueue Empty(string? problem = null) =>
        new(ReviewFilter.Waiting, null, [], 1, 0, ReviewSummary.Nothing, [], problem);

    public int Pages => Total == 0 ? 1 : (Total + PageSize - 1) / PageSize;
}

/// <summary>
/// What came of one decision: the row as it now is, and how much is left.
/// </summary>
/// <remarks>
/// The row rather than the page it was on. Somebody settling forty files should
/// not wait for forty pages to be read back, and the screen already knows which
/// row it just acted on — what it cannot know is the counts, which is why they
/// come along.
/// </remarks>
/// <param name="Entry">
/// The file and what is now decided about it, or <c>null</c> when the decision
/// was refused or covered more than one file.
/// </param>
public sealed record ReviewDecision(
    bool Made,
    ReviewEntry? Entry,
    ReviewSummary Summary,
    string? Problem = null)
{
    public static ReviewDecision Refused(string problem, ReviewSummary? summary = null) =>
        new(false, null, summary ?? ReviewSummary.Nothing, problem);
}

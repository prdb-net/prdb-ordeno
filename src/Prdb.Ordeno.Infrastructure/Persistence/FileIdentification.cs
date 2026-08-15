using Prdb.Ordeno.Core.Identification;

namespace Prdb.Ordeno.Infrastructure.Persistence;

/// <summary>
/// What prdb answered about one discovered file. One row per file, replaced
/// whole the next time the file is asked about.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately a different table from <see cref="DiscoveredFile"/>
/// rather than a handful of columns on it. The file row is an observation and
/// this is a claim about what the video is; keeping them apart is what makes
/// "the bytes changed, so forget what we were told" a delete rather than six
/// fields to remember to clear.
/// </para>
/// <para>
/// The title, date and site are a copy of what prdb said at
/// <see cref="AskedAt"/>, kept so that the screen reads as sentences rather than
/// identifiers and keeps working while prdb is unreachable. It is not a copy of
/// prdb's corpus and must not be used as one — ADR 0001 — and nothing is filed
/// or written from it: what a sidecar is written from is fetched again when it
/// is written.
/// </para>
/// </remarks>
public sealed class FileIdentification
{
    public int Id { get; set; }

    public int DiscoveredFileId { get; set; }

    public DateTimeOffset AskedAt { get; set; }

    public MatchConfidence Confidence { get; set; }

    /// <summary>
    /// The rung that matched, or <c>null</c> when nothing did — and also when
    /// prdb named a rung this build does not know, which is a newer server
    /// rather than an error.
    /// </summary>
    public MatchRung? MatchedBy { get; set; }

    /// <summary>
    /// The video prdb named. Null on an ambiguous answer, where the candidates
    /// are the answer and narrowing them to one is a person's job.
    /// </summary>
    public Guid? VideoId { get; set; }

    public string? Title { get; set; }

    public DateOnly? ReleaseDate { get; set; }

    public Guid? SiteId { get; set; }

    public string? SiteTitle { get; set; }

    /// <summary>
    /// Whether the question carried a perceptual hash. It is what makes
    /// re-identification finite: a file asked about without one is asked again
    /// when the backlog produces it, and a file asked about with one is not
    /// asked again at all.
    /// </summary>
    public bool AskedWithPerceptualHash { get; set; }

    public List<IdentificationCandidate> Candidates { get; } = [];
}

/// <summary>
/// One of the videos that fitted equally well. Several rows mean prdb refused to
/// guess, which is the outcome the review queue exists for.
/// </summary>
/// <remarks>
/// The identify endpoint names the candidates as ids and nothing more, so what a
/// person needs in order to choose between them — a title, a site, a date — is
/// fetched the first time the queue shows the row and kept here. That is the
/// bargain ADR 0017 struck for the answer itself, applied to the one part of it
/// that arrives without words: paid for once, rather than every time somebody
/// opens the page, and readable afterwards while prdb is unreachable.
/// </remarks>
public sealed class IdentificationCandidate
{
    public int Id { get; set; }

    public int FileIdentificationId { get; set; }

    /// <summary>The order prdb listed them in, preserved because it is the only order there is.</summary>
    public int Position { get; set; }

    public Guid VideoId { get; set; }

    /// <summary>
    /// When prdb was asked what this video is, or <c>null</c> while it has not
    /// been. It is the flag rather than the title being null, because a video
    /// whose title prdb does not fill in would otherwise be asked about on every
    /// page view forever.
    /// </summary>
    public DateTimeOffset? DescribedAt { get; set; }

    public string? Title { get; set; }

    public DateOnly? ReleaseDate { get; set; }

    public string? SiteTitle { get; set; }

    /// <summary>
    /// Who is in it, as one line in the order prdb listed them. Two candidates
    /// for one file are usually two scenes from one site in one month, and this
    /// is what tells them apart — a title and a date often do not.
    /// </summary>
    /// <remarks>
    /// One column rather than a table of its own. Nothing here searches, counts
    /// or matches on a performer: this is a line on a button, and a set of rows
    /// would be a corpus of prdb's people growing in a store that must not
    /// become one — ADR 0001.
    /// </remarks>
    public string? Performers { get; set; }
}

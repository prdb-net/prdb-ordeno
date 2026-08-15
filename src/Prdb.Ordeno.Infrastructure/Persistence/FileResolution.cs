using Prdb.Ordeno.Core.Review;

namespace Prdb.Ordeno.Infrastructure.Persistence;

/// <summary>
/// What a person decided about one file. One row per file, and never more than
/// one: a second decision replaces the first rather than joining it.
/// </summary>
/// <remarks>
/// <para>
/// A table of its own rather than columns on <see cref="FileIdentification"/> —
/// ADR 0023. That row is replaced whole every time prdb is asked again, which is
/// what keeps its answer honest and would quietly undo this one. Keeping them
/// apart is also what lets filing read them in a fixed order: the person's answer
/// first, prdb's second.
/// </para>
/// <para>
/// It survives re-identification and goes with the file: the row is deleted with
/// the discovered file, and the scan deletes it when the bytes change, because a
/// decision about last week's file must not name this week's.
/// </para>
/// </remarks>
public sealed class FileResolution
{
    public int Id { get; set; }

    public int DiscoveredFileId { get; set; }

    /// <summary>When the person decided. Not when prdb was asked — that is the other row.</summary>
    public DateTimeOffset DecidedAt { get; set; }

    public ResolutionKind Kind { get; set; }

    /// <summary>
    /// How the video was found, or <c>null</c> for a dismissal. It is recorded
    /// because confirming a candidate prdb offered and finding one it did not are
    /// different pieces of evidence, and one of them is worth contributing back.
    /// </summary>
    public ResolvedFrom? From { get; set; }

    /// <summary>The video the person named. Null on a dismissal, which names none.</summary>
    public Guid? VideoId { get; set; }

    /// <summary>
    /// What prdb says that video is, fetched when the decision was recorded. The
    /// browser sends an id and nothing else: these become a directory name and a
    /// file name, and a path built from what a page posted is a path built from
    /// unvalidated input.
    /// </summary>
    public string? Title { get; set; }

    public DateOnly? ReleaseDate { get; set; }

    public Guid? SiteId { get; set; }

    public string? SiteTitle { get; set; }
}

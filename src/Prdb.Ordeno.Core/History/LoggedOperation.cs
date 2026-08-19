using Prdb.Ordeno.Core.Configuration;
using Prdb.Ordeno.Core.Identification;
using Prdb.Ordeno.Core.Library;
using Prdb.Ordeno.Core.Review;

namespace Prdb.Ordeno.Core.History;

/// <summary>
/// The two things a run does to a filesystem, and therefore the only two things
/// the log holds — ADR 0028.
/// </summary>
public enum OperationKind
{
    /// <summary>A video moved out of a download directory and into the library.</summary>
    Filed,

    /// <summary>
    /// A file the library already held, renamed to carry its quality so that a
    /// second one could go in next to it (ADR 0020). Its own entry, because an
    /// undo that returns the newcomer and leaves this rename in place is half an
    /// undo.
    /// </summary>
    Relabelled,
}

/// <summary>Who named the video an operation is about.</summary>
public enum DecidedBy
{
    /// <summary>prdb, along the ladder ADR 0001 keeps remote.</summary>
    Prdb,

    /// <summary>A person, in the review queue. Their answer outranks prdb's — ADR 0023.</summary>
    Person,
}

/// <summary>
/// Why the tool believed the move was right.
/// </summary>
/// <remarks>
/// This is the half of the log that has nothing to do with undo. It is what
/// turns "it put my file in the wrong place" into something answerable, and it
/// has to be written at the time: the identification row it comes from is
/// deleted with the file it described.
/// </remarks>
/// <param name="Confidence">How prdb graded its own answer, or <c>null</c> when a person decided.</param>
/// <param name="MatchedBy">Which rung answered, or <c>null</c> for the same reason.</param>
/// <param name="DecidedAt">When the answer was given — prdb's, or the person's.</param>
public sealed record OperationReason(
    DecidedBy DecidedBy,
    MatchConfidence? Confidence = null,
    MatchRung? MatchedBy = null,
    DateTimeOffset? DecidedAt = null)
{
    /// <summary>
    /// The sentence under the row. It says who decided first, because that is
    /// the part a person reading their own library wants: their own answer and
    /// prdb's are different kinds of thing, not one that got better.
    /// </summary>
    public string InWords => DecidedBy is DecidedBy.Person
        ? "You named this video in the review queue."
        : Rung is { } rung
            ? $"prdb {rung}{Graded}."
            : $"prdb named this video{Graded}.";

    /// <summary>
    /// Why a file was filed, read off the two answers in the order ADR 0023
    /// fixes: what a person decided, and only then what prdb said. It is the
    /// same order filing itself reads them in, which is what keeps the log's
    /// account of a move and the move's own reason the same thing.
    /// </summary>
    public static OperationReason From(Recognition? recognition, Resolution? decision)
    {
        if (decision is not null)
        {
            // A person's answer carries no rung and no grade. Recording prdb's
            // next to it would say the file was filed for a reason it was not.
            return new OperationReason(History.DecidedBy.Person, DecidedAt: decision.DecidedAt);
        }

        return recognition is null
            ? new OperationReason(History.DecidedBy.Prdb)
            : new OperationReason(
                History.DecidedBy.Prdb,
                recognition.Confidence,
                recognition.MatchedBy,
                recognition.AskedAt);
    }

    private string? Rung => MatchedBy switch
    {
        MatchRung.OsHash => "matched it by its file hash",
        MatchRung.PerceptualHash => "matched it by its perceptual hash",
        MatchRung.FileName => "matched a file name it knows",
        MatchRung.ReleaseName => "read its release name",
        MatchRung.Site => "read the site out of the file name",
        _ => null,
    };

    private string Graded => Confidence switch
    {
        MatchConfidence.Exact => ", an exact match",
        MatchConfidence.Strong => ", a strong match",
        MatchConfidence.Probable => ", a probable match",
        MatchConfidence.Partial => ", a partial match",
        _ => string.Empty,
    };
}

/// <summary>
/// The <c>movie.nfo</c> this operation put in the scene directory.
/// </summary>
/// <remarks>
/// Only the path, because ADR 0024 already put a marker inside the document:
/// whether the file is still the tool's own is a question the file answers, and
/// an undo asks it at the moment it would remove one.
/// </remarks>
public sealed record WrittenSidecar(string Path);

/// <summary>
/// The <c>fanart.jpg</c> this operation downloaded into the scene directory.
/// </summary>
/// <remarks>
/// The length and the fingerprint are the part ADR 0027 left no other way to
/// answer. An image carries no marker — deliberately, because nothing is ever
/// written over one — so "is this still the file this run put here" can only be
/// answered by what was written having been written down. Nothing reads this to
/// decide a write; an undo reads it to decide that removing the file removes
/// nothing somebody else did.
/// </remarks>
/// <param name="Fingerprint">A SHA-256 of the bytes, lowercase hexadecimal.</param>
public sealed record WrittenArtwork(string Path, long Bytes, string Fingerprint);

/// <summary>
/// One change to a filesystem, with the reason it was made — ADR 0028.
/// </summary>
/// <remarks>
/// <para>
/// Both kinds are the same shape on purpose: a file that was at <see cref="From"/>
/// is now at <see cref="To"/>, and undoing either is putting it back. What
/// differs is what came with it, which is why a filing carries a sidecar, an
/// image and a directory it may have created, and a relabel carries none of
/// them.
/// </para>
/// <para>
/// It is not <c>FiledVideo</c>. That row says what is true of the library now
/// and is deleted when it stops being true; this says what happened and is kept
/// until the log is trimmed.
/// </para>
/// </remarks>
/// <param name="Scene">
/// What the video was filed as, as it was named at the time. A title corrected
/// in prdb since does not rewrite history.
/// </param>
/// <param name="From">Where the file was. For a filing, in a download directory.</param>
/// <param name="To">Where it is now, unless something has happened to it since.</param>
/// <param name="SizeBytes">
/// What the scan measured before the move, or <c>null</c> for a relabel, which
/// never read the file. It is half of how an undo tells the file it filed from a
/// file that has changed since.
/// </param>
/// <param name="OsHash">
/// The exact hash the scan read, where there was one — the package answers
/// <c>null</c> below 128 KiB, and a file may be filed before the backlog reaches
/// it.
/// </param>
/// <param name="CreatedDirectory">
/// Whether the scene directory did not exist until this operation. An undo
/// removes a directory it made and never one it found.
/// </param>
public sealed record LoggedOperation(
    int Id,
    int RunId,
    OperationKind Kind,
    Scene? Scene,
    string From,
    string To,
    string? QualityLabel,
    FileMovement Movement,
    long? SizeBytes,
    string? OsHash,
    bool CreatedDirectory,
    WrittenSidecar? Sidecar,
    WrittenArtwork? Artwork,
    OperationReason Reason,
    DateTimeOffset At,
    DateTimeOffset? UndoneAt = null)
{
    public bool Undone => UndoneAt is not null;

    /// <summary>What the file is called now, which is what a row is keyed to a person by.</summary>
    public string Name => System.IO.Path.GetFileName(To);

    /// <summary>What it was called before, which for a filing is the download's own name.</summary>
    public string PreviousName => System.IO.Path.GetFileName(From);

    /// <summary>The scene directory this is in.</summary>
    public string Directory => System.IO.Path.GetDirectoryName(To) ?? To;

    /// <summary>
    /// What happened, in one line. It is written from the entry rather than
    /// stored, because a sentence in a row is a sentence and the columns are the
    /// record.
    /// </summary>
    public string InWords => Kind switch
    {
        OperationKind.Relabelled =>
            $"'{PreviousName}' was renamed to '{Name}' so a second quality could go in next to it.",

        _ => Movement is FileMovement.CopyThenDelete
            ? $"'{PreviousName}' was copied into the library as '{Name}', checked, and removed "
                + "from the download directory."
            : $"'{PreviousName}' was moved into the library as '{Name}'.",
    };
}

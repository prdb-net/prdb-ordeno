using Prdb.Ordeno.Core.Configuration;
using Prdb.Ordeno.Core.History;
using Prdb.Ordeno.Core.Identification;

namespace Prdb.Ordeno.Infrastructure.Persistence;

/// <summary>
/// One change to a filesystem, and why the tool believed it was right —
/// ADR 0028.
/// </summary>
/// <remarks>
/// <para>
/// It is not <see cref="FiledVideo"/>, and the difference is the whole reason
/// there are two tables. That one says what is true of the library now, holds
/// nothing filing does not read, and loses its row the moment the file it names
/// is gone. This one says what happened, keeps saying it after the file has
/// moved on, and is read by an undo and by whoever is trying to work out why a
/// video is where it is.
/// </para>
/// <para>
/// The columns are flat and the fields are known, rather than a document in a
/// blob: the screen filters on them and the undo reads them one at a time.
/// </para>
/// </remarks>
public sealed class OperationEntry
{
    public int Id { get; set; }

    public int RunId { get; set; }

    public OperationKind Kind { get; set; }

    /// <summary>prdb's id for the scene, which is what makes two entries about one scene a pair.</summary>
    public Guid? VideoId { get; set; }

    /// <summary>
    /// What the video was filed as, as it was named at the time. A title
    /// corrected in prdb since does not rewrite what happened.
    /// </summary>
    public string? SceneTitle { get; set; }

    public string? SceneSite { get; set; }

    public DateOnly? SceneReleaseDate { get; set; }

    /// <summary>Where the file was. For a filing, in a download directory.</summary>
    public required string FromPath { get; set; }

    /// <summary>Where it went, and where an undo expects to find it.</summary>
    public required string ToPath { get; set; }

    public string? QualityLabel { get; set; }

    /// <summary>
    /// Whether it was a rename or a copy, a verification and a delete (ADR 0002).
    /// An undo goes back the same way, which is why it is on the entry rather
    /// than worked out again from two paths.
    /// </summary>
    public FileMovement Movement { get; set; }

    /// <summary>
    /// What the scan measured before the move, or <c>null</c> for a relabel,
    /// which never read the file. Half of how an undo tells the file it filed
    /// from one that has changed since.
    /// </summary>
    public long? SizeBytes { get; set; }

    /// <summary>The exact hash the scan read, where there was one. The other half.</summary>
    public string? OsHash { get; set; }

    /// <summary>
    /// Whether the scene directory did not exist until this operation. An undo
    /// removes a directory the tool made and never one it found.
    /// </summary>
    public bool CreatedDirectory { get; set; }

    /// <summary>
    /// The <c>movie.nfo</c> this operation wrote, or <c>null</c> where it wrote
    /// none. Whether it is still the tool's own is a question the file itself
    /// answers, by the marker ADR 0024 puts in it.
    /// </summary>
    public string? SidecarPath { get; set; }

    /// <summary>The <c>fanart.jpg</c> this operation downloaded, or <c>null</c>.</summary>
    public string? ArtworkPath { get; set; }

    /// <summary>How long that image was.</summary>
    public long? ArtworkBytes { get; set; }

    /// <summary>
    /// A SHA-256 of its bytes, lowercase hexadecimal. ADR 0027 left an image
    /// deliberately unmarked, so this is the only thing that can answer "is this
    /// still the file this run put here" — which is the question a removal has,
    /// and not the question a write has.
    /// </summary>
    public string? ArtworkFingerprint { get; set; }

    public DecidedBy DecidedBy { get; set; }

    /// <summary>How prdb graded its answer, or <c>null</c> when a person decided.</summary>
    public MatchConfidence? Confidence { get; set; }

    /// <summary>Which rung answered, or <c>null</c> for the same reason.</summary>
    public MatchRung? MatchedBy { get; set; }

    /// <summary>When that answer was given — prdb's, or the person's.</summary>
    public DateTimeOffset? DecidedAt { get; set; }

    /// <summary>When this happened.</summary>
    public DateTimeOffset At { get; set; }

    /// <summary>
    /// When it was put back, or <c>null</c> while it still stands. Stamped rather
    /// than deleted: an undo that leaves no trace is a screen showing a filing
    /// that appears to have happened while the library disagrees.
    /// </summary>
    public DateTimeOffset? UndoneAt { get; set; }

    /// <summary>Which undo run did that.</summary>
    public int? UndoneByRunId { get; set; }
}

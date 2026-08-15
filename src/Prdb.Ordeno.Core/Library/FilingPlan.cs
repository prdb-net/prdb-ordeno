using Prdb.Ordeno.Core.Configuration;

namespace Prdb.Ordeno.Core.Library;

/// <summary>
/// What filing would do with one video.
/// </summary>
public enum FilingOutcome
{
    /// <summary>Into a scene directory that does not exist yet.</summary>
    Filed,

    /// <summary>
    /// The same, into a directory carrying prdb's scene id because the name the
    /// layout wanted is taken by something else — see <see cref="FilingTarget"/>.
    /// </summary>
    CollisionBroken,

    /// <summary>
    /// Next to a copy of this scene the library already holds, at a quality it
    /// does not (ADR 0003). The copy that is there is relabelled first
    /// (ADR 0020).
    /// </summary>
    SecondQuality,

    /// <summary>
    /// The library already holds this scene at this quality. Nothing is moved
    /// and nothing is deleted; the file stays where it is and is reported —
    /// ADR 0003.
    /// </summary>
    AlreadyFiled,

    /// <summary>
    /// Nothing is moved, and the reason is on the plan. A target that could not
    /// be looked at, a quality that could not be read, an answer from prdb that
    /// names no scene.
    /// </summary>
    Blocked,
}

/// <summary>
/// A file already in the library, renamed to carry its quality before a second
/// one is put next to it — ADR 0020. Both names are in one directory, so this is
/// a rename in the sense of ADR 0002: instant, and unable to half-happen.
/// </summary>
public sealed record FilingRelabel(string From, string To);

/// <summary>
/// What would happen to one video, worked out without touching anything.
/// </summary>
/// <remarks>
/// <para>
/// This is the whole of
/// <see href="https://github.com/prdb-net/prdb-ordeno/blob/main/docs/adr/0022-filing-happens-when-it-is-asked-for.md">ADR 0022</see>:
/// the plan is what the user is shown, and the run is this plan carried out by
/// the code that produced it. It is computed again at the moment of the run
/// rather than carried over, because a directory can be occupied in the seconds
/// between reading a screen and pressing a button.
/// </para>
/// <para>
/// Nothing on it is optional decoration. Every field is either what the move
/// needs or what the sentence under it is built from, which is what keeps the
/// two the same thing.
/// </para>
/// </remarks>
/// <param name="FileId">The discovered file this is about, so a screen can key a row on it.</param>
/// <param name="Movement">
/// Whether this is a rename or a copy, verify and delete — the difference
/// between an instant filing and one that takes as long as the file is large
/// (ADR 0002). Known before anything happens, so the preview can say so.
/// </param>
/// <param name="Relabel">
/// The rename that happens first, or <c>null</c> when there is nothing to
/// relabel — which is every filing except a second quality arriving next to an
/// unlabelled first one.
/// </param>
public sealed record FilingPlan(
    FilingOutcome Outcome,
    int FileId,
    string SourcePath,
    string SourceName,
    Scene? Scene,
    string? QualityLabel,
    string? Directory,
    string? TargetPath,
    FilingRelabel? Relabel,
    FileMovement Movement,
    string? Message)
{
    /// <summary>Whether carrying this plan out moves a file at all.</summary>
    public bool Moves => TargetPath is not null;

    /// <summary>
    /// What the file would be called once it is there. It is what the preview
    /// shows, and it is the whole of what most users want to check.
    /// </summary>
    public string? TargetName => TargetPath is null ? null : System.IO.Path.GetFileName(TargetPath);

    public static FilingPlan Blocked(
        int fileId,
        string sourcePath,
        string sourceName,
        Scene? scene,
        string message) =>
        new(
            FilingOutcome.Blocked,
            fileId,
            sourcePath,
            sourceName,
            scene,
            QualityLabel: null,
            Directory: null,
            TargetPath: null,
            Relabel: null,
            FileMovement.Unknown,
            message);
}

/// <summary>
/// How one plan turned out once it was carried out.
/// </summary>
public enum FilingResultState
{
    /// <summary>The video is in the library and the download directory is emptier by one file.</summary>
    Filed,

    /// <summary>Nothing was moved, as the plan said. Not a failure.</summary>
    Skipped,

    /// <summary>
    /// Something went wrong while moving. What that leaves behind is the subject
    /// of the hard rule in <c>AGENTS.md</c>: never both halves of a copy, and
    /// never a deleted original.
    /// </summary>
    Failed,

    /// <summary>
    /// The container was asked to stop. Whatever was in flight either finished
    /// or was left exactly as it was found.
    /// </summary>
    Stopped,
}

/// <param name="Plan">The plan as it was at the moment of the run, which is what was carried out.</param>
/// <param name="Message">What to tell the user, whether it worked or not.</param>
public sealed record FilingResult(FilingResultState State, FilingPlan Plan, string? Message = null)
{
    public bool Filed => State is FilingResultState.Filed;
}

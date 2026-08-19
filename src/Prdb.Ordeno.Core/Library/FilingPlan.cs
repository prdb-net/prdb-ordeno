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
    /// An undo put this file back, and nothing files it again until somebody
    /// releases it — ADR 0030. It is not <see cref="Blocked"/>: nothing is
    /// wrong, and the way out of it is a button rather than a fix.
    /// </summary>
    Held,

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

/// <summary>What filing would do with the sidecar next to the video.</summary>
public enum SidecarAction
{
    /// <summary>Nothing is written, because nothing is filed.</summary>
    None,

    /// <summary>There is no sidecar in that directory, and one is written.</summary>
    Write,

    /// <summary>
    /// There is one this tool wrote, and it is replaced with what prdb says now.
    /// </summary>
    Replace,

    /// <summary>
    /// There is one this tool did not write, or one it could not read. It stays
    /// exactly as it is and nothing is written.
    /// </summary>
    Keep,
}

/// <summary>
/// What would happen to the sidecar, worked out with the rest of the plan and
/// carried out after the video has moved.
/// </summary>
/// <remarks>
/// <para>
/// It is on the plan for the reason everything else is: a write path that cannot
/// be asked what it would do is a write path the user cannot read before
/// approving it. This one writes a small file rather than moving a large one, and
/// it is still capable of destroying something — a sidecar somebody wrote by hand
/// — which is what <see cref="SidecarAction.Keep"/> exists for.
/// </para>
/// <para>
/// <em>When</em> a sidecar is refreshed is deliberately not settled here. This
/// says what happens as part of filing a video, which is the only thing that
/// writes one today.
/// </para>
/// </remarks>
/// <param name="Path">Where the sidecar is, or <c>null</c> when nothing is filed.</param>
/// <param name="Message">
/// Why it is being left alone, when it is. <c>null</c> everywhere else, where the
/// action speaks for itself.
/// </param>
public sealed record SidecarPlan(SidecarAction Action, string? Path = null, string? Message = null)
{
    public static readonly SidecarPlan None = new(SidecarAction.None);

    public bool Writes => Action is SidecarAction.Write or SidecarAction.Replace;

    /// <summary>
    /// What the row says about the sidecar, or <c>null</c> when there is nothing
    /// to say.
    /// </summary>
    public string? InWords => Message ?? Action switch
    {
        SidecarAction.Write =>
            $"A '{ScenePath.SidecarFileName}' is written next to it, carrying what prdb says the "
            + "scene is. Without one the media server shows the file name and nothing else.",

        SidecarAction.Replace =>
            $"The '{ScenePath.SidecarFileName}' already in that directory was written by this tool, "
            + "and is written again from what prdb says now.",

        _ => null,
    };
}

/// <summary>What filing would do with the image next to the video.</summary>
public enum ArtworkAction
{
    /// <summary>
    /// Nothing is downloaded and nothing is written — because nothing is filed,
    /// or because artwork is switched off, which is what it is by default
    /// (ADR 0027).
    /// </summary>
    None,

    /// <summary>
    /// There is no image in that directory, and one is downloaded if prdb has
    /// one for the scene.
    /// </summary>
    Write,

    /// <summary>
    /// There is already a file at that name, or one that could not be looked at.
    /// It stays exactly as it is and nothing is downloaded.
    /// </summary>
    Keep,
}

/// <summary>
/// What would happen to the image, worked out with the rest of the plan and
/// carried out after the video and the sidecar.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ArtworkAction.Write"/> is a promise to try rather than a promise to
/// produce a file: whether prdb has an image for this scene is in an answer the
/// preview deliberately does not fetch, exactly as the sidecar's is. What the
/// preview does say is the part that is decided here and cannot change under it —
/// which file name, in which directory, and that nothing at that name is written
/// over.
/// </para>
/// <para>
/// There is no <c>Replace</c>, and that is the whole of ADR 0027: a tool that
/// never writes over an image does not need to recognise its own, which is what
/// makes a marker inside the JPEG unnecessary rather than merely awkward.
/// </para>
/// </remarks>
/// <param name="Path">Where the image goes, or <c>null</c> when none would be written.</param>
/// <param name="Message">
/// Why it is being left alone, when it is. <c>null</c> everywhere else, where the
/// action speaks for itself.
/// </param>
public sealed record ArtworkPlan(ArtworkAction Action, string? Path = null, string? Message = null)
{
    public static readonly ArtworkPlan None = new(ArtworkAction.None);

    public bool Writes => Action is ArtworkAction.Write;

    /// <summary>
    /// What the row says about the image, or <c>null</c> when there is nothing
    /// to say — which includes artwork being off, because a setting nobody
    /// turned on is not a remark to put under every file.
    /// </summary>
    public string? InWords => Message ?? (Action is ArtworkAction.Write
        ? $"One image is downloaded next to the video as '{ScenePath.ArtworkFileName}', if prdb "
            + "has one for this scene."
        : null);
}

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
/// <param name="Sidecar">
/// What happens to the <c>movie.nfo</c> in that directory once the video is
/// there. Written after the move rather than before it: a sidecar written first
/// would leave metadata in a directory holding no video if the move then failed,
/// and would make the scene's own directory look occupied to the run that tried
/// again.
/// </param>
/// <param name="Artwork">
/// What happens to the <c>fanart.jpg</c> in that directory, under the same rule
/// as the sidecar and for the same reason — and only where somebody switched
/// artwork on, because spending their connection and their disk is not something
/// that happens by default (ADR 0027).
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
    string? Message,
    SidecarPlan Sidecar,
    ArtworkPlan Artwork)
{
    /// <summary>Whether carrying this plan out moves a file at all.</summary>
    public bool Moves => TargetPath is not null;

    /// <summary>
    /// What the file would be called once it is there. It is what the preview
    /// shows, and it is the whole of what most users want to check.
    /// </summary>
    public string? TargetName => TargetPath is null ? null : System.IO.Path.GetFileName(TargetPath);

    /// <summary>
    /// A file an undo put back, which no run files until somebody releases it —
    /// ADR 0030.
    /// </summary>
    /// <remarks>
    /// It is worked out before anything is asked of the filesystem, quality
    /// included: the hold is the whole answer, and reading the header of a file
    /// that is not going anywhere is work on somebody's NAS for nothing.
    /// </remarks>
    public static FilingPlan Held(
        int fileId,
        string sourcePath,
        string sourceName,
        Scene? scene,
        FilingHold hold) =>
        new(
            FilingOutcome.Held,
            fileId,
            sourcePath,
            sourceName,
            scene,
            QualityLabel: null,
            Directory: null,
            TargetPath: null,
            Relabel: null,
            FileMovement.Unknown,
            (hold ?? throw new ArgumentNullException(nameof(hold))).InWords,
            SidecarPlan.None,
            ArtworkPlan.None);

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
            message,
            SidecarPlan.None,
            ArtworkPlan.None);
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
/// <param name="Sidecar">
/// What became of the sidecar, when that is worth saying: it was left alone, or
/// it could not be written, or prdb could not be asked what to put in it. Silent
/// when it was written, because the plan already said it would be and the video
/// is what the row is about.
/// </param>
/// <param name="Artwork">
/// What became of the image, under the same rule — and silent in one more case
/// than the sidecar: a scene prdb has no image for is the ordinary outcome
/// rather than a problem, so it says nothing at all (ADR 0027).
/// </param>
public sealed record FilingResult(
    FilingResultState State,
    FilingPlan Plan,
    string? Message = null,
    string? Sidecar = null,
    string? Artwork = null)
{
    public bool Filed => State is FilingResultState.Filed;
}

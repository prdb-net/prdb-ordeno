namespace Prdb.Ordeno.Core.Identification;

/// <summary>
/// Why a video did or did not produce a perceptual hash. These are the package's
/// outcomes, kept as the tool's own type: they are written into the database and
/// read back by a later build, so they belong to this application.
/// </summary>
public enum PerceptualHashState
{
    Computed,

    /// <summary>The file was gone by the time its turn came.</summary>
    SourceMissing,

    /// <summary>ffprobe reported no usable duration, so the sample points are unknown.</summary>
    ProbeFailed,

    /// <summary>ffmpeg could not produce one of the 25 frames.</summary>
    FrameCaptureFailed,

    /// <summary>A frame came back as something the reader does not handle.</summary>
    FrameDecodeFailed,

    /// <summary>ffprobe or ffmpeg took longer than it was given.</summary>
    TimedOut,

    /// <summary>
    /// Neither of the above: hashing threw where the package promises it does
    /// not. Recorded rather than lost, because a backlog that stops on one file
    /// stops for every file behind it.
    /// </summary>
    Failed,
}

/// <param name="Error">A short description of the failure, for the container's log.</param>
public sealed record PerceptualHashReading(PerceptualHashState State, string? Hash = null, string? Error = null)
{
    public bool Computed => State is PerceptualHashState.Computed && Hash is not null;
}

/// <summary>
/// The perceptual hash of a video. Computing one decodes 25 frames, which is why
/// nothing calls this in the path of a file being imported.
/// </summary>
public interface IPerceptualHashes
{
    Task<PerceptualHashReading> ComputeAsync(string path, CancellationToken cancellationToken = default);
}

/// <summary>
/// How the backlog treats a file it could not hash.
/// </summary>
/// <remarks>
/// Almost every failure is a property of the file — a truncated download, a
/// container ffmpeg cannot seek — and repeating it costs twenty-five frame
/// decodes to learn the same thing, on a machine somebody is also trying to
/// watch films from. Only <see cref="PerceptualHashState.TimedOut"/> earns
/// another go, because it says as much about how busy the disk was as about the
/// file.
/// </remarks>
public static class PerceptualHashBacklog
{
    /// <summary>How often one file is tried before the answer is taken as final.</summary>
    public const int MaxAttempts = 3;
}

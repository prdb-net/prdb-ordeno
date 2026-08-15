using System.Globalization;

namespace Prdb.Ordeno.Core.Library;

/// <summary>
/// The size of the picture, and the name the library knows it by.
/// </summary>
/// <remarks>
/// <para>
/// prdb identifies the scene and not the encode somebody happens to have
/// (<see href="https://github.com/prdb-net/prdb-ordeno/blob/main/docs/adr/0003-duplicates-are-skipped-not-deleted.md">ADR 0003</see>),
/// so this is read out of the file. It decides two things: whether an arriving
/// video is a second quality of a filed scene or the same one again, and what
/// goes in the brackets when both are kept.
/// </para>
/// <para>
/// The label is this tool's own name for a size, and it only has to be stable
/// and comparable. Section 6 of the layout document measured that Jellyfin
/// derives the version label from the file name rather than from the picture, so
/// nothing on the server side depends on agreeing with what it would have called
/// it.
/// </para>
/// </remarks>
public sealed record VideoQuality
{
    public VideoQuality(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        Width = width;
        Height = height;
        Label = LabelFor(width, height);
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>
    /// <c>2160p</c>, <c>1080p</c> and so on — what goes in the brackets, and
    /// what two videos of one scene are compared by.
    /// </summary>
    /// <remarks>
    /// Compared as this string and never as the dimensions, per
    /// <see href="https://github.com/prdb-net/prdb-ordeno/blob/main/docs/adr/0020-a-second-quality-relabels-the-filed-file.md">ADR 0020</see>:
    /// 1918×1080 and 1920×1080 are the same quality, and treating them as two
    /// would file both and then want to give them one name.
    /// </remarks>
    public string Label { get; }

    /// <summary>
    /// The sizes a release is cut to, largest first. A file is called by the
    /// first of these it reaches.
    /// </summary>
    private static readonly (int Height, int Width, string Label)[] Standards =
    [
        (2160, 3840, "2160p"),
        (1440, 2560, "1440p"),
        (1080, 1920, "1080p"),
        (720, 1280, "720p"),
        (576, 1024, "576p"),
        (480, 854, "480p"),
        (360, 640, "360p"),
        (240, 426, "240p"),
    ];

    /// <summary>
    /// The name for one size, decided by whichever of the two dimensions gets
    /// there first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Width has to count, or every scope-ratio release is misnamed: 3840×1600
    /// is a 4K encode with letterboxing taken out of the file rather than a tall
    /// 1080p one, and calling it <c>1600p</c> — or worse, <c>1080p</c> — would
    /// make it a second quality of itself the next time the same release turned
    /// up in its full frame.
    /// </para>
    /// <para>
    /// The threshold for each name is halfway to the next one down, so a
    /// standard size is never within reach of two of them. That is what keeps
    /// 1024×576 a <c>576p</c> web rip rather than a <c>720p</c> one, which the
    /// obvious "eighty per cent of the standard" rule gets wrong.
    /// </para>
    /// </remarks>
    private static string LabelFor(int width, int height)
    {
        for (var index = 0; index < Standards.Length; index++)
        {
            var (standardHeight, standardWidth, label) = Standards[index];

            // The last one has nothing below it to be halfway to, so it takes
            // everything that reaches it and the fallback keeps the rest.
            var next = index + 1 < Standards.Length ? Standards[index + 1] : (Height: 0, Width: 0, Label: string.Empty);

            if (height >= Halfway(standardHeight, next.Height) || width >= Halfway(standardWidth, next.Width))
            {
                return label;
            }
        }

        // Smaller than anything anyone releases. Named after itself rather than
        // rounded up to 240p, which would claim two different things are one.
        return height.ToString(CultureInfo.InvariantCulture) + "p";
    }

    private static int Halfway(int standard, int next) => next == 0 ? standard : (standard + next) / 2;
}

/// <summary>
/// Why a video did or did not give up its size.
/// </summary>
public enum VideoQualityState
{
    Read,

    /// <summary>The file was gone by the time it was asked about.</summary>
    SourceMissing,

    /// <summary>ffprobe answered, and there was no video stream with a size in it.</summary>
    NoVideoStream,

    /// <summary>ffprobe could not read the file at all.</summary>
    Unreadable,

    /// <summary>ffprobe took longer than it was given — a share gone quiet, most often.</summary>
    TimedOut,
}

/// <param name="Error">A short description of the failure, for the container's log.</param>
public sealed record VideoQualityReading(
    VideoQualityState State,
    VideoQuality? Quality = null,
    string? Error = null)
{
    public bool WasRead => State is VideoQualityState.Read && Quality is not null;

    public static VideoQualityReading Of(int width, int height) =>
        new(VideoQualityState.Read, new VideoQuality(width, height));

    /// <summary>
    /// What to tell the user about a file that could not be measured. It is a
    /// sentence rather than a state name because it is the whole reason their
    /// video was left in the download directory.
    /// </summary>
    public string? Message => State switch
    {
        VideoQualityState.Read => null,

        VideoQualityState.SourceMissing =>
            "The file was gone by the time the tool looked at it.",

        VideoQualityState.NoVideoStream =>
            "There is no video track in this file that ffprobe can measure, so the tool cannot "
            + "tell what quality it is — and without that it cannot tell a second quality of a "
            + "scene from a second copy of one. Nothing was moved.",

        VideoQualityState.TimedOut =>
            "Reading this file took too long, which usually means the share it is on stopped "
            + "answering. Nothing was moved; the next run tries again.",

        _ =>
            "ffprobe could not read this file, so the tool cannot tell what quality it is — and "
            + "without that it cannot tell a second quality of a scene from a second copy of one. "
            + "A file that will not open here is unlikely to play either. Nothing was moved.",
    };
}

/// <summary>
/// Reads the size of the picture out of a video. ffprobe reads a container
/// header, which is the cheap end of what the image ships ffmpeg for — unlike
/// the twenty-five frame decode a perceptual hash costs, this sits in the filing
/// path with nothing queued behind it (ADR 0020).
/// </summary>
public interface IVideoQualities
{
    Task<VideoQualityReading> ReadAsync(string path, CancellationToken cancellationToken = default);
}

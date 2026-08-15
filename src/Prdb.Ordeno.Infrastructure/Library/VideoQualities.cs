using System.Diagnostics;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using Prdb.Ordeno.Core.Library;

namespace Prdb.Ordeno.Infrastructure.Library;

/// <summary>
/// Reads the size of the picture with the <c>ffprobe</c> the image ships
/// (ADR 0013).
/// </summary>
/// <remarks>
/// <para>
/// This asks for a header and nothing else — no decoding, no seeking — so it is
/// milliseconds on a local file and one round trip on a share. That is what lets
/// ADR 0020 put it in the filing path, where a perceptual hash could never go.
/// </para>
/// <para>
/// It is a process rather than a library because ffmpeg is a process: the image
/// carries the binaries for exactly this kind of question, and a managed
/// container parser would be a second answer to it that has to be kept true of
/// fourteen extensions.
/// </para>
/// </remarks>
public sealed class VideoQualities(ILogger<VideoQualities> logger) : IVideoQualities
{
    /// <summary>
    /// Found on the path, which is where the image puts it. Not configurable:
    /// ADR 0009 keeps the environment to what must exist before the application
    /// starts, and a user who has to tell the tool where ffprobe is has an image
    /// that is broken in a way a setting would only hide.
    /// </summary>
    private const string Ffprobe = "ffprobe";

    /// <summary>
    /// Long enough for a NAS that has to spin a disk up, short enough that a
    /// share which has stopped answering does not hold a filing run open. The
    /// file is not filed either way; the next run asks again.
    /// </summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public async Task<VideoQualityReading> ReadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            return new VideoQualityReading(VideoQualityState.SourceMissing);
        }

        try
        {
            var (exitCode, output, error) = await RunAsync(path, cancellationToken);

            // ffprobe answers with an exit code and prints why on stderr. A file
            // it cannot open is the ordinary failure here — a truncated
            // download, a container it does not know — and it is the file's
            // property rather than the tool's.
            return exitCode == 0
                ? Parse(output, path)
                : new VideoQualityReading(VideoQualityState.Unreadable, Error: Shorten(error));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("ffprobe did not answer about {Path} within {Timeout}.", path, Timeout);

            return new VideoQualityReading(VideoQualityState.TimedOut);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            logger.LogWarning(exception, "ffprobe could not be run for {Path}.", path);

            return new VideoQualityReading(VideoQualityState.Unreadable, Error: exception.Message);
        }
    }

    private static VideoQualityReading Parse(string output, string path)
    {
        using var document = JsonDocument.Parse(output);

        if (!document.RootElement.TryGetProperty("streams", out var streams)
            || streams.ValueKind is not JsonValueKind.Array)
        {
            return new VideoQualityReading(VideoQualityState.NoVideoStream);
        }

        foreach (var stream in streams.EnumerateArray())
        {
            // Cover art is a video stream by ffprobe's reckoning, and taking its
            // size would name a scene after its poster — 600x900, which rounds
            // to nothing anyone releases and would make every re-import a second
            // quality of the first.
            if (IsAttachedPicture(stream))
            {
                continue;
            }

            if (Dimension(stream, "width") is { } width && Dimension(stream, "height") is { } height)
            {
                return VideoQualityReading.Of(width, height);
            }
        }

        return new VideoQualityReading(
            VideoQualityState.NoVideoStream,
            Error: $"ffprobe found no video stream with a size in {path}.");
    }

    private static bool IsAttachedPicture(JsonElement stream) =>
        stream.TryGetProperty("disposition", out var disposition)
        && disposition.TryGetProperty("attached_pic", out var attached)
        && attached.ValueKind is JsonValueKind.Number
        && attached.GetInt32() == 1;

    private static int? Dimension(JsonElement stream, string name) =>
        stream.TryGetProperty(name, out var value)
        && value.ValueKind is JsonValueKind.Number
        && value.TryGetInt32(out var number)
        && number > 0
            ? number
            : null;

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(Ffprobe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // As separate arguments rather than a command line: a scene title can
        // contain anything, and a path is not a string to be quoted by hand.
        start.ArgumentList.Add("-v");
        start.ArgumentList.Add("error");
        start.ArgumentList.Add("-select_streams");
        start.ArgumentList.Add("v");
        start.ArgumentList.Add("-show_entries");
        start.ArgumentList.Add("stream=width,height:stream_disposition=attached_pic");
        start.ArgumentList.Add("-of");
        start.ArgumentList.Add("json");
        start.ArgumentList.Add(path);

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException($"{Ffprobe} did not start.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        // Both pipes are drained while the process runs. A file whose header
        // produces more output than a pipe buffer holds would otherwise block
        // ffprobe on a write while this waits for it to exit.
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);

        try
        {
            await process.WaitForExitAsync(timeout.Token);

            return (process.ExitCode, await output, await error);
        }
        catch (OperationCanceledException)
        {
            Stop(process);

            throw;
        }
    }

    private static void Stop(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException
            or System.ComponentModel.Win32Exception)
        {
            // It exited between the question and the answer, which is the
            // outcome that was wanted.
        }
    }

    /// <summary>
    /// ffprobe's complaint, cut to something a screen can carry. The whole of it
    /// is in the container's log.
    /// </summary>
    private static string? Shorten(string error)
    {
        var line = error.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        return string.IsNullOrEmpty(line) ? null : line.Length <= 200 ? line : line[..200];
    }
}

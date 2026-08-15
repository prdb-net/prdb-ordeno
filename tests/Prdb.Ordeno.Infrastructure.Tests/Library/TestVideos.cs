using System.Diagnostics;

namespace Prdb.Ordeno.Infrastructure.Tests.Library;

/// <summary>
/// Real video files, made with the ffmpeg the image ships and the tests
/// therefore require. A fixture generated here is the only way to test the
/// reading of a picture size against something that has one — a fake would test
/// the parser and not the question.
/// </summary>
internal static class TestVideos
{
    /// <summary>
    /// A second of test pattern at the given size. Small enough to make in
    /// milliseconds and to leave in a temporary directory.
    /// </summary>
    public static string Write(string path, int width, int height)
    {
        Run(
            "ffmpeg",
            [
                "-v", "error",
                "-f", "lavfi",
                "-i", $"testsrc=size={width}x{height}:duration=1:rate=5",
                "-pix_fmt", "yuv420p",
                "-y", path,
            ]);

        return path;
    }

    /// <summary>
    /// A file with sound and no picture. It is the case that separates "there is
    /// no video here" from "this file is broken", and the two lead to different
    /// sentences.
    /// </summary>
    public static string WriteAudioOnly(string path)
    {
        Run(
            "ffmpeg",
            [
                "-v", "error",
                "-f", "lavfi",
                "-i", "sine=frequency=440:duration=1",
                "-y", path,
            ]);

        return path;
    }

    private static void Run(string command, string[] arguments)
    {
        var start = new ProcessStartInfo(command)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException($"{command} did not start.");

        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"{command} failed with {process.ExitCode}. The image ships ffmpeg and these "
                + $"tests need it on the path.{Environment.NewLine}{error}");
        }
    }
}

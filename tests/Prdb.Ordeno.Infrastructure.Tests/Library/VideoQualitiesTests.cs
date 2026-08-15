using Microsoft.Extensions.Logging.Abstractions;

using Prdb.Ordeno.Core.Library;
using Prdb.Ordeno.Infrastructure.Library;

using Xunit;

namespace Prdb.Ordeno.Infrastructure.Tests.Library;

/// <summary>
/// Reading the picture size out of real files with the real ffprobe. Every case
/// here is one the filing path turns into a decision it cannot take back, and
/// the failures are as much the subject as the successes: a file that cannot be
/// measured is a file that is not filed (ADR 0020), so an answer of "unreadable"
/// mistaken for "no picture" — or for a size — changes what happens to somebody's
/// video.
/// </summary>
public sealed class VideoQualitiesTests
{
    private readonly VideoQualities qualities = new(NullLogger<VideoQualities>.Instance);

    [Theory]
    [InlineData(1920, 1080, "1080p")]
    [InlineData(1280, 720, "720p")]
    [InlineData(3840, 1600, "2160p")]
    public async Task A_video_is_measured_as_it_was_encoded(int width, int height, string label)
    {
        using var temp = new TempDirectory();
        var file = TestVideos.Write(temp.Combine("scene.mkv"), width, height);

        var reading = await qualities.ReadAsync(file);

        Assert.True(reading.WasRead);
        Assert.Equal(width, reading.Quality?.Width);
        Assert.Equal(height, reading.Quality?.Height);
        Assert.Equal(label, reading.Quality?.Label);
    }

    /// <summary>
    /// A file with no video in it. The tool has one, because the scanner takes a
    /// file by its extension and an <c>.mkv</c> holding only sound is a
    /// perfectly ordinary mistake.
    /// </summary>
    [Fact]
    public async Task A_file_with_no_picture_says_so()
    {
        using var temp = new TempDirectory();
        var file = TestVideos.WriteAudioOnly(temp.Combine("sound-only.mkv"));

        var reading = await qualities.ReadAsync(file);

        Assert.Equal(VideoQualityState.NoVideoStream, reading.State);
        Assert.Null(reading.Quality);
        Assert.NotNull(reading.Message);
    }

    /// <summary>
    /// The ordinary failure on a real download directory: a truncated file, or
    /// something that was never a video. It is the file's property and not an
    /// error of the tool's, so it is an answer rather than an exception.
    /// </summary>
    [Fact]
    public async Task Something_that_is_not_a_video_is_unreadable()
    {
        using var temp = new TempDirectory();
        var file = temp.Combine("not-really.mkv");
        await File.WriteAllTextAsync(file, "this was never a video");

        var reading = await qualities.ReadAsync(file);

        Assert.Equal(VideoQualityState.Unreadable, reading.State);
        Assert.Null(reading.Quality);
    }

    /// <summary>
    /// Distinct from unreadable on purpose. A file that has gone is a scan that
    /// is out of date, and the next one puts it right; a file that will not open
    /// is a file the user has to look at.
    /// </summary>
    [Fact]
    public async Task A_file_that_is_gone_is_not_a_file_that_is_broken()
    {
        using var temp = new TempDirectory();

        var reading = await qualities.ReadAsync(temp.Combine("never-existed.mkv"));

        Assert.Equal(VideoQualityState.SourceMissing, reading.State);
    }

    /// <summary>
    /// The container stopping is not an answer about the file, and recording one
    /// would leave a claim behind that nothing measured.
    /// </summary>
    [Fact]
    public async Task A_cancelled_run_throws_rather_than_answering()
    {
        using var temp = new TempDirectory();
        var file = TestVideos.Write(temp.Combine("scene.mkv"), 1280, 720);

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => qualities.ReadAsync(file, cancelled.Token));
    }
}

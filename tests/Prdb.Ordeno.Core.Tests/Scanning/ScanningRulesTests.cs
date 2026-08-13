using Prdb.Ordeno.Core.Scanning;

using Xunit;

namespace Prdb.Ordeno.Core.Tests.Scanning;

/// <summary>
/// The two questions asked of every file in a download directory: is this a
/// video at all, and has it finished being written. Both are decided here so
/// that the filesystem never has to be involved in checking them.
/// </summary>
public sealed class ScanningRulesTests
{
    [Theory]
    [InlineData("Site.Name.25.11.03.Scene.Title.1080p.mkv")]
    [InlineData("video.mp4")]
    [InlineData("VIDEO.MKV")]
    [InlineData("a video with spaces.avi")]
    public void A_video_is_a_candidate(string name) => Assert.True(VideoFiles.IsCandidate(name));

    /// <summary>
    /// The names download clients use while they are still writing. Every one of
    /// them stops being a video by the only rule there is — the last extension —
    /// which is why there is no second list to keep up to date.
    /// </summary>
    [Theory]
    [InlineData("video.mkv.part")]        // Firefox, and others
    [InlineData("video.mkv.!qB")]         // qBittorrent
    [InlineData("video.mkv.crdownload")]  // Chrome
    [InlineData("video.mkv.aria2")]
    [InlineData("archive.rar")]
    [InlineData("archive.r00")]
    [InlineData("notes.txt")]
    [InlineData("video")]
    [InlineData("")]
    public void An_unfinished_or_unrelated_name_is_not(string name) =>
        Assert.False(VideoFiles.IsCandidate(name));

    /// <summary>
    /// A Mac writing to a share leaves "._" next to every file: a few kilobytes
    /// of resource fork carrying the video's whole name, which would otherwise
    /// be identified as a video of its own.
    /// </summary>
    [Theory]
    [InlineData("._video.mkv")]
    [InlineData(".hidden.mkv")]
    public void A_hidden_file_is_not_a_candidate(string name) =>
        Assert.False(VideoFiles.IsCandidate(name));

    [Theory]
    [InlineData("Season 1")]
    [InlineData("complete")]
    public void An_ordinary_directory_is_walked(string name) =>
        Assert.True(VideoFiles.IsWorthWalking(name));

    /// <summary>
    /// These exist on a NAS share and hold no downloads. @eaDir in particular
    /// holds a copy of everything around it, which would arrive in the review
    /// queue as a mystery nobody can explain.
    /// </summary>
    [Theory]
    [InlineData("@eaDir")]
    [InlineData("#recycle")]
    [InlineData("$RECYCLE.BIN")]
    [InlineData("lost+found")]
    [InlineData(".Trash-1000")]
    [InlineData(".stfolder")]
    public void A_directory_that_never_holds_a_download_is_skipped(string name) =>
        Assert.False(VideoFiles.IsWorthWalking(name));

    [Fact]
    public void A_file_that_has_not_changed_for_the_quiet_period_has_settled()
    {
        var seen = DateTimeOffset.UnixEpoch;

        Assert.True(Settling.HasSettled(1_000, seen, seen + Settling.QuietPeriod));
    }

    /// <summary>
    /// The rule VISION.md asks for: the tool waits another cycle rather than act
    /// on a file that is still growing.
    /// </summary>
    [Fact]
    public void A_file_seen_only_just_now_has_not()
    {
        var seen = DateTimeOffset.UnixEpoch;

        Assert.False(Settling.HasSettled(1_000, seen, seen));
        Assert.False(Settling.HasSettled(1_000, seen, seen + Settling.QuietPeriod - TimeSpan.FromSeconds(1)));
    }

    /// <summary>
    /// A download client that creates the final name before it has anything to
    /// put in it leaves an empty file behind for a moment. An empty file is not
    /// a video however long it sits there.
    /// </summary>
    [Fact]
    public void An_empty_file_never_settles() =>
        Assert.False(Settling.HasSettled(0, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddYears(1)));
}

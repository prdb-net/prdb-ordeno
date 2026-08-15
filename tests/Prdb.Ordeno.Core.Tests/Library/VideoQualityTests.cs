using Prdb.Ordeno.Core.Library;

using Xunit;

namespace Prdb.Ordeno.Core.Tests.Library;

/// <summary>
/// The name a size is known by. It decides two things that cannot be taken back
/// — whether an arriving video is a second quality or a second copy, and what
/// goes in the brackets — so what matters here is that one release is always
/// called the same thing and that two different ones are never called the same.
/// </summary>
public sealed class VideoQualityTests
{
    [Theory]
    [InlineData(3840, 2160, "2160p")]
    [InlineData(4096, 2160, "2160p")]
    [InlineData(2560, 1440, "1440p")]
    [InlineData(1920, 1080, "1080p")]
    [InlineData(1280, 720, "720p")]
    [InlineData(1024, 576, "576p")]
    [InlineData(720, 576, "576p")]
    [InlineData(720, 480, "480p")]
    [InlineData(640, 360, "360p")]
    [InlineData(426, 240, "240p")]
    public void A_standard_size_is_called_what_it_is(int width, int height, string label) =>
        Assert.Equal(label, new VideoQuality(width, height).Label);

    /// <summary>
    /// Letterboxing taken out of the file is the common shape of a scope
    /// release. Naming it after its height would call a 4K encode
    /// <c>1600p</c> — a name nothing else ever produces, so the same release in
    /// its full frame would arrive as a second quality of itself.
    /// </summary>
    [Theory]
    [InlineData(3840, 1600, "2160p")]
    [InlineData(3840, 2072, "2160p")]
    [InlineData(1920, 800, "1080p")]
    [InlineData(1920, 1040, "1080p")]
    [InlineData(1280, 534, "720p")]
    public void A_wide_picture_is_named_by_its_width(int width, int height, string label) =>
        Assert.Equal(label, new VideoQuality(width, height).Label);

    /// <summary>
    /// The two sizes nothing separates by much. Halfway thresholds put each on
    /// its own side; the tempting "eighty per cent of the standard" rule makes
    /// 1024×576 a 720p file.
    /// </summary>
    [Fact]
    public void A_576p_web_rip_is_not_a_720p_one()
    {
        Assert.Equal("576p", new VideoQuality(1024, 576).Label);
        Assert.Equal("720p", new VideoQuality(1280, 720).Label);
    }

    /// <summary>
    /// ADR 0020 compares the label rather than the dimensions. Two encodes of
    /// one scene that differ by two pixels are one quality — and have to be, or
    /// both would be filed and both would want the same name.
    /// </summary>
    [Fact]
    public void Two_encodes_of_the_same_release_are_one_quality()
    {
        Assert.Equal(new VideoQuality(1920, 1080).Label, new VideoQuality(1918, 1080).Label);
        Assert.Equal(new VideoQuality(1920, 1080).Label, new VideoQuality(1920, 1076).Label);
    }

    /// <summary>
    /// Smaller than anything released. Rounding it up to the smallest name would
    /// claim two different things are one, which is the one mistake this may not
    /// make.
    /// </summary>
    [Fact]
    public void Something_smaller_than_any_release_is_named_after_itself() =>
        Assert.Equal("144p", new VideoQuality(256, 144).Label);

    /// <summary>
    /// A size of zero is not a small video, it is an answer that was not read.
    /// The reading carries that as a state, and constructing a quality out of it
    /// would turn a failure into a label.
    /// </summary>
    [Theory]
    [InlineData(0, 1080)]
    [InlineData(1920, 0)]
    [InlineData(-1920, -1080)]
    public void A_size_that_is_not_a_size_is_refused(int width, int height) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new VideoQuality(width, height));

    [Fact]
    public void A_reading_that_failed_carries_no_quality()
    {
        var reading = new VideoQualityReading(VideoQualityState.Unreadable);

        Assert.False(reading.WasRead);
        Assert.Null(reading.Quality);
        Assert.NotNull(reading.Message);
    }

    [Fact]
    public void A_reading_that_worked_says_nothing_to_the_user()
    {
        var reading = VideoQualityReading.Of(1920, 1080);

        Assert.True(reading.WasRead);
        Assert.Equal("1080p", reading.Quality?.Label);
        Assert.Null(reading.Message);
    }
}

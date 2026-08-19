using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Prdb.Ordeno.Core.Library;
using Prdb.Ordeno.Infrastructure.Library;

using Xunit;

namespace Prdb.Ordeno.Infrastructure.Tests.Library;

/// <summary>
/// The image on a real filesystem, fetched over a real HTTP stack from a server
/// that answers whatever a test tells it to.
/// </summary>
/// <remarks>
/// Two halves, and both matter. What arrives is checked because the tool did not
/// compose the URL and does not control what answers it; what is on disk
/// afterwards is checked because ADR 0027 promises exactly two things — a file
/// already at that name survives, and a failure leaves nothing behind at all.
/// </remarks>
public sealed class SceneArtworkTests : IDisposable
{
    private const string Url = "https://cdn.example/videos/scene.jpg";

    private readonly TempDirectory directory = new();
    private readonly FakeCdn cdn = new();
    private readonly ServiceProvider services;
    private readonly SceneArtwork artwork;

    public SceneArtworkTests()
    {
        var collection = new ServiceCollection();

        collection
            .AddHttpClient(SceneArtwork.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => cdn);

        services = collection.BuildServiceProvider();

        artwork = new SceneArtwork(
            services.GetRequiredService<IHttpClientFactory>(),
            NullLogger<SceneArtwork>.Instance);
    }

    private string Path => System.IO.Path.Combine(directory.Root, ScenePath.ArtworkFileName);

    public void Dispose()
    {
        services.Dispose();
        cdn.Dispose();
        directory.Dispose();
    }

    [Fact]
    public void A_directory_with_no_image_in_it_says_so() =>
        Assert.Equal(ArtworkState.Missing, artwork.StateOf(Path));

    [Fact]
    public async Task An_image_is_written_where_there_is_none()
    {
        var outcome = await artwork.DownloadAsync(Url, Path);

        Assert.Equal(ArtworkWriteState.Written, outcome.State);
        Assert.Null(outcome.Problem);
        Assert.Equal(FakeCdn.Jpeg(), await File.ReadAllBytesAsync(Path));
        Assert.Equal(Url, Assert.Single(cdn.Requests));
    }

    /// <summary>
    /// The decision ADR 0027 is named for. It holds whoever put the file there
    /// and whatever is in it — an image the tool downloaded last month and one a
    /// user made themselves are the same case, because neither is worth losing
    /// and neither is worth a second download.
    /// </summary>
    [Fact]
    public async Task A_file_that_is_already_there_is_never_written_over()
    {
        const string Mine = "the image I chose myself";

        await File.WriteAllTextAsync(Path, Mine);

        var outcome = await artwork.DownloadAsync(Url, Path);

        Assert.Equal(ArtworkWriteState.Kept, outcome.State);
        Assert.Equal(Mine, await File.ReadAllTextAsync(Path));
        Assert.Contains("deleting it", outcome.Problem);

        // And the download was never made: the file on disk is the answer, and
        // spending the bandwidth to find that out afterwards would be the waste
        // the rule exists to avoid.
        Assert.Empty(cdn.Requests);
    }

    /// <summary>
    /// The other half of that promise, and the affordance it buys: delete the
    /// file, and the next filing into that scene brings a fresh one. No setting
    /// to find, and nothing to recognise the old one by.
    /// </summary>
    [Fact]
    public async Task Deleting_the_file_is_how_a_fresh_one_is_asked_for()
    {
        await artwork.DownloadAsync(Url, Path);
        File.Delete(Path);

        Assert.Equal(ArtworkWriteState.Written, (await artwork.DownloadAsync(Url, Path)).State);
        Assert.True(File.Exists(Path));
    }

    /// <summary>
    /// A scene directory is not a place to put whatever answered. The tool asked
    /// for an image at a URL somebody else composed, and an error page with a
    /// 200 on it is the ordinary way that goes wrong.
    /// </summary>
    [Fact]
    public async Task Something_that_is_not_a_jpeg_is_not_written()
    {
        cdn.Image = "<html><body>Not found, sorry.</body></html>"u8.ToArray();
        cdn.ContentType = "text/html";

        var outcome = await artwork.DownloadAsync(Url, Path);

        Assert.Equal(ArtworkWriteState.Failed, outcome.State);
        Assert.Contains("not a JPEG", outcome.Problem);
        Assert.False(File.Exists(Path));
    }

    /// <summary>
    /// A download that stopped halfway looks like a JPEG from the front. It is
    /// refused from the back, because a half-written image is worse than none:
    /// it is a file at the name that stops the next run writing the good one.
    /// </summary>
    [Fact]
    public async Task An_image_that_did_not_arrive_whole_is_not_written()
    {
        cdn.Image = FakeCdn.TruncatedJpeg();

        var outcome = await artwork.DownloadAsync(Url, Path);

        Assert.Equal(ArtworkWriteState.Failed, outcome.State);
        Assert.False(File.Exists(Path));
    }

    /// <summary>
    /// More than the tool will write into somebody's library directory, whatever
    /// it is. The cap is not about pictures — it is about a response that is not
    /// what it claimed to be.
    /// </summary>
    [Fact]
    public async Task An_image_larger_than_the_cap_is_refused()
    {
        cdn.Image = FakeCdn.Jpeg(SceneArtwork.MaximumBytes);

        var outcome = await artwork.DownloadAsync(Url, Path);

        Assert.Equal(ArtworkWriteState.Failed, outcome.State);
        Assert.Contains("larger than", outcome.Problem);
        Assert.False(File.Exists(Path));
    }

    /// <summary>
    /// Whatever goes wrong, the directory is as it was. That is the whole of the
    /// dotted temporary name: nothing half-written is left where the next run
    /// would find it and take it for an image.
    /// </summary>
    [Theory]
    [InlineData("not-a-url")]
    [InlineData("file:///etc/passwd")]
    public async Task A_url_that_is_not_one_is_not_requested(string url)
    {
        var outcome = await artwork.DownloadAsync(url, Path);

        Assert.Equal(ArtworkWriteState.Failed, outcome.State);
        Assert.Empty(cdn.Requests);
        Assert.Empty(Directory.GetFileSystemEntries(directory.Root));
    }

    /// <summary>
    /// A CDN that is not answering is one filed video without an image, said in
    /// a sentence. It is not an exception for the filing run to catch, because
    /// the video has already moved by the time this runs.
    /// </summary>
    [Fact]
    public async Task A_cdn_that_cannot_be_reached_leaves_nothing_behind()
    {
        cdn.Down = true;

        var outcome = await artwork.DownloadAsync(Url, Path);

        Assert.Equal(ArtworkWriteState.Failed, outcome.State);
        Assert.NotNull(outcome.Problem);
        Assert.Empty(Directory.GetFileSystemEntries(directory.Root));
    }

    /// <summary>
    /// The same for a stop: <c>docker stop</c> arriving mid-download must not
    /// throw its way out of here and turn a video that is filed into a run that
    /// reports it as stopped.
    /// </summary>
    [Fact]
    public async Task A_stop_mid_download_is_a_sentence_rather_than_an_exception()
    {
        using var stopping = new CancellationTokenSource();
        await stopping.CancelAsync();

        var outcome = await artwork.DownloadAsync(Url, Path, stopping.Token);

        Assert.Equal(ArtworkWriteState.Failed, outcome.State);
        Assert.Empty(Directory.GetFileSystemEntries(directory.Root));
    }
}

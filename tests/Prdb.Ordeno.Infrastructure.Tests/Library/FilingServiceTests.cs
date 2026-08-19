using System.Xml.Linq;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Prdb.Ordeno.Core.Configuration;
using Prdb.Ordeno.Core.History;
using Prdb.Ordeno.Core.Identification;
using Prdb.Ordeno.Core.Library;
using Prdb.Ordeno.Core.Review;
using Prdb.Ordeno.Core.Scanning;
using Prdb.Ordeno.Infrastructure.Identification;
using Prdb.Ordeno.Infrastructure.Library;
using Prdb.Ordeno.Infrastructure.Persistence;
using Prdb.Ordeno.Infrastructure.Scanning;
using Prdb.Ordeno.Infrastructure.Tests.Identification;
using Prdb.Ordeno.Infrastructure.Tests.Review;
using Prdb.Ordeno.Infrastructure.Tests.Scanning;

using Xunit;

namespace Prdb.Ordeno.Infrastructure.Tests.Library;

/// <summary>
/// Issue #17 end to end, against a real filesystem, a real SQLite file, real
/// videos and the real ffprobe: a download is walked, asked about, planned and
/// moved, and the download directory ends up emptier by exactly one file.
/// </summary>
/// <remarks>
/// Only prdb's endpoint and the exact hash are replaced, because they are the
/// two things a test has to be able to decide. Everything between them — the
/// scan, the settling rule, the planner, the quality reading and the move — is
/// what a container would run.
/// </remarks>
public sealed class FilingServiceTests : IAsyncLifetime
{
    private const string Site = "Example Studio";
    private const string Title = "Scene Title";

    /// <summary>Where prdb says the scene's first image is — an absolute URL, ready to request.</summary>
    private const string ImageUrl = "https://cdn.example/videos/scene.jpg";

    /// <summary>What the layout gives the scene the fake prdb answers with.</summary>
    private const string SceneDirectory = "Example Studio/Example Studio - 2024-05-01 - Scene Title";

    private readonly TempDirectory directory = new();
    private readonly TestTime time = new();
    private readonly FakePrdbIdentification prdb = new();
    private readonly FakeVideoLookup videos = new();
    private readonly TestFileHashes hashes = new();
    private readonly StoppingQualities stopping = new();
    private readonly FakeCdn cdn = new();

    private ServiceProvider services = null!;
    private string downloads = null!;
    private string library = null!;

    public async Task InitializeAsync()
    {
        downloads = Directory.CreateDirectory(directory.Combine("downloads")).FullName;
        library = Directory.CreateDirectory(directory.Combine("library")).FullName;

        var collection = new ServiceCollection();
        collection.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        collection.AddSingleton<TimeProvider>(time);
        collection.AddSingleton<IVideoIdentification>(prdb);

        // The other question the tool asks prdb: not what a file is, but what
        // the video it was recognised as says — which is what goes in the
        // sidecar, fetched at the moment it is written rather than read off the
        // identification row.
        collection.AddSingleton<IVideoLookup>(videos);
        collection.AddSingleton<IFileHashes>(hashes);

        // Registered before the slice, whose registrations are TryAdd. It is the
        // real ffprobe until a test asks it to stop the run, which is how a
        // shutdown is put in a known place rather than raced for.
        collection.AddSingleton<IVideoQualities>(stopping);

        collection.AddOrdenoPersistence(directory.Combine("data"));
        collection.AddOrdenoScanning();
        collection.AddOrdenoIdentification();
        collection.AddOrdenoLibrary();

        // The socket the image would have come down, and nothing above it: the
        // size cap, the JPEG check and the write are the ones under test.
        collection
            .AddHttpClient(SceneArtwork.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => cdn);

        services = collection.BuildServiceProvider();

        await services.PrepareOrdenoDatabaseAsync();
        await ConfigureAsync();
    }

    public async Task DisposeAsync()
    {
        await services.DisposeAsync();
        stopping.Dispose();
        cdn.Dispose();
        directory.Dispose();
    }

    /// <summary>
    /// The line the whole issue is about: the video is where the layout says,
    /// and the download directory is emptier by exactly one file.
    /// </summary>
    [Fact]
    public async Task A_recognised_video_ends_up_where_the_layout_says()
    {
        Recognised();
        var source = await ArrivedAsync("Example.Studio.24.05.01.Scene.Title.1080p.mkv", 1920, 1080);
        await ReadyAsync();

        var report = await FileAsync();

        var result = Assert.Single(report.Results);
        Assert.Equal(FilingResultState.Filed, result.State);

        Assert.Equal(
            Path.Combine(library, SceneDirectory, "Example Studio - 2024-05-01 - Scene Title.mkv"),
            result.Plan.TargetPath);
        Assert.True(File.Exists(result.Plan.TargetPath));
        Assert.False(File.Exists(source));
        Assert.Empty(Directory.GetFiles(downloads));
    }

    /// <summary>
    /// ADR 0022, and the reason the plan is a type rather than a log line: what
    /// the user is shown is what runs. Not a description of it — the same
    /// computation, made again.
    /// </summary>
    [Fact]
    public async Task The_preview_is_what_the_run_carries_out()
    {
        Recognised();
        await ArrivedAsync("Example.Studio.24.05.01.Scene.Title.1080p.mkv", 1920, 1080);
        await ReadyAsync();

        var preview = await PlanAsync();
        var report = await FileAsync();

        var planned = Assert.Single(preview.Plans);
        var carried = Assert.Single(report.Results);

        Assert.Equal(planned, carried.Plan);
        Assert.True(File.Exists(planned.TargetPath));
    }

    /// <summary>
    /// A preview moves nothing. It is the half of ADR 0022 that would be easy to
    /// get wrong once the two share a code path.
    /// </summary>
    [Fact]
    public async Task Working_out_what_would_happen_moves_nothing()
    {
        Recognised();
        var source = await ArrivedAsync("scene.1080p.mkv", 1280, 720);
        await ReadyAsync();

        var preview = await PlanAsync();

        Assert.Single(preview.Plans);
        Assert.True(File.Exists(source));
        Assert.Empty(Directory.GetDirectories(library));
    }

    /// <summary>
    /// ADR 0003: the same scene at the same quality is not filed, not deleted,
    /// and reported. This is the download directory that holds one scene twice,
    /// which is the case the tool was pointed at.
    /// </summary>
    [Fact]
    public async Task A_second_copy_at_the_same_quality_is_left_where_it_is()
    {
        Recognised();
        await ArrivedAsync("first.1080p.mkv", 1920, 1080);
        await ReadyAsync();
        await FileAsync();

        var second = await ArrivedAsync("second.1080p.mkv", 1920, 1080);
        await ReadyAsync();

        var result = Assert.Single((await FileAsync()).Results);

        Assert.Equal(FilingResultState.Skipped, result.State);
        Assert.Equal(FilingOutcome.AlreadyFiled, result.Plan.Outcome);
        Assert.True(File.Exists(second));
        Assert.Contains("not deleted", result.Message);

        // One directory, one video: the library did not gain a second entry.
        Assert.Single(VideosIn(Path.Combine(library, SceneDirectory)));
    }

    /// <summary>
    /// ADR 0003 and ADR 0020 together: both qualities are kept, in one
    /// directory, and the one that was filed first is renamed to carry its own
    /// label so the media server lists both by their quality.
    /// </summary>
    [Fact]
    public async Task A_second_quality_joins_the_first_and_both_end_up_labelled()
    {
        Recognised();
        await ArrivedAsync("first.1080p.mkv", 1920, 1080);
        await ReadyAsync();
        await FileAsync();

        await ArrivedAsync("second.2160p.mkv", 3840, 2160);
        await ReadyAsync();

        var result = Assert.Single((await FileAsync()).Results);

        Assert.Equal(FilingResultState.Filed, result.State);
        Assert.Equal(FilingOutcome.SecondQuality, result.Plan.Outcome);

        var scene = Path.Combine(library, SceneDirectory);

        Assert.Equal(
            [
                "Example Studio - 2024-05-01 - Scene Title - [1080p].mkv",
                "Example Studio - 2024-05-01 - Scene Title - [2160p].mkv",
            ],
            VideosIn(scene).Select(Path.GetFileName));

        // And the record was rewritten with it, or the next filing would look
        // for a file that is no longer called that.
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdenoDbContext>();

        Assert.Equal(
            ["Example Studio - 2024-05-01 - Scene Title - [1080p].mkv",
                "Example Studio - 2024-05-01 - Scene Title - [2160p].mkv"],
            await context.FiledVideos.OrderBy(row => row.FileName).Select(row => row.FileName).ToListAsync());
    }

    /// <summary>
    /// Section 6 is what this protects: every video in a scene directory has to
    /// begin with the directory's own name, or the library shows two entries
    /// with identical names instead of one with two versions.
    /// </summary>
    /// <remarks>
    /// The sidecar is the one file in there that does not, and section 4 is why:
    /// a Movies library reads <c>movie.nfo</c> and the per-file form loses to it
    /// — and being a per-file name is exactly what would drag it into the
    /// version grouping this test is about.
    /// </remarks>
    [Fact]
    public async Task Every_video_in_a_scene_directory_begins_with_its_name()
    {
        Recognised();
        await ArrivedAsync("first.720p.mkv", 1280, 720);
        await ReadyAsync();
        await FileAsync();

        await ArrivedAsync("second.2160p.mkv", 3840, 2160);
        await ReadyAsync();
        await FileAsync();

        var scene = Path.Combine(library, SceneDirectory);

        Assert.All(
            VideosIn(scene),
            file => Assert.StartsWith(
                Path.GetFileName(scene),
                Path.GetFileName(file),
                StringComparison.Ordinal));

        Assert.True(File.Exists(Path.Combine(scene, "movie.nfo")));
    }

    /// <summary>
    /// #18, and the point of the whole tool: a tidy filename is not what the
    /// user came for. What the media server shows comes out of the sidecar.
    /// </summary>
    [Fact]
    public async Task A_filed_video_gets_a_sidecar_carrying_what_prdb_knows()
    {
        Recognised(performers: ["Someone Real", "Somebody Else"]);
        await ArrivedAsync("scene.1080p.mkv", 1920, 1080);
        await ReadyAsync();

        var result = Assert.Single((await FileAsync()).Results);

        Assert.True(result.Filed);
        Assert.Null(result.Sidecar);

        var movie = Sidecar(Path.Combine(library, SceneDirectory));

        Assert.Equal(Title, movie.Element("title")?.Value);
        Assert.Equal("2024-05-01", movie.Element("premiered")?.Value);
        Assert.Equal(Site, movie.Element("studio")?.Value);

        Assert.Equal(
            ["Someone Real", "Somebody Else"],
            movie.Elements("actor").Select(actor => actor.Element("name")?.Value));
    }

    /// <summary>
    /// The rule in <c>AGENTS.md</c> about what the stored answer is for: it is
    /// read to put a name on a screen, and what a sidecar says is asked for
    /// again when the sidecar is written. A title prdb has corrected since is
    /// most of what somebody came here for.
    /// </summary>
    /// <remarks>
    /// The directory keeps the name the identification produced, and that is
    /// correct rather than a compromise: a path is what the library holds, and
    /// renaming somebody's directories because a title was edited is a different
    /// decision from writing what is true into the file that carries the
    /// metadata.
    /// </remarks>
    [Fact]
    public async Task The_sidecar_says_what_prdb_says_now_rather_than_what_it_said_then()
    {
        var videoId = Recognised();
        await ArrivedAsync("scene.1080p.mkv", 1920, 1080);
        await ReadyAsync();

        // prdb has corrected the title since the file was identified.
        videos.Knows(new VideoSummary(
            videoId,
            "The Title As prdb Has It Now",
            new DateOnly(2024, 5, 1),
            Guid.NewGuid(),
            Site,
            []));

        await FileAsync();

        var scene = Path.Combine(library, SceneDirectory);

        Assert.Equal("The Title As prdb Has It Now", Sidecar(scene).Element("title")?.Value);
        Assert.Single(VideosIn(scene));
    }

    /// <summary>
    /// Nothing is written on the strength of a failed lookup. The video is filed
    /// — that decision was made from what the tool already knew — and the row
    /// says why there is nothing next to it.
    /// </summary>
    [Fact]
    public async Task prdb_being_unreachable_files_the_video_and_writes_no_sidecar()
    {
        Recognised();
        await ArrivedAsync("scene.1080p.mkv", 1920, 1080);
        await ReadyAsync();

        videos.Stopped = "prdb could not be reached, so nothing could be asked about.";

        var result = Assert.Single((await FileAsync()).Results);

        Assert.True(result.Filed);
        Assert.False(File.Exists(Path.Combine(library, SceneDirectory, "movie.nfo")));
        Assert.Contains("prdb could not be reached", result.Sidecar);
    }

    /// <summary>
    /// A video prdb has merged away since it was identified. The lookup leaves
    /// an id it does not know out of the answer rather than failing it, so this
    /// is one file with no sidecar rather than a run that stopped.
    /// </summary>
    [Fact]
    public async Task A_video_prdb_no_longer_knows_is_filed_without_a_sidecar()
    {
        // Only the endpoint that names the file knows this video, deliberately.
        prdb.Recognises(Guid.NewGuid(), Title, Site);
        await ArrivedAsync("scene.1080p.mkv", 1920, 1080);
        await ReadyAsync();

        var result = Assert.Single((await FileAsync()).Results);

        Assert.True(result.Filed);
        Assert.False(File.Exists(Path.Combine(library, SceneDirectory, "movie.nfo")));
        Assert.Contains("no longer knows", result.Sidecar);
    }

    /// <summary>
    /// A <c>movie.nfo</c> somebody wrote by hand is not the tool's to overwrite,
    /// and it is at the very name the tool wants — there is no stepping around
    /// it. The video still goes in next to it.
    /// </summary>
    [Fact]
    public async Task A_sidecar_somebody_else_wrote_is_left_exactly_as_it_is()
    {
        Recognised();
        await ArrivedAsync("first.1080p.mkv", 1920, 1080);
        await ReadyAsync();
        await FileAsync();

        var scene = Path.Combine(library, SceneDirectory);
        var sidecar = Path.Combine(scene, "movie.nfo");
        const string ByHand = "<movie><title>The Name I Gave It</title></movie>";

        await File.WriteAllTextAsync(sidecar, ByHand);

        await ArrivedAsync("second.2160p.mkv", 3840, 2160);
        await ReadyAsync();

        var result = Assert.Single((await FileAsync()).Results);

        Assert.True(result.Filed);
        Assert.Equal(ByHand, await File.ReadAllTextAsync(sidecar));
        Assert.Contains("did not write", result.Plan.Sidecar.Message);
        Assert.Equal(2, VideosIn(scene).Count);

        // And nothing was left lying about next to it while finding that out.
        Assert.Equal(3, Directory.GetFileSystemEntries(scene).Length);
    }

    /// <summary>
    /// Its own, on the other hand, is replaced — and replaced by a write and a
    /// rename, so that an interruption leaves the old document or the new one
    /// and never half of either.
    /// </summary>
    [Fact]
    public async Task A_sidecar_the_tool_wrote_is_written_again_when_a_second_quality_arrives()
    {
        var videoId = Recognised();
        await ArrivedAsync("first.1080p.mkv", 1920, 1080);
        await ReadyAsync();
        await FileAsync();

        videos.Knows(new VideoSummary(
            videoId,
            "The Title As prdb Has It Now",
            new DateOnly(2024, 5, 1),
            Guid.NewGuid(),
            Site,
            []));

        await ArrivedAsync("second.2160p.mkv", 3840, 2160);
        await ReadyAsync();

        var result = Assert.Single((await FileAsync()).Results);

        Assert.Equal(SidecarAction.Replace, result.Plan.Sidecar.Action);
        Assert.Null(result.Sidecar);

        var scene = Path.Combine(library, SceneDirectory);

        Assert.Equal("The Title As prdb Has It Now", Sidecar(scene).Element("title")?.Value);
        Assert.Equal(3, Directory.GetFileSystemEntries(scene).Length);
    }

    /// <summary>
    /// The sidecar is a write, so ADR 0022 covers it: the plan says it would be
    /// written, and working out a plan writes nothing.
    /// </summary>
    /// <remarks>
    /// It also asks prdb nothing. A preview over a first pass at somebody's
    /// library is thousands of files, and spending a rate-limited quota to
    /// produce a sentence the user may not act on is the request pattern
    /// ADR 0001 exists to avoid.
    /// </remarks>
    [Fact]
    public async Task Working_out_what_would_happen_writes_no_sidecar_and_asks_prdb_nothing()
    {
        Recognised();
        await ArrivedAsync("scene.1080p.mkv", 1920, 1080);
        await ReadyAsync();

        var plan = Assert.Single((await PlanAsync()).Plans);

        Assert.Equal(SidecarAction.Write, plan.Sidecar.Action);
        Assert.Equal(Path.Combine(library, SceneDirectory, "movie.nfo"), plan.Sidecar.Path);
        Assert.Empty(Directory.GetDirectories(library));
        Assert.Empty(videos.Described);
    }

    /// <summary>
    /// #28, and the shape ADR 0027 cut it down to: one image, called
    /// <c>fanart.jpg</c>, and no poster — section 5 measured a landscape image
    /// in the Primary slot to be worse than none at all.
    /// </summary>
    [Fact]
    public async Task A_filed_video_gets_the_one_image_prdb_has_for_the_scene()
    {
        await ArtworkOnAsync();
        Recognised();
        await ArrivedAsync("scene.1080p.mkv", 1920, 1080);
        await ReadyAsync();

        var result = Assert.Single((await FileAsync()).Results);

        Assert.True(result.Filed);
        Assert.Null(result.Artwork);

        var scene = Path.Combine(library, SceneDirectory);

        Assert.Equal(FakeCdn.Jpeg(), await File.ReadAllBytesAsync(Path.Combine(scene, "fanart.jpg")));
        Assert.False(File.Exists(Path.Combine(scene, "poster.jpg")));

        // The image came from the CDN and cost prdb nothing: the URL was in the
        // answer the sidecar was written from.
        Assert.Equal(ImageUrl, Assert.Single(cdn.Requests));
        Assert.Single(videos.Described);
    }

    /// <summary>
    /// The default, and the hard rule applied to bandwidth rather than to data:
    /// spending somebody's connection and disk is not something that happens
    /// because nobody said no.
    /// </summary>
    [Fact]
    public async Task Nothing_is_downloaded_unless_somebody_turned_artwork_on()
    {
        Recognised();
        await ArrivedAsync("scene.1080p.mkv", 1920, 1080);
        await ReadyAsync();

        var result = Assert.Single((await FileAsync()).Results);

        Assert.True(result.Filed);
        Assert.Null(result.Artwork);
        Assert.Empty(cdn.Requests);
        Assert.False(File.Exists(Path.Combine(library, SceneDirectory, "fanart.jpg")));
    }

    /// <summary>
    /// The decision ADR 0027 is named for, end to end: an image somebody put
    /// there survives everything the tool does, and it needs no marker to do it.
    /// </summary>
    [Fact]
    public async Task An_image_the_user_put_there_is_left_exactly_as_it_is()
    {
        await ArtworkOnAsync();
        Recognised();
        await ArrivedAsync("first.1080p.mkv", 1920, 1080);
        await ReadyAsync();
        await FileAsync();

        var scene = Path.Combine(library, SceneDirectory);
        var image = Path.Combine(scene, "fanart.jpg");
        const string Mine = "the image I chose myself";

        await File.WriteAllTextAsync(image, Mine);
        cdn.Requests.Clear();

        await ArrivedAsync("second.2160p.mkv", 3840, 2160);
        await ReadyAsync();

        var result = Assert.Single((await FileAsync()).Results);

        Assert.True(result.Filed);
        Assert.Equal(ArtworkAction.Keep, result.Plan.Artwork.Action);
        Assert.Equal(Mine, await File.ReadAllTextAsync(image));
        Assert.Contains("deleting it", result.Plan.Artwork.Message);

        // Not downloaded and then discarded — not downloaded at all.
        Assert.Empty(cdn.Requests);
    }

    /// <summary>
    /// A scene prdb has no image for. The array is empty and the field is
    /// nullable, and this is the ordinary case for a scene nobody photographed:
    /// nothing is written, and nothing is said about it either. A warning here
    /// would turn the ordinary into a problem.
    /// </summary>
    [Fact]
    public async Task A_scene_prdb_has_no_image_for_is_filed_without_one_and_without_a_word()
    {
        await ArtworkOnAsync();
        Recognised(image: null);
        await ArrivedAsync("scene.1080p.mkv", 1920, 1080);
        await ReadyAsync();

        var result = Assert.Single((await FileAsync()).Results);

        Assert.True(result.Filed);
        Assert.Null(result.Artwork);
        Assert.Empty(cdn.Requests);
        Assert.False(File.Exists(Path.Combine(library, SceneDirectory, "fanart.jpg")));

        // And the rest of the filing happened exactly as it would have.
        Assert.True(File.Exists(Path.Combine(library, SceneDirectory, "movie.nfo")));
    }

    /// <summary>
    /// A download that fails never fails a filing. The video is where it belongs
    /// and the row says what did not arrive next to it — which is the same rule
    /// the sidecar has, one step further from mattering.
    /// </summary>
    [Fact]
    public async Task A_download_that_fails_still_leaves_the_video_filed()
    {
        await ArtworkOnAsync();
        Recognised();
        await ArrivedAsync("scene.1080p.mkv", 1920, 1080);
        await ReadyAsync();

        cdn.Image = null;

        var result = Assert.Single((await FileAsync()).Results);

        Assert.True(result.Filed);
        Assert.NotNull(result.Artwork);

        var scene = Path.Combine(library, SceneDirectory);

        Assert.True(File.Exists(result.Plan.TargetPath));
        Assert.True(File.Exists(Path.Combine(scene, "movie.nfo")));

        // And nothing was left lying about while finding that out: no image, and
        // no half-written file under a dotted name either.
        Assert.Equal(2, Directory.GetFileSystemEntries(scene).Length);
    }

    /// <summary>
    /// ADR 0022 covers the download as it covers every other write: the plan says
    /// an image would be fetched, and working out a plan fetches nothing.
    /// </summary>
    [Fact]
    public async Task Working_out_what_would_happen_downloads_nothing()
    {
        await ArtworkOnAsync();
        Recognised();
        await ArrivedAsync("scene.1080p.mkv", 1920, 1080);
        await ReadyAsync();

        var plan = Assert.Single((await PlanAsync()).Plans);

        Assert.Equal(ArtworkAction.Write, plan.Artwork.Action);
        Assert.Equal(Path.Combine(library, SceneDirectory, "fanart.jpg"), plan.Artwork.Path);
        Assert.Empty(cdn.Requests);
        Assert.Empty(Directory.GetDirectories(library));
    }

    /// <summary>
    /// ADR 0020: without a quality neither the skip nor the label can be
    /// decided. The file stays where it is and the message says why.
    /// </summary>
    [Fact]
    public async Task A_file_whose_quality_cannot_be_read_is_not_filed()
    {
        Recognised();

        var path = Path.Combine(downloads, "truncated.mkv");
        await File.WriteAllBytesAsync(path, new byte[64 * 1024]);
        await ReadyAsync();

        var result = Assert.Single((await FileAsync()).Results);

        Assert.Equal(FilingResultState.Skipped, result.State);
        Assert.Equal(FilingOutcome.Blocked, result.Plan.Outcome);
        Assert.True(File.Exists(path));
        Assert.Empty(Directory.GetDirectories(library));
    }

    /// <summary>
    /// ADR 0019: a file prdb cannot name is not filed at all, so it is not even
    /// a candidate. It waits for the review queue rather than appearing here as
    /// something that failed.
    /// </summary>
    [Fact]
    public async Task A_file_prdb_could_not_name_is_not_a_candidate()
    {
        await ArrivedAsync("mystery.mkv", 1280, 720);
        await ReadyAsync();

        Assert.Empty((await PlanAsync()).Plans);
    }

    /// <summary>
    /// ADR 0023: a video a person named is filed exactly as one prdb named. The
    /// queue answers what a file is; this is still the run that moves it.
    /// </summary>
    [Fact]
    public async Task A_video_a_person_named_is_filed_like_any_other()
    {
        var source = await ArrivedAsync("mystery.1080p.mkv", 1920, 1080);
        await ReadyAsync();

        await DecidedAsync("mystery.1080p.mkv", ResolutionKind.Assigned, Guid.NewGuid());

        var result = Assert.Single((await FileAsync()).Results);

        Assert.Equal(FilingResultState.Filed, result.State);
        Assert.Equal(
            Path.Combine(library, SceneDirectory, "Example Studio - 2024-05-01 - Scene Title.mkv"),
            result.Plan.TargetPath);
        Assert.False(File.Exists(source));
    }

    /// <summary>
    /// A person's answer outranks prdb's wherever both exist. prdb naming
    /// something else afterwards is not a reason to undo somebody's decision.
    /// </summary>
    [Fact]
    public async Task A_persons_answer_is_the_one_that_is_filed()
    {
        Recognised("What prdb Called It", "Some Other Site");
        await ArrivedAsync("disputed.1080p.mkv", 1920, 1080);
        await ReadyAsync();

        await DecidedAsync("disputed.1080p.mkv", ResolutionKind.Assigned, Guid.NewGuid());

        var result = Assert.Single((await FileAsync()).Results);

        Assert.Equal(FilingResultState.Filed, result.State);
        Assert.Equal(Path.Combine(library, SceneDirectory), result.Plan.Directory);
    }

    /// <summary>
    /// Saying no is an answer, and the answer is "leave it alone". A dismissed
    /// file is not a candidate, not a failure, and still on disk.
    /// </summary>
    [Fact]
    public async Task A_file_somebody_dismissed_is_not_filed()
    {
        Recognised();
        var source = await ArrivedAsync("sample.1080p.mkv", 1920, 1080);
        await ReadyAsync();

        await DecidedAsync("sample.1080p.mkv", ResolutionKind.Dismissed, videoId: null);

        Assert.Empty((await PlanAsync()).Plans);
        Assert.Empty((await FileAsync()).Results);
        Assert.True(File.Exists(source));
        Assert.Empty(Directory.GetDirectories(library));
    }

    /// <summary>
    /// One video, known to both halves of prdb that a test replaces: the
    /// endpoint that names a file, and the lookup a sidecar is written from.
    /// </summary>
    /// <remarks>
    /// Both, always. A test where only the first knows the video is a test where
    /// the file is filed and nothing is written next to it, which is a real
    /// state — <see cref="A_video_prdb_no_longer_knows_is_filed_without_a_sidecar"/>
    /// is where it is arranged deliberately rather than by forgetting.
    /// </remarks>
    private Guid Recognised(
        string title = Title,
        string site = Site,
        Guid? videoId = null,
        string? image = ImageUrl,
        params string[] performers)
    {
        var id = videoId ?? Guid.NewGuid();

        prdb.Recognises(id, title, site);

        videos.Knows(new VideoSummary(
            id,
            title,
            new DateOnly(2024, 5, 1),
            Guid.NewGuid(),
            site,
            performers,
            image));

        return id;
    }

    /// <summary>
    /// Somebody turned artwork on — ADR 0027. Written straight to the
    /// configuration, because what is under test is what filing makes of it.
    /// </summary>
    private async Task ArtworkOnAsync()
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdenoDbContext>();

        (await context.Configuration.SingleAsync()).DownloadArtwork = true;

        await context.SaveChangesAsync();
    }

    /// <summary>
    /// What the queue writes when somebody answers, written straight to the
    /// database: what is under test here is what filing makes of it.
    /// </summary>
    private async Task DecidedAsync(string relative, ResolutionKind kind, Guid? videoId)
    {
        if (videoId is { } named)
        {
            // The queue fetched this from prdb when the decision was recorded,
            // and filing fetches it again when it writes the sidecar.
            videos.Knows(new VideoSummary(
                named,
                Title,
                new DateOnly(2024, 5, 1),
                Guid.NewGuid(),
                Site,
                []));
        }

        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdenoDbContext>();

        var path = Path.Combine(downloads, relative);
        var file = await context.DiscoveredFiles.SingleAsync(row => row.Path == path);

        context.FileResolutions.Add(new FileResolution
        {
            DiscoveredFileId = file.Id,
            DecidedAt = time.GetUtcNow(),
            Kind = kind,
            From = kind is ResolutionKind.Assigned ? ResolvedFrom.Search : null,
            VideoId = videoId,
            Title = videoId is null ? null : Title,
            SiteTitle = videoId is null ? null : Site,
            ReleaseDate = videoId is null ? null : new DateOnly(2024, 5, 1),
        });

        await context.SaveChangesAsync();
    }

    /// <summary>
    /// The rule the scan established and filing inherits: a file that is still
    /// being written is not acted on, whatever prdb has already said about it.
    /// </summary>
    [Fact]
    public async Task A_file_that_has_not_settled_is_not_filed()
    {
        Recognised();
        await ArrivedAsync("still.arriving.mkv", 1280, 720);
        await ReadyAsync();

        // It grew again after prdb had answered about it.
        await ArrivedAsync("still.arriving.mkv", 1920, 1080);
        await ScanAsync();

        Assert.Empty((await PlanAsync()).Plans);
    }

    /// <summary>
    /// Nothing is filed until the setup is finished — ADR 0009 — and the answer
    /// says so rather than being an empty list somebody has to interpret.
    /// </summary>
    [Fact]
    public async Task Nothing_is_filed_before_the_setup_is_finished()
    {
        Recognised();
        await ArrivedAsync("scene.mkv", 1280, 720);
        await ReadyAsync();

        await using (var scope = services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<OrdenoDbContext>();
            var configuration = await context.Configuration.SingleAsync();
            configuration.OnboardingCompletedAt = null;

            await context.SaveChangesAsync();
        }

        var preview = await PlanAsync();

        Assert.Empty(preview.Plans);
        Assert.NotNull(preview.Problem);
    }

    /// <summary>
    /// A library that has gone — an unmounted share, most often — stops
    /// everything and says so. Filing into the empty mount point underneath it
    /// would write somebody's library onto the container's own disk.
    /// </summary>
    [Fact]
    public async Task A_library_that_cannot_be_written_to_stops_the_run()
    {
        Recognised();
        var source = await ArrivedAsync("scene.mkv", 1280, 720);
        await ReadyAsync();

        Directory.Delete(library, recursive: true);

        var report = await FileAsync();

        Assert.Empty(report.Results);
        Assert.NotNull(report.Problem);
        Assert.True(File.Exists(source));
    }

    /// <summary>
    /// The tool's memory of the download goes with the download. Leaving the row
    /// behind would have the next run try to move a file that is not there and
    /// report it as missing, minutes after filing it.
    /// </summary>
    [Fact]
    public async Task A_video_that_has_been_filed_is_no_longer_a_download()
    {
        Recognised();
        await ArrivedAsync("scene.1080p.mkv", 1920, 1080);
        await ReadyAsync();
        await FileAsync();

        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdenoDbContext>();

        Assert.Empty(await context.DiscoveredFiles.ToListAsync());
        Assert.Empty(await context.FileIdentifications.ToListAsync());
        Assert.Empty((await PlanAsync()).Plans);
    }

    /// <summary>
    /// A record of a file the user has since deleted describes a library that no
    /// longer holds the scene, so the scene is filed again rather than reported
    /// as a duplicate of something that is not there.
    /// </summary>
    [Fact]
    public async Task A_scene_the_user_deleted_from_the_library_is_filed_again()
    {
        Recognised();
        await ArrivedAsync("first.1080p.mkv", 1920, 1080);
        await ReadyAsync();
        await FileAsync();

        Directory.Delete(Path.Combine(library, SceneDirectory), recursive: true);

        await ArrivedAsync("again.1080p.mkv", 1920, 1080);
        await ReadyAsync();

        var result = Assert.Single((await FileAsync()).Results);

        Assert.Equal(FilingResultState.Filed, result.State);
        Assert.Equal(FilingOutcome.Filed, result.Plan.Outcome);

        // And the record that had gone stale was replaced rather than joined.
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdenoDbContext>();

        Assert.Single(await context.FiledVideos.ToListAsync());
    }

    /// <summary>
    /// Two different scenes whose names the layout cannot tell apart. Filed into
    /// one directory they would become one entry with two versions, and one of
    /// them would stop existing — so the second is stepped around with prdb's
    /// scene id.
    /// </summary>
    [Fact]
    public async Task Two_scenes_the_layout_gives_one_name_do_not_share_a_directory()
    {
        var second = Guid.NewGuid();

        Recognised();
        await ArrivedAsync("first.1080p.mkv", 1920, 1080);
        await ReadyAsync();
        await FileAsync();

        Recognised(videoId: second);
        await ArrivedAsync("other.scene.1080p.mkv", 1920, 1080);
        await ReadyAsync();

        var result = Assert.Single((await FileAsync()).Results);

        Assert.Equal(FilingResultState.Filed, result.State);
        Assert.Equal(FilingOutcome.CollisionBroken, result.Plan.Outcome);
        Assert.Contains(second.ToString("d"), result.Plan.Directory);
        Assert.Equal(2, Directory.GetDirectories(Path.Combine(library, "Example Studio")).Length);
    }

    /// <summary>
    /// What <c>docker stop</c> has to mean. The entrypoint <c>exec</c>s so the
    /// signal reaches the application, and what arrives here is a cancelled
    /// token partway through a run: the file that had already moved is filed and
    /// written down, and the ones behind it are untouched and reported as not
    /// reached rather than silently dropped.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The stop is arranged from inside the run rather than by racing it with a
    /// timer, because a test that sometimes stops in the right place is a test
    /// that sometimes proves nothing. The quality reading is the seam: it is
    /// what every file goes through on its way to being planned.
    /// </para>
    /// <para>
    /// The other half — a stop that lands in the middle of a cross-filesystem
    /// copy — is in <see cref="LibraryMovesTests"/>. It cannot be arranged here:
    /// both directories are on one filesystem, so a filing is a rename, and a
    /// rename has no middle.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_run_stopped_partway_files_what_it_reached_and_leaves_the_rest()
    {
        Recognised();
        var first = await ArrivedAsync("first.1080p.mkv", 1920, 1080);
        var second = await ArrivedAsync("second.1080p.mkv", 1920, 1080);
        await ReadyAsync();

        // The stop arrives while the first file is being worked out, which is
        // where a real one would: mid-run, with a file already on its way.
        stopping.After(reads: 1);

        var report = await FileAsync(stopping.Token);

        Assert.Equal(2, report.Results.Count);
        Assert.NotNull(report.Problem);

        var filed = Assert.Single(report.Results, result => result.Filed);
        var stopped = Assert.Single(report.Results, result => result.State is FilingResultState.Stopped);

        Assert.True(File.Exists(filed.Plan.TargetPath));
        Assert.False(File.Exists(filed.Plan.SourcePath));
        Assert.True(File.Exists(stopped.Plan.SourcePath));

        // And the one that did move was written down, although the run was
        // stopping while that happened. A library holding a video no row knows
        // about would be filed around next time.
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdenoDbContext>();

        Assert.Single(await context.FiledVideos.ToListAsync());

        // Belt and braces: one of the two is still in the download directory.
        Assert.Single([first, second], File.Exists);
    }

    private async Task<FilingPreview> PlanAsync()
    {
        await using var scope = services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<FilingService>().PlanAsync();
    }

    private async Task<FilingReport> FileAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();

        return await scope.ServiceProvider
            .GetRequiredService<FilingService>()
            .FileAsync(AskedBy.Person, cancellationToken);
    }

    /// <summary>
    /// The videos in a scene directory, which is everything in it that filing
    /// did not write next to them.
    /// </summary>
    private static IReadOnlyList<string> VideosIn(string sceneDirectory) =>
    [
        .. Directory.GetFiles(sceneDirectory)
            .Where(file => Path.GetFileName(file) is not ("movie.nfo" or "fanart.jpg"))
            .Order(StringComparer.Ordinal),
    ];

    /// <summary>The sidecar in a scene directory, parsed — which is half of what is under test.</summary>
    private static XElement Sidecar(string sceneDirectory) =>
        XDocument.Load(Path.Combine(sceneDirectory, "movie.nfo")).Root!;

    /// <summary>A real video of the given size, in the download directory.</summary>
    private async Task<string> ArrivedAsync(string relative, int width, int height)
    {
        var path = Path.Combine(downloads, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        TestVideos.Write(path, width, height);

        await Task.CompletedTask;

        return path;
    }

    /// <summary>Two scans a quiet period apart, and then prdb is asked.</summary>
    private async Task ReadyAsync()
    {
        await ScanAsync();
        time.Advance(Settling.QuietPeriod);
        await ScanAsync();
        await IdentifyAsync();
    }

    private async Task ScanAsync()
    {
        await using var scope = services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<ScanService>().ScanAsync();
    }

    private async Task IdentifyAsync()
    {
        await using var scope = services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<IdentificationService>().IdentifyAsync();
    }

    /// <summary>
    /// The real reading of a picture size, with a way to cancel the run at a
    /// known point — after the given number of files have been measured.
    /// </summary>
    private sealed class StoppingQualities : IVideoQualities, IDisposable
    {
        private readonly VideoQualities real = new(NullLogger<VideoQualities>.Instance);
        private readonly CancellationTokenSource source = new();

        private int stopAfter = -1;
        private int read;

        public CancellationToken Token => source.Token;

        public void After(int reads) => stopAfter = reads;

        public async Task<VideoQualityReading> ReadAsync(
            string path,
            CancellationToken cancellationToken = default)
        {
            var reading = await real.ReadAsync(path, cancellationToken);

            if (stopAfter > 0 && ++read >= stopAfter)
            {
                await source.CancelAsync();
            }

            return reading;
        }

        public void Dispose() => source.Dispose();
    }

    private async Task ConfigureAsync()
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdenoDbContext>();

        var configuration = await context.Configuration.SingleAsync();
        configuration.PrdbApiKey = "not-a-real-key";
        configuration.TargetDirectory = library;
        configuration.Layout = LibraryLayouts.NameOf(LibraryLayout.Jellyfin);
        configuration.OnboardingCompletedAt = time.GetUtcNow();

        context.SourceDirectories.Add(new SourceDirectory { Path = downloads, AddedAt = time.GetUtcNow() });

        await context.SaveChangesAsync();
    }
}

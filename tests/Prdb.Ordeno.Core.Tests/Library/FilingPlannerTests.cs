using Prdb.Ordeno.Core.Configuration;
using Prdb.Ordeno.Core.Library;

using Xunit;

namespace Prdb.Ordeno.Core.Tests.Library;

/// <summary>
/// Every decision filing makes, made without touching anything. What is tested
/// here is what the user is shown before they press the button and what the run
/// then carries out — one answer, which is the whole point of ADR 0022.
/// </summary>
public sealed class FilingPlannerTests
{
    private const string Root = "/library";
    private const string Directory = "/library/Example Studio/Example Studio - 2025-11-03 - Scene Title";

    private static readonly Guid VideoId = Guid.Parse("0f3a1c2b-4d5e-6f70-8192-a3b4c5d6e7f8");

    private static readonly Scene Scene =
        new(VideoId, "Example Studio", "Scene Title", new DateOnly(2025, 11, 3));

    /// <summary>What is at each path, for the paths a test cares about; free everywhere else.</summary>
    private sealed class Directories : ISceneDirectories
    {
        private readonly Dictionary<string, SceneDirectoryState> answers = [];

        public List<string> Asked { get; } = [];

        public Directories With(string path, SceneDirectoryState state)
        {
            answers[path] = state;

            return this;
        }

        public SceneDirectoryState StateOf(string absolutePath)
        {
            Asked.Add(absolutePath);

            return answers.GetValueOrDefault(absolutePath, SceneDirectoryState.Free);
        }
    }

    /// <summary>Whose the sidecar at each path is; there is none anywhere else.</summary>
    private sealed class Sidecars : ISidecars
    {
        private readonly Dictionary<string, SidecarState> answers = [];

        public List<string> Asked { get; } = [];

        public Sidecars With(string path, SidecarState state)
        {
            answers[path] = state;

            return this;
        }

        public SidecarState StateOf(string absolutePath)
        {
            Asked.Add(absolutePath);

            return answers.GetValueOrDefault(absolutePath, SidecarState.Missing);
        }

        /// <summary>
        /// What the refresh asks. Filing never does — it needs whose the file is
        /// and not what it says — so this answers the state and no document.
        /// </summary>
        public SidecarLook Look(string absolutePath) => new(StateOf(absolutePath));
    }

    /// <summary>Whether there is an image at each path; there is none anywhere else.</summary>
    private sealed class Artwork : ISceneArtwork
    {
        private readonly Dictionary<string, ArtworkState> answers = [];

        public List<string> Asked { get; } = [];

        public Artwork With(string path, ArtworkState state)
        {
            answers[path] = state;

            return this;
        }

        public ArtworkState StateOf(string absolutePath)
        {
            Asked.Add(absolutePath);

            return answers.GetValueOrDefault(absolutePath, ArtworkState.Missing);
        }
    }

    private static FilingPlan Plan(
        Directories directories,
        VideoQualityReading? quality = null,
        Scene? scene = null,
        IReadOnlyList<FiledCopy>? filed = null,
        string sourceName = "some.release.1080p.mkv",
        FileMovement movement = FileMovement.Rename,
        Sidecars? sidecars = null,
        Artwork? artwork = null,
        bool wantsArtwork = false) =>
        new FilingPlanner(
            new TargetPaths(directories),
            directories,
            sidecars ?? new Sidecars(),
            artwork ?? new Artwork()).Plan(
            fileId: 7,
            sourcePath: "/downloads/" + sourceName,
            sourceName,
            Root,
            movement,
            scene ?? Scene,
            quality ?? VideoQualityReading.Of(1920, 1080),
            filed ?? [],
            wantsArtwork);

    [Fact]
    public void A_scene_the_library_does_not_hold_is_filed_where_the_layout_says()
    {
        var plan = Plan(new Directories());

        Assert.Equal(FilingOutcome.Filed, plan.Outcome);
        Assert.Equal(Directory, plan.Directory);
        Assert.Equal($"{Directory}/Example Studio - 2025-11-03 - Scene Title.mkv", plan.TargetPath);
        Assert.Equal("1080p", plan.QualityLabel);
        Assert.Null(plan.Relabel);
        Assert.Null(plan.Message);
        Assert.True(plan.Moves);
    }

    /// <summary>
    /// The first copy carries no label. There is only one of it, and ADR 0020
    /// puts one on it when that stops being true rather than in anticipation.
    /// </summary>
    [Fact]
    public void The_first_copy_of_a_scene_is_not_labelled() =>
        Assert.Equal("Example Studio - 2025-11-03 - Scene Title.mkv", Plan(new Directories()).TargetName);

    /// <summary>
    /// #20's answer, reached through the planner: a taken name is stepped around
    /// with prdb's scene id rather than written into, because two scenes in one
    /// directory become one entry and one of them stops existing.
    /// </summary>
    [Fact]
    public void A_name_taken_by_something_else_is_stepped_around()
    {
        var plan = Plan(new Directories().With(Directory, SceneDirectoryState.Occupied));

        Assert.Equal(FilingOutcome.CollisionBroken, plan.Outcome);
        Assert.Contains(VideoId.ToString("d"), plan.Directory);
        Assert.NotNull(plan.Message);
        Assert.True(plan.Moves);
    }

    [Fact]
    public void A_library_that_cannot_be_looked_at_stops_the_filing()
    {
        var plan = Plan(new Directories().With(Directory, SceneDirectoryState.Unknown));

        Assert.Equal(FilingOutcome.Blocked, plan.Outcome);
        Assert.Null(plan.TargetPath);
        Assert.False(plan.Moves);
    }

    /// <summary>
    /// ADR 0003, and the case the tool exists to handle: a download directory
    /// holding the same scene three times. Nothing is moved and nothing is
    /// deleted.
    /// </summary>
    [Fact]
    public void A_second_copy_at_the_same_quality_is_not_filed()
    {
        var filed = Held("Example Studio - 2025-11-03 - Scene Title.mkv", "1080p");

        var plan = Plan(new Directories().With(filed[0].Path, SceneDirectoryState.Occupied), filed: filed);

        Assert.Equal(FilingOutcome.AlreadyFiled, plan.Outcome);
        Assert.False(plan.Moves);
        Assert.Null(plan.Relabel);
        Assert.Contains("1080p", plan.Message);
        Assert.Contains("not deleted", plan.Message);
    }

    /// <summary>
    /// The same scene at a quality the library does not hold. It joins the copy
    /// that is there — one directory, or Jellyfin shows two entries instead of
    /// one with two versions — and that copy is relabelled first (ADR 0020).
    /// </summary>
    [Fact]
    public void A_second_quality_joins_the_copy_that_is_there_and_relabels_it()
    {
        var filed = Held("Example Studio - 2025-11-03 - Scene Title.mkv", "1080p");

        var plan = Plan(
            new Directories().With(filed[0].Path, SceneDirectoryState.Occupied),
            VideoQualityReading.Of(3840, 2160),
            filed: filed,
            sourceName: "some.release.2160p.mkv");

        Assert.Equal(FilingOutcome.SecondQuality, plan.Outcome);
        Assert.Equal(Directory, plan.Directory);
        Assert.Equal(
            $"{Directory}/Example Studio - 2025-11-03 - Scene Title - [2160p].mkv",
            plan.TargetPath);

        Assert.Equal($"{Directory}/Example Studio - 2025-11-03 - Scene Title.mkv", plan.Relabel?.From);
        Assert.Equal(
            $"{Directory}/Example Studio - 2025-11-03 - Scene Title - [1080p].mkv",
            plan.Relabel?.To);
    }

    /// <summary>
    /// Once every copy carries a label there is nothing to rename, and a third
    /// quality is a pure addition.
    /// </summary>
    [Fact]
    public void A_third_quality_next_to_two_labelled_ones_renames_nothing()
    {
        var directories = new Directories();
        var filed = new List<FiledCopy>
        {
            Copy("Example Studio - 2025-11-03 - Scene Title - [1080p].mkv", "1080p"),
            Copy("Example Studio - 2025-11-03 - Scene Title - [2160p].mkv", "2160p"),
        };

        foreach (var copy in filed)
        {
            directories.With(copy.Path, SceneDirectoryState.Occupied);
        }

        var plan = Plan(
            directories,
            VideoQualityReading.Of(1280, 720),
            filed: filed,
            sourceName: "some.release.720p.mkv");

        Assert.Equal(FilingOutcome.SecondQuality, plan.Outcome);
        Assert.Null(plan.Relabel);
        Assert.Equal(
            $"{Directory}/Example Studio - 2025-11-03 - Scene Title - [720p].mkv",
            plan.TargetPath);
    }

    /// <summary>
    /// The directory that is there is what the newcomer is named after, even
    /// where the layout would name that scene something else today — a collision
    /// was broken there, or a title was truncated differently. Recomputing the
    /// name would break the prefix rule section 6 rests on.
    /// </summary>
    [Fact]
    public void A_second_quality_is_named_after_the_directory_that_is_there()
    {
        const string broken = "/library/Example Studio/Example Studio - 2025-11-03 - Scene Title "
            + "[0f3a1c2b-4d5e-6f70-8192-a3b4c5d6e7f8]";

        var filed = new List<FiledCopy>
        {
            new(VideoId, broken, "Example Studio - 2025-11-03 - Scene Title "
                + "[0f3a1c2b-4d5e-6f70-8192-a3b4c5d6e7f8].mkv", "1080p"),
        };

        var plan = Plan(
            new Directories().With(filed[0].Path, SceneDirectoryState.Occupied),
            VideoQualityReading.Of(3840, 2160),
            filed: filed);

        Assert.Equal(broken, plan.Directory);
        Assert.Equal(
            broken + "/Example Studio - 2025-11-03 - Scene Title "
            + "[0f3a1c2b-4d5e-6f70-8192-a3b4c5d6e7f8] - [2160p].mkv",
            plan.TargetPath);
        Assert.StartsWith(
            System.IO.Path.GetFileName(broken),
            System.IO.Path.GetFileName(plan.TargetPath!),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A record of a file the user has since deleted is out of date, not a
    /// library that holds the scene. The scene is filed again as though for the
    /// first time.
    /// </summary>
    [Fact]
    public void A_copy_that_is_no_longer_there_does_not_hold_the_scene()
    {
        var plan = Plan(
            new Directories(),
            filed: Held("Example Studio - 2025-11-03 - Scene Title.mkv", "1080p"));

        Assert.Equal(FilingOutcome.Filed, plan.Outcome);
        Assert.Equal($"{Directory}/Example Studio - 2025-11-03 - Scene Title.mkv", plan.TargetPath);
    }

    /// <summary>
    /// Not being able to look is not the same as nothing being there. Filing on
    /// the strength of it would put a second copy next to one the tool cannot
    /// see, or rename a file it cannot find.
    /// </summary>
    [Fact]
    public void A_copy_that_cannot_be_looked_at_stops_the_filing()
    {
        var filed = Held("Example Studio - 2025-11-03 - Scene Title.mkv", "1080p");

        var plan = Plan(
            new Directories().With(filed[0].Path, SceneDirectoryState.Unknown),
            filed: filed);

        Assert.Equal(FilingOutcome.Blocked, plan.Outcome);
        Assert.False(plan.Moves);
    }

    /// <summary>
    /// ADR 0020: without a quality neither the skip nor the label can be
    /// decided, and the hard rule says nothing is written on a partial answer.
    /// </summary>
    [Fact]
    public void A_file_whose_quality_could_not_be_read_is_not_filed()
    {
        var plan = Plan(new Directories(), new VideoQualityReading(VideoQualityState.Unreadable));

        Assert.Equal(FilingOutcome.Blocked, plan.Outcome);
        Assert.False(plan.Moves);
        Assert.NotNull(plan.Message);
    }

    /// <summary>
    /// ADR 0019: a file prdb cannot name is not filed at all. The planner is
    /// given the answer to that question rather than asking it, and says so
    /// rather than throwing.
    /// </summary>
    [Fact]
    public void A_file_prdb_could_not_name_is_not_filed()
    {
        var directories = new Directories();

        var plan = new FilingPlanner(
            new TargetPaths(directories),
            directories,
            new Sidecars(),
            new Artwork()).Plan(
            fileId: 7,
            sourcePath: "/downloads/some.release.mkv",
            sourceName: "some.release.mkv",
            Root,
            FileMovement.Rename,
            scene: null,
            VideoQualityReading.Of(1920, 1080),
            []);

        Assert.Equal(FilingOutcome.Blocked, plan.Outcome);
        Assert.False(plan.Moves);
        Assert.Contains("review queue", plan.Message);
    }

    /// <summary>
    /// #18: the video is only half of what a filing writes. The sidecar is on
    /// the plan for the same reason the move is — it is a write, and a write the
    /// user has not read about is one they cannot refuse.
    /// </summary>
    [Fact]
    public void A_scene_the_library_does_not_hold_gets_a_sidecar()
    {
        var sidecars = new Sidecars();

        var plan = Plan(new Directories(), sidecars: sidecars);

        Assert.Equal(SidecarAction.Write, plan.Sidecar.Action);
        Assert.Equal($"{Directory}/movie.nfo", plan.Sidecar.Path);
        Assert.NotNull(plan.Sidecar.InWords);

        // And the filesystem was not asked. A directory holding anything at all
        // counts as occupied, so a scene directory this filing may write into is
        // one that demonstrably has no sidecar in it.
        Assert.Empty(sidecars.Asked);
    }

    /// <summary>
    /// The one case where there may already be one: a second quality goes into
    /// the directory the first was filed into, and the tool wrote that sidecar.
    /// </summary>
    [Fact]
    public void A_sidecar_the_tool_wrote_is_written_again()
    {
        var filed = Held("Example Studio - 2025-11-03 - Scene Title.mkv", "1080p");

        var plan = Plan(
            new Directories().With(filed[0].Path, SceneDirectoryState.Occupied),
            VideoQualityReading.Of(3840, 2160),
            filed: filed,
            sourceName: "some.release.2160p.mkv",
            sidecars: new Sidecars().With($"{Directory}/movie.nfo", SidecarState.Ours));

        Assert.Equal(SidecarAction.Replace, plan.Sidecar.Action);
        Assert.True(plan.Sidecar.Writes);
    }

    /// <summary>
    /// A hand-written sidecar is most likely to be at this very name — it is
    /// what Jellyfin reads — so there is no stepping around it. It stays, and
    /// the plan says so before anything is moved.
    /// </summary>
    [Theory]
    [InlineData(SidecarState.Foreign)]
    [InlineData(SidecarState.Unknown)]
    public void A_sidecar_the_tool_did_not_write_is_left_alone(SidecarState state)
    {
        var filed = Held("Example Studio - 2025-11-03 - Scene Title.mkv", "1080p");

        var plan = Plan(
            new Directories().With(filed[0].Path, SceneDirectoryState.Occupied),
            VideoQualityReading.Of(3840, 2160),
            filed: filed,
            sourceName: "some.release.2160p.mkv",
            sidecars: new Sidecars().With($"{Directory}/movie.nfo", state));

        Assert.Equal(SidecarAction.Keep, plan.Sidecar.Action);
        Assert.False(plan.Sidecar.Writes);
        Assert.NotNull(plan.Sidecar.Message);

        // The video still goes in next to it. What is refused is writing over
        // somebody's file, not filing the video they will want to watch.
        Assert.True(plan.Moves);
    }

    /// <summary>
    /// Nothing is filed, so nothing is written — not even into a directory whose
    /// sidecar has gone missing. When one is refreshed is a decision of its own,
    /// and a run reporting that it moved nothing must not be quietly writing.
    /// </summary>
    [Fact]
    public void A_run_that_moves_nothing_writes_no_sidecar_either()
    {
        var filed = Held("Example Studio - 2025-11-03 - Scene Title.mkv", "1080p");

        var already = Plan(new Directories().With(filed[0].Path, SceneDirectoryState.Occupied), filed: filed);
        var blocked = Plan(new Directories(), new VideoQualityReading(VideoQualityState.Unreadable));

        Assert.Equal(SidecarAction.None, already.Sidecar.Action);
        Assert.Equal(SidecarAction.None, blocked.Sidecar.Action);
        Assert.Null(already.Sidecar.Path);
        Assert.Null(blocked.Sidecar.InWords);
    }

    /// <summary>
    /// ADR 0027: off unless somebody turned it on. The default is not a
    /// convenience here — it is the hard rule applied to bandwidth, and a plan
    /// that promised a download nobody asked for would be the preview lying
    /// about the run.
    /// </summary>
    [Fact]
    public void Artwork_is_not_written_unless_somebody_turned_it_on()
    {
        var artwork = new Artwork();

        var plan = Plan(new Directories(), artwork: artwork);

        Assert.Equal(ArtworkAction.None, plan.Artwork.Action);
        Assert.Null(plan.Artwork.Path);
        Assert.Null(plan.Artwork.InWords);

        // And a switch that is off costs not even the question.
        Assert.Empty(artwork.Asked);
    }

    /// <summary>
    /// With it on, the image is named next to the video before anything is
    /// downloaded — one file, <c>fanart.jpg</c>, and no poster: section 5
    /// measured a landscape image in the Primary slot to be worse than none.
    /// </summary>
    [Fact]
    public void Artwork_that_is_on_names_one_image_in_the_scene_directory()
    {
        var artwork = new Artwork();

        var plan = Plan(new Directories(), artwork: artwork, wantsArtwork: true);

        Assert.Equal(ArtworkAction.Write, plan.Artwork.Action);
        Assert.Equal($"{Directory}/fanart.jpg", plan.Artwork.Path);
        Assert.NotNull(plan.Artwork.InWords);

        // The same reason the sidecar is not asked about: a directory holding
        // anything at all counts as occupied, so a target that got this far is
        // one no image can be sitting in.
        Assert.Empty(artwork.Asked);
    }

    /// <summary>
    /// The decision ADR 0027 exists for. A file at that name stays, whoever put
    /// it there — the tool last month or the user this morning — because the two
    /// want the same thing and neither is worth a marker inside a JPEG.
    /// </summary>
    [Theory]
    [InlineData(ArtworkState.Present)]
    [InlineData(ArtworkState.Unknown)]
    public void An_image_that_is_already_there_is_never_written_over(ArtworkState state)
    {
        var filed = Held("Example Studio - 2025-11-03 - Scene Title.mkv", "1080p");

        var plan = Plan(
            new Directories().With(filed[0].Path, SceneDirectoryState.Occupied),
            VideoQualityReading.Of(3840, 2160),
            filed: filed,
            sourceName: "some.release.2160p.mkv",
            artwork: new Artwork().With($"{Directory}/fanart.jpg", state),
            wantsArtwork: true);

        Assert.Equal(ArtworkAction.Keep, plan.Artwork.Action);
        Assert.False(plan.Artwork.Writes);
        Assert.NotNull(plan.Artwork.Message);

        // And the video still goes in next to it, as with the sidecar.
        Assert.True(plan.Moves);
    }

    /// <summary>
    /// A scene directory the library already holds with no image in it gets one.
    /// That is the affordance ADR 0027 gives a user with no setting to find:
    /// delete the file, and the next filing into that scene brings a fresh one.
    /// </summary>
    [Fact]
    public void A_scene_directory_with_no_image_in_it_gets_one()
    {
        var filed = Held("Example Studio - 2025-11-03 - Scene Title.mkv", "1080p");

        var plan = Plan(
            new Directories().With(filed[0].Path, SceneDirectoryState.Occupied),
            VideoQualityReading.Of(3840, 2160),
            filed: filed,
            sourceName: "some.release.2160p.mkv",
            wantsArtwork: true);

        Assert.Equal(FilingOutcome.SecondQuality, plan.Outcome);
        Assert.Equal(ArtworkAction.Write, plan.Artwork.Action);
        Assert.Equal($"{Directory}/fanart.jpg", plan.Artwork.Path);
    }

    /// <summary>
    /// Nothing is filed, so nothing is downloaded — with artwork on as much as
    /// with it off. A run that reports moving nothing must not be quietly
    /// spending somebody's connection.
    /// </summary>
    [Fact]
    public void A_run_that_moves_nothing_downloads_nothing_either()
    {
        var filed = Held("Example Studio - 2025-11-03 - Scene Title.mkv", "1080p");

        var already = Plan(
            new Directories().With(filed[0].Path, SceneDirectoryState.Occupied),
            filed: filed,
            wantsArtwork: true);

        var blocked = Plan(
            new Directories(),
            new VideoQualityReading(VideoQualityState.Unreadable),
            wantsArtwork: true);

        Assert.Equal(ArtworkAction.None, already.Artwork.Action);
        Assert.Equal(ArtworkAction.None, blocked.Artwork.Action);
        Assert.Null(already.Artwork.Path);
        Assert.Null(blocked.Artwork.InWords);
    }

    /// <summary>
    /// The preview and the run are the same call, so asking twice with nothing
    /// changed has to answer the same thing. A plan that drifted would be a
    /// preview of something else.
    /// </summary>
    [Fact]
    public void Asking_twice_answers_the_same_thing()
    {
        var directories = new Directories().With(Directory, SceneDirectoryState.Occupied);

        Assert.Equal(Plan(directories), Plan(directories));
    }

    /// <summary>
    /// Whether this is an instant rename or an hour of copying is known before
    /// anything happens, because it is the sentence the user reads while
    /// deciding.
    /// </summary>
    [Fact]
    public void The_plan_says_how_the_file_would_travel() =>
        Assert.Equal(
            FileMovement.CopyThenDelete,
            Plan(new Directories(), movement: FileMovement.CopyThenDelete).Movement);

    private static List<FiledCopy> Held(string fileName, string quality) => [Copy(fileName, quality)];

    private static FiledCopy Copy(string fileName, string quality) =>
        new(VideoId, Directory, fileName, quality);
}

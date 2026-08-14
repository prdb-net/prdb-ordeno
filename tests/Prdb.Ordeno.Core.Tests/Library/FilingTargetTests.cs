using Prdb.Ordeno.Core.Library;

using Xunit;

namespace Prdb.Ordeno.Core.Tests.Library;

/// <summary>
/// What the planner does with what is already on disk. The names it computes are
/// tested next door; this is about the one question it asks the filesystem and
/// the three answers it gives back.
/// </summary>
public sealed class FilingTargetTests
{
    private const string Root = "/library";

    private static readonly Scene Scene =
        new(Guid.Parse("0f3a1c2b-4d5e-6f70-8192-a3b4c5d6e7f8"), "Example Studio", "Scene Title",
            new DateOnly(2025, 11, 3));

    /// <summary>
    /// Answers what it is told to and remembers what it was asked, so that a test
    /// can also show the filesystem was not consulted twice about the same thing.
    /// </summary>
    private sealed class Directories(params SceneDirectoryState[] answers) : ISceneDirectories
    {
        private int asked;

        public List<string> Asked { get; } = [];

        public SceneDirectoryState StateOf(string absolutePath)
        {
            Asked.Add(absolutePath);

            return asked < answers.Length ? answers[asked++] : SceneDirectoryState.Free;
        }
    }

    [Fact]
    public void A_free_name_is_the_one_the_layout_wanted()
    {
        var target = new TargetPaths(new Directories(SceneDirectoryState.Free))
            .For(Root, Scene, "some.release.name.1080p.mkv");

        Assert.Equal(FilingTargetOutcome.Ready, target.Outcome);
        Assert.Equal("Example Studio - 2025-11-03 - Scene Title", target.Path?.SceneDirectory);
        Assert.Null(target.Message);

        // The release name the file arrived under survives nowhere in the target.
        Assert.Equal(
            "/library/Example Studio/Example Studio - 2025-11-03 - Scene Title",
            target.Directory);
        Assert.Equal(
            "/library/Example Studio/Example Studio - 2025-11-03 - Scene Title/"
            + "Example Studio - 2025-11-03 - Scene Title - [1080p].mkv",
            target.VideoFile("1080p"));
    }

    /// <summary>
    /// An empty directory is free. A filing that stopped half way, or a directory
    /// somebody made by hand, is not another scene — and refusing to write into
    /// it would leave a library nobody can repair without a file manager.
    /// </summary>
    [Fact]
    public void An_empty_directory_is_free()
    {
        var target = new TargetPaths(new Directories(SceneDirectoryState.Free)).For(Root, Scene, "v.mkv");

        Assert.Equal(FilingTargetOutcome.Ready, target.Outcome);
    }

    /// <summary>
    /// The collision this issue exists for, constructed on purpose: two different
    /// scenes from one site on one date whose titles differ only past the point
    /// where sanitising and truncation have taken everything away. Filed into one
    /// directory they would become a single Jellyfin entry with two versions, and
    /// one of the two would stop existing as a thing of its own.
    /// </summary>
    [Fact]
    public void Two_scenes_that_want_one_directory_get_two()
    {
        var first = new Scene(Guid.NewGuid(), "Example Studio", "A/B", new DateOnly(2025, 11, 3));
        var second = new Scene(Guid.NewGuid(), "Example Studio", "A:B", new DateOnly(2025, 11, 3));

        // They do collide — that is the premise, and it is worth failing on if
        // sanitising ever stops being lossy in this way.
        Assert.Equal(
            JellyfinPaths.For(first, ".mkv").SceneDirectory,
            JellyfinPaths.For(second, ".mkv").SceneDirectory);

        var filedFirst = new TargetPaths(new Directories(SceneDirectoryState.Free))
            .For(Root, first, "first.mkv");

        // The second one finds the first one's directory in the way.
        var filedSecond = new TargetPaths(new Directories(SceneDirectoryState.Occupied))
            .For(Root, second, "second.mkv");

        Assert.Equal(FilingTargetOutcome.Ready, filedFirst.Outcome);
        Assert.Equal(FilingTargetOutcome.CollisionBroken, filedSecond.Outcome);
        Assert.NotEqual(filedFirst.Path?.SceneDirectory, filedSecond.Path?.SceneDirectory);
        Assert.EndsWith($"[{second.VideoId:d}]", filedSecond.Path!.SceneDirectory, StringComparison.Ordinal);

        // What the user is told names the scene they know, not the id.
        Assert.Contains("already holds something else", filedSecond.Message ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    /// The id in that name is this scene's own, so a directory already carrying
    /// it is this scene already filed — a duplicate or a re-file. Both are
    /// decisions for #17 and ADR 0003, and neither is made by moving a file.
    /// </summary>
    [Fact]
    public void A_name_carrying_this_scenes_id_being_taken_stops_the_filing()
    {
        var target = new TargetPaths(new Directories(SceneDirectoryState.Occupied, SceneDirectoryState.Occupied))
            .For(Root, Scene, "v.mkv");

        Assert.Equal(FilingTargetOutcome.Blocked, target.Outcome);
        Assert.Null(target.Path);
        Assert.False(target.CanBeFiled);
        Assert.NotNull(target.Message);
    }

    /// <summary>
    /// A directory that could not be looked at is not a collision, so it is not
    /// filed around. Sidestepping it would mean writing into a library on the
    /// evidence of a permission error.
    /// </summary>
    [Fact]
    public void A_directory_that_could_not_be_looked_at_stops_the_filing()
    {
        var directories = new Directories(SceneDirectoryState.Unknown);
        var target = new TargetPaths(directories).For(Root, Scene, "v.mkv");

        Assert.Equal(FilingTargetOutcome.Blocked, target.Outcome);
        Assert.Single(directories.Asked);
    }

    /// <summary>
    /// Determinism, which the operation log and every re-filing depend on: the
    /// same scene and the same metadata produce the same path, however often it
    /// is asked for.
    /// </summary>
    [Fact]
    public void The_same_scene_produces_the_same_path()
    {
        var paths = new TargetPaths(new Directories());

        Assert.Equal(
            paths.For(Root, Scene, "one.name.mkv").Path,
            paths.For(Root, Scene, "another.name.entirely.mkv").Path);
    }
}

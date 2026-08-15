using System.Security.Cryptography;

using Microsoft.Extensions.Logging.Abstractions;

using Prdb.Ordeno.Core.Configuration;
using Prdb.Ordeno.Infrastructure.Library;

using Xunit;

namespace Prdb.Ordeno.Infrastructure.Tests.Library;

/// <summary>
/// The one place in this tool that moves a file somebody cannot get back, tested
/// against a real filesystem in a temporary directory as <c>AGENTS.md</c> asks.
/// </summary>
/// <remarks>
/// What is being tested is mostly what a failure leaves behind. Every case here
/// has one question under it: after this, is the video still somewhere? A mocked
/// file layer cannot have a half-finished copy or a target that turned out to be
/// taken, which is exactly why these run against a real one.
/// </remarks>
public sealed class LibraryMovesTests : IDisposable
{
    private readonly LibraryMoves moves = new(NullLogger<LibraryMoves>.Instance);
    private readonly TempDirectory temp = new();

    public void Dispose() => temp.Dispose();

    private string Downloads => Ensure("downloads");

    private string Library => Ensure("library");

    private string Scene => Ensure("library/Example Studio/Example Studio - Scene Title");

    [Theory]
    [InlineData(FileMovement.Rename)]
    [InlineData(FileMovement.CopyThenDelete)]
    public async Task A_video_ends_up_where_it_was_sent_and_leaves_nothing_behind(FileMovement movement)
    {
        var content = RandomNumberGenerator.GetBytes(300 * 1024);
        var source = Write("downloads/some.release.1080p.mkv", content);
        var target = Path.Combine(Scene, "Example Studio - Scene Title.mkv");

        var outcome = await moves.FileAsync(source, target, Library, movement);

        Assert.True(outcome.Moved);
        Assert.Null(outcome.Problem);
        Assert.False(File.Exists(source));
        Assert.Equal(content, await File.ReadAllBytesAsync(target));
        Assert.Empty(Staged());
    }

    /// <summary>
    /// The directory a scene goes in is made on the way. Nothing else creates
    /// it, and a filing that stopped because a parent was missing would be a
    /// failure with no cause a user could act on.
    /// </summary>
    [Fact]
    public async Task The_scene_directory_is_created_if_it_is_not_there()
    {
        var source = Write("downloads/some.release.mkv", [1, 2, 3]);
        var target = Path.Combine(Library, "Example Studio", "Example Studio - Scene Title", "video.mkv");

        Assert.True((await moves.FileAsync(source, target, Library, FileMovement.Rename)).Moved);
        Assert.True(File.Exists(target));
    }

    /// <summary>
    /// The hard rule: a path that is taken is not an overwrite. Both shapes stop,
    /// and the file stays in the download directory holding what it held.
    /// </summary>
    [Theory]
    [InlineData(FileMovement.Rename)]
    [InlineData(FileMovement.CopyThenDelete)]
    public async Task A_target_that_is_taken_is_never_written_over(FileMovement movement)
    {
        var source = Write("downloads/some.release.mkv", [1, 2, 3]);
        var target = Path.Combine(Scene, "Example Studio - Scene Title.mkv");
        await File.WriteAllBytesAsync(target, [9, 9, 9]);

        var outcome = await moves.FileAsync(source, target, Library, movement);

        Assert.Equal(MoveState.TargetTaken, outcome.State);
        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(source));
        Assert.Equal([9, 9, 9], await File.ReadAllBytesAsync(target));
        Assert.Empty(Staged());
    }

    [Fact]
    public async Task A_file_that_is_gone_is_reported_rather_than_thrown()
    {
        var outcome = await moves.FileAsync(
            Path.Combine(Downloads, "never-existed.mkv"),
            Path.Combine(Scene, "video.mkv"),
            Library,
            FileMovement.CopyThenDelete);

        Assert.Equal(MoveState.SourceMissing, outcome.State);
    }

    /// <summary>
    /// The one the issue asks for by name: a cross-filesystem move interrupted
    /// at any point loses nothing. Rather than trying to stop it at one chosen
    /// instant — which is a race dressed up as a test — this interrupts it at a
    /// spread of moments and asserts the invariant that has to hold at every one
    /// of them: the video is either still in the download directory or complete
    /// in the library, never neither, and never a part file left where something
    /// would read it.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public async Task A_copy_interrupted_partway_loses_nothing(int afterMilliseconds)
    {
        var content = RandomNumberGenerator.GetBytes(48 * 1024 * 1024);
        var source = Write("downloads/some.release.2160p.mkv", content);
        var target = Path.Combine(Scene, "Example Studio - Scene Title.mkv");

        using var stopping = new CancellationTokenSource(TimeSpan.FromMilliseconds(afterMilliseconds));

        try
        {
            await moves.FileAsync(source, target, Library, FileMovement.CopyThenDelete, stopping.Token);
        }
        catch (OperationCanceledException)
        {
            // The container was stopping. Everything asserted below still holds.
        }

        if (File.Exists(target))
        {
            Assert.Equal(content, await File.ReadAllBytesAsync(target));
            Assert.False(File.Exists(source));
        }
        else
        {
            Assert.Equal(content, await File.ReadAllBytesAsync(source));
        }

        // Whatever happened, no half-written copy is left under a name anything
        // reads, and none is left in the staging directory either.
        Assert.Empty(Staged());
    }

    /// <summary>
    /// A container killed rather than stopped leaves the part file behind. The
    /// run that would have cleaned it up is the run that did not finish, so the
    /// next one does it on the way in — and it is under a dotted directory
    /// meanwhile, where neither the media server nor this tool's own walk looks.
    /// </summary>
    [Fact]
    public void What_a_killed_container_left_behind_is_cleared_on_the_way_in()
    {
        var staging = Directory.CreateDirectory(
            Path.Combine(Library, LibraryMoves.StagingDirectoryName));
        var leftover = Path.Combine(staging.FullName, "8d2f.part");
        File.WriteAllBytes(leftover, [1, 2, 3]);

        moves.ClearStaging(Library);

        Assert.False(File.Exists(leftover));
    }

    [Fact]
    public void Clearing_a_library_that_never_staged_anything_does_nothing() =>
        moves.ClearStaging(Library);

    /// <summary>
    /// ADR 0020's rename. It happens inside one directory, so there is no copy
    /// and nothing to verify — but it is still a write against a file the user
    /// considers filed, and the cases where it must not happen are the point.
    /// </summary>
    [Fact]
    public void A_filed_file_is_relabelled_in_place()
    {
        var from = Path.Combine(Scene, "Example Studio - Scene Title.mkv");
        var to = Path.Combine(Scene, "Example Studio - Scene Title - [1080p].mkv");
        File.WriteAllBytes(from, [1, 2, 3]);

        Assert.True(moves.Relabel(from, to).Moved);
        Assert.False(File.Exists(from));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(to));
    }

    [Fact]
    public void A_file_that_is_no_longer_there_is_not_relabelled()
    {
        var outcome = moves.Relabel(
            Path.Combine(Scene, "gone.mkv"),
            Path.Combine(Scene, "gone - [1080p].mkv"));

        Assert.Equal(MoveState.SourceMissing, outcome.State);
        Assert.NotNull(outcome.Problem);
    }

    [Fact]
    public void A_relabel_onto_a_name_that_exists_is_refused()
    {
        var from = Path.Combine(Scene, "Example Studio - Scene Title.mkv");
        var to = Path.Combine(Scene, "Example Studio - Scene Title - [1080p].mkv");
        File.WriteAllBytes(from, [1, 2, 3]);
        File.WriteAllBytes(to, [9, 9, 9]);

        Assert.Equal(MoveState.TargetTaken, moves.Relabel(from, to).State);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(from));
        Assert.Equal([9, 9, 9], File.ReadAllBytes(to));
    }

    private string[] Staged()
    {
        var staging = Path.Combine(Library, LibraryMoves.StagingDirectoryName);

        return Directory.Exists(staging) ? Directory.GetFiles(staging) : [];
    }

    private string Ensure(string relative) =>
        Directory.CreateDirectory(Path.Combine(temp.Root, relative)).FullName;

    private string Write(string relative, byte[] content)
    {
        var path = Path.Combine(temp.Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);

        return path;
    }
}

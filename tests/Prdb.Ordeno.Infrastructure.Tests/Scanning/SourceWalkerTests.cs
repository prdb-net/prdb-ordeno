using System.Runtime.Versioning;

using Microsoft.Extensions.Logging.Abstractions;

using Prdb.Ordeno.Infrastructure.Scanning;

using Xunit;

namespace Prdb.Ordeno.Infrastructure.Tests.Scanning;

/// <summary>
/// Against a real directory tree, because everything worth testing about a walk
/// is a property of a real filesystem: a symlink that points at its own parent,
/// a directory this process may not enter, a file that vanishes while the walk
/// is in the middle of it.
/// </summary>
public sealed class SourceWalkerTests
{
    private readonly SourceWalker walker = new(NullLogger<SourceWalker>.Instance);

    [Fact]
    public void Videos_are_found_however_deep_they_sit()
    {
        using var directory = new TempDirectory();
        Write(directory.Combine("one.mkv"));
        Write(directory.Combine("release/two.mp4"));
        Write(directory.Combine("release/extras/three.avi"));

        var found = Names(directory.Root);

        Assert.Equal(["one.mkv", "three.avi", "two.mp4"], found);
    }

    [Fact]
    public void What_is_not_a_finished_video_is_not_reported()
    {
        using var directory = new TempDirectory();
        Write(directory.Combine("video.mkv.part"));
        Write(directory.Combine("video.mkv.!qB"));
        Write(directory.Combine("release.rar"));
        Write(directory.Combine("release.r00"));
        Write(directory.Combine("._resource-fork.mkv"));
        Write(directory.Combine("notes.txt"));

        Assert.Empty(Names(directory.Root));
    }

    [Fact]
    public void A_directory_a_NAS_keeps_to_itself_is_not_walked()
    {
        using var directory = new TempDirectory();
        Write(directory.Combine("@eaDir/thumbnail.mkv"));
        Write(directory.Combine("#recycle/deleted.mkv"));
        Write(directory.Combine(".Trash-1000/thrown-away.mkv"));
        Write(directory.Combine("keep/wanted.mkv"));

        Assert.Equal(["wanted.mkv"], Names(directory.Root));
    }

    /// <summary>
    /// The loop that would otherwise never end. A share is allowed to contain a
    /// link back up its own tree, and a tool that walks it forever is a tool that
    /// pins a CPU on someone's NAS until they notice.
    /// </summary>
    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void A_symlink_pointing_back_up_the_tree_does_not_loop()
    {
        using var directory = new TempDirectory();
        Write(directory.Combine("release/video.mkv"));
        Directory.CreateSymbolicLink(directory.Combine("release/back"), directory.Root);

        Assert.Equal(["video.mkv"], Names(directory.Root));
    }

    /// <summary>
    /// The library must never be walked as if it were a download directory: the
    /// tool would find what it had just filed and file it again. The
    /// configuration refuses the two overlapping, but it compares the paths as
    /// they were typed, and a symlink makes two different paths the same place.
    /// </summary>
    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void The_library_is_left_alone_even_when_it_is_reached_through_a_link()
    {
        using var directory = new TempDirectory();
        var library = Directory.CreateDirectory(directory.Combine("library")).FullName;
        var downloads = Directory.CreateDirectory(directory.Combine("downloads")).FullName;

        Write(Path.Combine(downloads, "new.mkv"));
        Write(Path.Combine(library, "Site/Site - Scene/Site - Scene.mkv"));
        Directory.CreateSymbolicLink(Path.Combine(downloads, "filed"), library);

        Assert.Equal(["new.mkv"], Names(downloads, library));
    }

    /// <summary>
    /// One directory the process may not enter is a permission to fix, not a
    /// reason to lose the three thousand files next to it.
    /// </summary>
    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void An_unreadable_subdirectory_does_not_end_the_walk()
    {
        using var directory = new TempDirectory();
        var closed = Directory.CreateDirectory(directory.Combine("closed")).FullName;
        Write(Path.Combine(closed, "hidden-away.mkv"));
        Write(directory.Combine("readable.mkv"));

        File.SetUnixFileMode(closed, UnixFileMode.None);

        try
        {
            var found = Names(directory.Root);

            if (found.Contains("hidden-away.mkv"))
            {
                // Running as a user the permission bits do not apply to — root in
                // a container. There is nothing to assert then.
                return;
            }

            Assert.Equal(["readable.mkv"], found);
        }
        finally
        {
            File.SetUnixFileMode(
                closed,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public void A_directory_that_is_not_there_is_no_files_rather_than_a_failure()
    {
        using var directory = new TempDirectory();

        Assert.Empty(Names(directory.Combine("never-mounted")));
    }

    [Fact]
    public void Size_and_modification_time_come_back_with_the_file()
    {
        using var directory = new TempDirectory();
        var path = directory.Combine("video.mkv");
        File.WriteAllBytes(path, new byte[2048]);

        var observed = Assert.Single(walker.Walk(directory.Root, excluded: null));

        Assert.Equal(path, observed.Path);
        Assert.Equal(2048, observed.SizeBytes);
        Assert.Equal(File.GetLastWriteTimeUtc(path), observed.LastWriteAt.UtcDateTime);
    }

    private List<string> Names(string root, string? excluded = null) =>
        [.. walker.Walk(root, excluded).Select(file => Path.GetFileName(file.Path)).Order(StringComparer.Ordinal)];

    private static void Write(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[512]);
    }
}

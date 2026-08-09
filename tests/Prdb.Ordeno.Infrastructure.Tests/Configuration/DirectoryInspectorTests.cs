using System.Runtime.Versioning;

using Prdb.Ordeno.Core.Configuration;
using Prdb.Ordeno.Infrastructure.Configuration;

using Xunit;

namespace Prdb.Ordeno.Infrastructure.Tests.Configuration;

/// <summary>
/// Against a real filesystem, because every interesting answer here is one a
/// mocked file layer cannot have: a permission that is set but does not apply, a
/// directory that is a file, two paths that look alike and sit on different
/// mounts.
/// </summary>
public sealed class DirectoryInspectorTests
{
    /// <summary>What a directory is put back to once a test has taken its permissions away.</summary>
    private const UnixFileMode DefaultMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private readonly DirectoryInspector inspector = new();

    [Fact]
    public void A_readable_directory_is_usable_as_a_source()
    {
        using var directory = new TempDirectory();

        var inspection = inspector.Inspect(directory.Root, DirectoryRole.Source);

        Assert.True(inspection.Usable);
        Assert.Null(inspection.Message);
    }

    [Fact]
    public void A_path_nothing_is_mounted_at_says_so_in_terms_of_the_volume()
    {
        using var directory = new TempDirectory();
        var missing = directory.Combine("never-mounted");

        var inspection = inspector.Inspect(missing, DirectoryRole.Source);

        Assert.Equal(DirectoryProblem.Missing, inspection.Problem);
        Assert.Contains(missing, inspection.Message!, StringComparison.Ordinal);
        Assert.Contains("volume", inspection.Message!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_is_not_a_directory()
    {
        using var directory = new TempDirectory();
        var file = directory.Combine("video.mkv");
        File.WriteAllText(file, "not a directory");

        Assert.Equal(DirectoryProblem.NotADirectory, inspector.Inspect(file, DirectoryRole.Source).Problem);
    }

    [Theory]
    [InlineData("downloads")]
    [InlineData("./downloads")]
    public void A_relative_path_means_nothing_to_a_container(string path) =>
        Assert.Equal(DirectoryProblem.NotAbsolute, inspector.Inspect(path, DirectoryRole.Source).Problem);

    [Fact]
    public void An_empty_path_is_asked_for_again() =>
        Assert.Equal(DirectoryProblem.Empty, inspector.Inspect("   ", DirectoryRole.Target).Problem);

    /// <summary>
    /// The check that separates a source from a target: the tool writes into the
    /// library and only reads the downloads, so a read-only download share is
    /// fine and a read-only library is not.
    /// </summary>
    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void A_directory_that_cannot_be_written_to_is_refused_as_a_target_and_accepted_as_a_source()
    {
        using var directory = new TempDirectory();
        var readOnly = directory.Combine("read-only");
        Directory.CreateDirectory(readOnly);
        File.SetUnixFileMode(readOnly, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        try
        {
            if (StillWritable(readOnly))
            {
                // Running as a user the permission bits do not apply to — root in
                // a container, most likely. There is nothing to assert then, and
                // asserting anyway would fail for a reason that is not a defect.
                return;
            }

            Assert.Equal(DirectoryProblem.NotWritable, inspector.Inspect(readOnly, DirectoryRole.Target).Problem);
            Assert.True(inspector.Inspect(readOnly, DirectoryRole.Source).Usable);
        }
        finally
        {
            // Left as it was found, or the temporary directory cannot be removed.
            File.SetUnixFileMode(readOnly, DefaultMode);
        }
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void A_directory_that_cannot_be_listed_is_refused()
    {
        using var directory = new TempDirectory();
        var closed = directory.Combine("closed");
        Directory.CreateDirectory(closed);
        File.SetUnixFileMode(closed, UnixFileMode.None);

        try
        {
            if (StillListable(closed))
            {
                return;
            }

            Assert.Equal(DirectoryProblem.NotReadable, inspector.Inspect(closed, DirectoryRole.Source).Problem);
        }
        finally
        {
            File.SetUnixFileMode(closed, DefaultMode);
        }
    }

    /// <summary>
    /// The write probe is a real file. It must leave nothing behind, because the
    /// directory it is written to is the user's library.
    /// </summary>
    [Fact]
    public void Checking_a_target_leaves_it_as_it_was()
    {
        using var directory = new TempDirectory();

        Assert.True(inspector.Inspect(directory.Root, DirectoryRole.Target).Usable);
        Assert.Empty(Directory.EnumerateFileSystemEntries(directory.Root));
    }

    [Fact]
    public void Two_directories_on_one_filesystem_are_renamed_between()
    {
        using var directory = new TempDirectory();
        var downloads = Directory.CreateDirectory(directory.Combine("downloads")).FullName;
        var library = Directory.CreateDirectory(directory.Combine("library")).FullName;

        Assert.Equal(FileMovement.Rename, inspector.MovementBetween(downloads, library));
    }

    /// <summary>
    /// The case ADR 0002 warns about. <c>/dev/shm</c> is a mount of its own
    /// wherever it exists, which makes it the one second filesystem a test can
    /// count on without asking for a disk.
    /// </summary>
    [Fact]
    public void Two_directories_on_different_mounts_are_copied_between()
    {
        const string OtherMount = "/dev/shm";

        if (!Directory.Exists(OtherMount) || !OnItsOwnMount(OtherMount))
        {
            return;
        }

        using var directory = new TempDirectory();
        var elsewhere = Path.Combine(OtherMount, $"prdb-ordeno-tests-{Guid.NewGuid():n}");

        Directory.CreateDirectory(elsewhere);
        try
        {
            Assert.Equal(FileMovement.CopyThenDelete, inspector.MovementBetween(elsewhere, directory.Root));
        }
        finally
        {
            Directory.Delete(elsewhere, recursive: true);
        }
    }

    private static bool OnItsOwnMount(string path) =>
        DriveInfo.GetDrives().Any(drive =>
            string.Equals(
                Path.TrimEndingDirectorySeparator(drive.RootDirectory.FullName),
                Path.TrimEndingDirectorySeparator(path),
                StringComparison.Ordinal));

    private static bool StillWritable(string path)
    {
        try
        {
            var probe = Path.Combine(path, "probe");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);

            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    private static bool StillListable(string path)
    {
        try
        {
            Directory.EnumerateFileSystemEntries(path).FirstOrDefault();

            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}

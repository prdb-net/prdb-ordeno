using Prdb.Ordeno.Core.Library;
using Prdb.Ordeno.Infrastructure.Library;

using Xunit;

namespace Prdb.Ordeno.Infrastructure.Tests.Library;

/// <summary>
/// The one question the path computation asks a filesystem, against a real one.
/// The distinction that matters is between a directory with nothing in it and a
/// directory with something in it — the second is somebody else's scene, and
/// filing into it is the silent merge the layout research warned about.
/// </summary>
public sealed class SceneDirectoriesTests
{
    private readonly SceneDirectories directories = new();

    [Fact]
    public void A_path_with_nothing_at_it_is_free()
    {
        using var temp = new TempDirectory();

        Assert.Equal(SceneDirectoryState.Free, directories.StateOf(temp.Combine("not there")));
    }

    [Fact]
    public void An_empty_directory_is_free()
    {
        using var temp = new TempDirectory();
        var scene = temp.Combine("Example Studio - 2025-11-03 - Scene Title");
        Directory.CreateDirectory(scene);

        Assert.Equal(SceneDirectoryState.Free, directories.StateOf(scene));
    }

    /// <summary>
    /// Anything at all counts, and a leftover sidecar counts most: a directory
    /// holding a movie.nfo and no video is a filing that stopped half way, and
    /// writing a second scene's video next to that sidecar produces one entry
    /// wearing the other scene's metadata.
    /// </summary>
    [Theory]
    [InlineData("Example Studio - 2025-11-03 - Scene Title.mkv")]
    [InlineData("movie.nfo")]
    public void A_directory_with_something_in_it_is_occupied(string name)
    {
        using var temp = new TempDirectory();
        var scene = temp.Combine("Example Studio - 2025-11-03 - Scene Title");
        Directory.CreateDirectory(scene);
        File.WriteAllText(System.IO.Path.Combine(scene, name), string.Empty);

        Assert.Equal(SceneDirectoryState.Occupied, directories.StateOf(scene));
    }

    /// <summary>
    /// A file where a directory was expected is not free, whatever else it is.
    /// The move would fail, and reporting it as a free name would put that
    /// failure in front of the user as a bug rather than as a name they have to
    /// look at.
    /// </summary>
    [Fact]
    public void A_file_in_the_way_is_not_free()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("Example Studio - 2025-11-03 - Scene Title");
        File.WriteAllText(path, string.Empty);

        Assert.NotEqual(SceneDirectoryState.Free, directories.StateOf(path));
    }
}

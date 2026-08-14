using System.Text;

using Prdb.Ordeno.Core.Library;

using Xunit;

namespace Prdb.Ordeno.Core.Tests.Library;

/// <summary>
/// The names one scene turns into. Everything here is decided from the scene and
/// nothing from a filesystem, so a name that is wrong is wrong in the same way on
/// every machine — which is the property re-filing and the operation log depend
/// on.
/// </summary>
public sealed class ScenePathTests
{
    private static readonly Guid SceneId = Guid.Parse("0f3a1c2b-4d5e-6f70-8192-a3b4c5d6e7f8");

    private static Scene AScene(string title = "Scene Title", string site = "Example Studio") =>
        new(SceneId, site, title, new DateOnly(2025, 11, 3));

    [Fact]
    public void A_scene_is_a_site_directory_a_scene_directory_and_a_file()
    {
        var path = JellyfinPaths.For(AScene(), ".mkv");

        Assert.Equal("Example Studio", path.SiteDirectory);
        Assert.Equal("Example Studio - 2025-11-03 - Scene Title", path.SceneDirectory);
        Assert.Equal("Example Studio - 2025-11-03 - Scene Title.mkv", path.VideoFileName);
        Assert.Equal(
            "/library/Example Studio/Example Studio - 2025-11-03 - Scene Title/movie.nfo",
            path.SidecarUnder("/library"));
    }

    /// <summary>
    /// ADR 0019: the segment goes, separator and all, and nothing takes its
    /// place. The one shape that must not appear is the doubled separator a
    /// naive format string produces.
    /// </summary>
    [Fact]
    public void A_scene_with_no_date_drops_the_segment()
    {
        var path = JellyfinPaths.For(new Scene(SceneId, "Example Studio", "Scene Title"), ".mkv");

        Assert.Equal("Example Studio - Scene Title", path.SceneDirectory);
        Assert.Equal("Example Studio - Scene Title.mkv", path.VideoFileName);
        Assert.DoesNotContain(" -  - ", path.SceneDirectory, StringComparison.Ordinal);
    }

    /// <summary>
    /// The invariant everything else is arranged around — section 6 of the layout
    /// document. A file name that does not begin with its directory name, to the
    /// character, is a second entry rather than a second version.
    /// </summary>
    [Theory]
    [InlineData("Scene Title")]
    [InlineData("A/B: The \"Sequel\" | Part 2 *")]
    [InlineData("日本語のタイトルがとても長い場合でもこの規則は変わらない")]
    [InlineData("Ünïcödé — em dash 🎬")]
    [InlineData("////")]
    public void The_file_name_begins_with_the_directory_name(string title)
    {
        foreach (var path in new[]
        {
            JellyfinPaths.For(AScene(title), ".mkv"),
            JellyfinPaths.For(AScene(title), ".mkv", distinguish: true),
            JellyfinPaths.For(new Scene(SceneId, "Example Studio", title), ".mpeg"),
            JellyfinPaths.For(AScene(title, site: new string('S', 300)), ".mkv"),
            JellyfinPaths.For(AScene(new string('T', 400)), ".mkv"),
        })
        {
            Assert.StartsWith(path.SceneDirectory, path.VideoFileName, StringComparison.Ordinal);
            Assert.StartsWith(path.SceneDirectory, path.VideoFileNameFor("2160p"), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Section 8: on the SMB share these are accepted and not stored as written,
    /// so a library filed with them in it reads differently from another client —
    /// and the same share mounted without mapposix rejects them outright.
    /// </summary>
    [Fact]
    public void The_reserved_set_never_reaches_the_disk()
    {
        var path = JellyfinPaths.For(AScene("A/B: \"C\" <D> | E ? F * G \\ H"), ".mkv");

        Assert.DoesNotContain(path.SceneDirectory, character => "<>:\"/\\|?*".Contains(character));
        Assert.Equal("Example Studio - 2025-11-03 - A B C D E F G H", path.SceneDirectory);
    }

    /// <summary>
    /// A control character is not something a title has any business carrying,
    /// and a name starting with a dot is hidden from Jellyfin, from a file
    /// manager, and from this tool's own walk over the library.
    /// </summary>
    [Theory]
    [InlineData("Tab\there", "Example Studio - 2025-11-03 - Tab here")]
    [InlineData(".hidden", "Example Studio - 2025-11-03 - hidden")]
    [InlineData("Trailing space  ", "Example Studio - 2025-11-03 - Trailing space")]
    [InlineData("Trailing period.", "Example Studio - 2025-11-03 - Trailing period")]
    public void What_a_filesystem_will_not_carry_is_taken_out(string title, string expected) =>
        Assert.Equal(expected, JellyfinPaths.For(AScene(title), ".mkv").SceneDirectory);

    /// <summary>
    /// A title that was nothing but reserved characters sanitises to nothing.
    /// An empty path component is worse than an ugly one, so the scene id stands
    /// in for it.
    /// </summary>
    [Fact]
    public void A_title_that_sanitises_away_falls_back_to_the_scene_id()
    {
        var path = JellyfinPaths.For(AScene("///"), ".mkv");

        Assert.Equal($"Example Studio - 2025-11-03 - {SceneId:d}", path.SceneDirectory);
    }

    /// <summary>
    /// Section 8 again: the limit is 255 bytes and not 255 characters — 85 CJK
    /// characters fit and 86 do not — and a scene directory has to leave room for
    /// the longest thing derived from it.
    /// </summary>
    [Fact]
    public void No_component_exceeds_its_budget()
    {
        var path = JellyfinPaths.For(
            new Scene(SceneId, new string('サ', 200), string.Concat(Enumerable.Repeat("長い題名", 100))),
            ".mpeg");

        Assert.True(Encoding.UTF8.GetByteCount(path.SiteDirectory) <= LibraryNames.ComponentBudgetBytes);
        Assert.True(
            Encoding.UTF8.GetByteCount(path.SceneDirectory) <= LibraryNames.SceneDirectoryBudgetBytes);
        Assert.True(
            Encoding.UTF8.GetByteCount(path.VideoFileNameFor("2160p")) <= LibraryNames.ComponentBudgetBytes);
    }

    /// <summary>
    /// Truncation must not split a UTF-8 sequence. A component cut mid-character
    /// is not merely ugly: it is a name some of these filesystems refuse, and one
    /// no client renders.
    /// </summary>
    [Fact]
    public void Truncation_of_multi_byte_characters_produces_valid_utf8()
    {
        var path = JellyfinPaths.For(AScene(string.Concat(Enumerable.Repeat("日本語", 200))), ".mkv");

        var bytes = Encoding.UTF8.GetBytes(path.SceneDirectory);
        var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        Assert.Equal(path.SceneDirectory, strict.GetString(bytes));
        Assert.DoesNotContain('�', path.SceneDirectory);
    }

    [Theory]
    [InlineData("Ends in a hyphen -")]
    [InlineData("Ends in a period.")]
    [InlineData("Ends in a space ")]
    public void Nothing_ends_in_a_space_a_period_or_a_dangling_separator(string title)
    {
        var path = JellyfinPaths.For(AScene(title), ".mkv");

        Assert.DoesNotContain(path.SceneDirectory[^1], " .-_");
        Assert.DoesNotContain(path.SiteDirectory[^1], " .-_");
    }

    /// <summary>
    /// The trap the fixed reserve exists for. The budget a directory name is cut
    /// to must not depend on the file being filed, or the 2160p arriving as .mkv
    /// and the 1080p arriving as .mpeg would land in two directories — and two
    /// directories is two entries in the library.
    /// </summary>
    [Fact]
    public void The_directory_name_does_not_depend_on_the_extension()
    {
        var scene = AScene(string.Concat(Enumerable.Repeat("long title ", 40)));

        Assert.Equal(
            JellyfinPaths.For(scene, ".mkv").SceneDirectory,
            JellyfinPaths.For(scene, ".mpeg").SceneDirectory);
    }

    /// <summary>
    /// Section 6: the bracketed form is what groups two qualities into one entry.
    /// Without it the resolution token is stripped from the display name and the
    /// library shows two items with identical names.
    /// </summary>
    [Fact]
    public void A_second_quality_is_a_bracketed_label_on_the_same_name()
    {
        var path = JellyfinPaths.For(AScene(), ".mkv");

        Assert.Equal("Example Studio - 2025-11-03 - Scene Title - [2160p].mkv", path.VideoFileNameFor("2160p"));
        Assert.Equal(path.VideoFileName, path.VideoFileNameFor(null));
    }

    [Theory]
    [InlineData("video.MKV", ".mkv")]
    [InlineData("video.mp4", ".mp4")]
    [InlineData("video", "")]
    public void The_extension_comes_from_the_file_and_is_lowercased(string fileName, string expected) =>
        Assert.Equal(expected, JellyfinPaths.For(AScene(), Path.GetExtension(fileName)).Extension);

    /// <summary>
    /// ADR 0019 does not file a video prdb cannot name, so arriving here without
    /// a title is a caller that skipped the question — not a scene to be given
    /// some name anyway.
    /// </summary>
    [Theory]
    [InlineData("", "Title")]
    [InlineData("  ", "Title")]
    [InlineData("Site", "")]
    public void A_scene_with_no_site_or_no_title_is_not_named_at_all(string site, string title) =>
        Assert.Throws<ArgumentException>(() =>
            JellyfinPaths.For(new Scene(SceneId, site, title), ".mkv"));
}

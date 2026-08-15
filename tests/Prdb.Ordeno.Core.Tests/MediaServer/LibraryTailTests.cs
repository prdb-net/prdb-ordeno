using Prdb.Ordeno.Core.MediaServer;

using Xunit;

namespace Prdb.Ordeno.Core.Tests.MediaServer;

/// <summary>
/// The route from a path this tool wrote to an item the media server holds.
/// Section 9 of the layout document is why it is a tail match and not one of
/// the two things that look easier: the server ignores a path filter and answers
/// with the whole library, and it indexes a name that comes out of the sidecar
/// rather than off the disk.
/// </summary>
public sealed class LibraryTailTests
{
    private const string Root = "/library";

    private const string Filed = "/library/Example Studio/Example Studio - 2025-11-15 - A Scene/"
        + "Example Studio - 2025-11-15 - A Scene.mkv";

    private const string Tail = "/Example Studio/Example Studio - 2025-11-15 - A Scene/"
        + "Example Studio - 2025-11-15 - A Scene.mkv";

    [Fact]
    public void The_tail_is_everything_below_the_library_root() =>
        Assert.Equal(Tail, LibraryTail.Of(Root, Filed));

    [Fact]
    public void A_trailing_separator_on_the_root_changes_nothing() =>
        Assert.Equal(Tail, LibraryTail.Of("/library/", Filed));

    /// <summary>
    /// Nothing to go looking for, rather than something to guess a tail from. A
    /// guess here refreshes an item that has nothing to do with the file.
    /// </summary>
    [Theory]
    [InlineData("/elsewhere/Site/Scene/Scene.mkv")]
    [InlineData("/librarytoo/Site/Scene/Scene.mkv")]
    [InlineData("/library")]
    public void A_path_that_is_not_below_the_root_has_no_tail(string path) =>
        Assert.Null(LibraryTail.Of(Root, path));

    /// <summary>
    /// The match that the mount prefixes make necessary: the tool wrote to
    /// <c>/library/...</c> and the server reports <c>/media/movies/...</c>, and
    /// nothing in either configuration mentions the other.
    /// </summary>
    [Fact]
    public void An_item_is_found_by_its_tail_whatever_the_server_calls_the_library()
    {
        var items = new[]
        {
            new MediaServerItem("other", "/media/movies/Another Studio/Another/Another.mkv"),
            new MediaServerItem("wanted", "/media/movies" + Tail),
        };

        var found = LibraryTail.Match(items, LibraryTail.Of(Root, Filed)!);

        Assert.Equal("wanted", Assert.Single(found).Id);
    }

    /// <summary>
    /// And the match is the receipt: what is left in front of it is the
    /// substitution nobody configured and nothing else would ever say out loud.
    /// </summary>
    [Fact]
    public void What_is_left_in_front_of_the_match_is_the_substitution()
    {
        var item = new MediaServerItem("wanted", "/media/movies" + Tail);

        Assert.Equal("/media/movies", LibraryTail.Substitution(item, Tail));
    }

    /// <summary>
    /// The reason a tail carries its leading separator. Without one, a scene
    /// directory that ends the same way as another site's would match, and the
    /// item refreshed would be somebody else's scene.
    /// </summary>
    [Fact]
    public void A_tail_only_matches_on_a_directory_boundary()
    {
        var items = new[]
        {
            new MediaServerItem("nearly", "/media/Not Example Studio/Example Studio - 2025-11-15 - A Scene/"
                + "Example Studio - 2025-11-15 - A Scene.mkv"),
        };

        Assert.Empty(LibraryTail.Match(items, Tail));
    }

    /// <summary>
    /// One directory can sit in two libraries, and then two items show the
    /// sidecar that was just rewritten. Refreshing one of them would leave the
    /// other showing what the file used to say.
    /// </summary>
    [Fact]
    public void One_directory_in_two_libraries_is_two_items_to_refresh()
    {
        var items = new[]
        {
            new MediaServerItem("first", "/media/movies" + Tail),
            new MediaServerItem("second", "/other/mount" + Tail),
        };

        Assert.Equal(2, LibraryTail.Match(items, Tail).Count);
    }
}

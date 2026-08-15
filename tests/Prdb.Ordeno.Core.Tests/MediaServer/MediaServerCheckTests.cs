using Prdb.Ordeno.Core.MediaServer;

using Xunit;

namespace Prdb.Ordeno.Core.Tests.MediaServer;

/// <summary>
/// The connection test's verdict. ADR 0018 asks it to prove three things rather
/// than one, and two of the three failures are silent everywhere else: a release
/// date format that discards every date the tool writes, and a server that
/// answers and holds none of what the tool filed. Both are sentences here
/// because neither is an error anywhere.
/// </summary>
public sealed class MediaServerCheckTests
{
    private static readonly MediaServerFacts Jellyfin = new("Jellyfin Server", "10.11.11", "yyyy-MM-dd");

    [Fact]
    public void A_server_that_holds_something_the_tool_filed_has_proved_the_whole_thing()
    {
        var check = MediaServerCheck.Of(
            Jellyfin,
            new MediaServerMatch(58, 12, "A Scene.mkv", "/media/movies"),
            "/library");

        Assert.Equal(MediaServerCheckStatus.Working, check.Status);
        Assert.True(check.Working);
        Assert.True(check.Answered);
        Assert.Contains("Jellyfin Server 10.11.11", check.Message, StringComparison.Ordinal);
        Assert.Contains("A Scene.mkv", check.Message, StringComparison.Ordinal);

        // The substitution, said out loud once. Nobody configured it and nothing
        // in either product will ever mention it.
        Assert.Contains("/library here is /media/movies there", check.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The state that looks fine and does nothing. The server answers, the key
    /// works, and every sidecar this tool writes is invisible to it.
    /// </summary>
    [Fact]
    public void A_server_that_holds_none_of_what_was_filed_says_so_out_loud()
    {
        var check = MediaServerCheck.Of(Jellyfin, new MediaServerMatch(58, 12), "/library");

        Assert.Equal(MediaServerCheckStatus.Unmatched, check.Status);
        Assert.False(check.Working);

        // Still worth storing: what it says is something the user can act on.
        Assert.True(check.Answered);
        Assert.Contains("none of them", check.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// During setup nothing has been filed, so there is nothing to look for. That
    /// is not the state above and must not read like it — ADR 0018 is explicit
    /// that a blank or unproven connection is not a broken one.
    /// </summary>
    [Fact]
    public void Nothing_filed_yet_is_not_a_complaint()
    {
        var check = MediaServerCheck.Of(Jellyfin, new MediaServerMatch(0, 0), "/library");

        Assert.Equal(MediaServerCheckStatus.Unproven, check.Status);
        Assert.True(check.Answered);
        Assert.Contains("Nothing has been filed yet", check.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The one thing the connection exists for. A user who changed this setting
    /// gets a library with no dates and no production years out of entirely
    /// correct sidecars, and neither side reports anything.
    /// </summary>
    [Fact]
    public void A_release_date_format_that_discards_every_date_outranks_everything_else()
    {
        var check = MediaServerCheck.Of(
            Jellyfin with { ReleaseDateFormat = "dd.MM.yyyy" },
            new MediaServerMatch(58, 12, "A Scene.mkv", "/media/movies"),
            "/library");

        Assert.Equal(MediaServerCheckStatus.DatesDiscarded, check.Status);
        Assert.False(check.Working);
        Assert.Contains("'dd.MM.yyyy'", check.Message, StringComparison.Ordinal);
        Assert.Contains("yyyy-MM-dd", check.Message, StringComparison.Ordinal);

        // And the rest is still reported: the user is told what does work in the
        // same breath as what does not.
        Assert.Contains("A Scene.mkv", check.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The setting could not be read. Saying nothing would be claiming it is
    /// right, which is the failure this whole check exists to prevent.
    /// </summary>
    [Fact]
    public void A_format_that_could_not_be_read_is_neither_claimed_nor_condemned()
    {
        var check = MediaServerCheck.Of(
            Jellyfin with { ReleaseDateFormat = null },
            new MediaServerMatch(58, 12, "A Scene.mkv", "/media/movies"),
            "/library");

        Assert.Equal(MediaServerCheckStatus.Working, check.Status);
        Assert.Contains("could not be read", check.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A key the server refused and a server that never answered are the two
    /// results nothing is stored for: the first is wrong, and the second is
    /// unknown. Storing either would be the tool claiming something it cannot.
    /// </summary>
    [Fact]
    public void Nothing_is_stored_for_a_refusal_or_a_silence()
    {
        Assert.False(MediaServerCheck.Refused("no").Answered);
        Assert.False(MediaServerCheck.Unreachable("nothing there").Answered);
    }
}

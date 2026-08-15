using Prdb.Ordeno.Core.MediaServer;

using Xunit;

namespace Prdb.Ordeno.Core.Tests.MediaServer;

/// <summary>
/// The two optional fields, read. Everything here is about what somebody
/// actually types into them, and about the one thing that must never come back
/// out.
/// </summary>
public sealed class MediaServerConnectionTests
{
    private const string Key = "0123456789abcdef";

    /// <summary>
    /// A server on somebody's own network is typed as a host and a port. The
    /// scheme is the one part the tool can supply itself, so refusing over it
    /// would be pedantry rather than a check.
    /// </summary>
    [Fact]
    public void An_address_without_a_scheme_is_read_as_http()
    {
        var connection = MediaServerConnection.From("192.168.1.10:8096", Key, out var problem);

        Assert.Null(problem);
        Assert.Equal("http://192.168.1.10:8096/", connection!.Address);
    }

    [Fact]
    public void An_address_behind_a_proxy_keeps_the_path_its_endpoints_hang_off()
    {
        var connection = MediaServerConnection.From("https://home.example/jellyfin", Key, out _);

        Assert.Equal("https://home.example/jellyfin/", connection!.Address);
        Assert.Equal(
            "https://home.example/jellyfin/System/Info",
            connection.Endpoint("System/Info").ToString());
    }

    /// <summary>
    /// A rooted endpoint would throw the proxy's path away and ask a server that
    /// is not there, which answers 404 and reads as "wrong key" to somebody who
    /// typed a perfectly good one.
    /// </summary>
    [Fact]
    public void An_endpoint_never_escapes_the_path_the_address_carries()
    {
        var connection = MediaServerConnection.From("https://home.example/jellyfin/", Key, out _);

        Assert.Equal(
            "https://home.example/jellyfin/Items?recursive=true",
            connection!.Endpoint("/Items?recursive=true").ToString());
    }

    /// <summary>
    /// A query string and a fragment are dropped: what is stored is a base to
    /// hang endpoints off, and a tab parameter somebody pasted along with the
    /// address would otherwise end up in the middle of every request. The path is
    /// kept, because a path is what a proxy puts a server behind — the tool
    /// cannot tell one of those from a page, so a pasted page address is left to
    /// fail at the connection test, which says so in words.
    /// </summary>
    [Fact]
    public void What_follows_the_path_is_dropped_and_the_path_is_not()
    {
        var connection = MediaServerConnection.From(
            "http://nas:8096/jellyfin?start=1#!/home.html",
            Key,
            out _);

        Assert.Equal("http://nas:8096/jellyfin/", connection!.Address);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void No_address_is_no_connection_and_says_so_without_alarm(string? url)
    {
        Assert.Null(MediaServerConnection.From(url, Key, out var problem));
        Assert.Contains("leave both fields empty", problem!, StringComparison.Ordinal);
    }

    /// <summary>
    /// An address with no key reaches a server that will not answer, which is a
    /// worse thing to store than nothing at all.
    /// </summary>
    [Fact]
    public void An_address_without_a_key_is_refused()
    {
        Assert.Null(MediaServerConnection.From("http://nas:8096", " ", out var problem));
        Assert.Contains("API key", problem!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ftp://nas:8096")]
    [InlineData("this is not a url")]
    [InlineData("file:///library")]
    public void Something_that_is_not_an_address_is_refused_in_words(string url)
    {
        Assert.Null(MediaServerConnection.From(url, Key, out var problem));
        Assert.Contains("http://host:8096", problem!, StringComparison.Ordinal);
    }

    /// <summary>
    /// A record prints all of itself, and this one holds a credential. Anything
    /// that interpolates a connection into a log line would otherwise write the
    /// key into the container's log, which ADR 0009 says never happens.
    /// </summary>
    [Fact]
    public void The_key_is_not_in_what_a_log_line_would_print()
    {
        var connection = MediaServerConnection.From("http://nas:8096", Key, out _);

        Assert.DoesNotContain(Key, $"{connection}", StringComparison.Ordinal);
        Assert.Equal(Key, connection!.ApiKey);
    }
}

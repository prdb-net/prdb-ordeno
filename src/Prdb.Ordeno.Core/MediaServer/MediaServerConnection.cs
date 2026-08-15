namespace Prdb.Ordeno.Core.MediaServer;

/// <summary>
/// Where the media server is and the key that gets in — the two optional fields
/// ADR 0018 adds to onboarding, once they have been read back out of it.
/// </summary>
/// <remarks>
/// <para>
/// There is no third field, and its absence is the decision rather than an
/// omission: the path substitution between this container's mounts and the
/// server's is derived from a match (see <see cref="LibraryTail"/>), because a
/// setting whose only correct value can be computed is a setting that will be
/// wrong on somebody's machine.
/// </para>
/// <para>
/// Blank is the ordinary state. Nothing in the tool may require one of these to
/// exist, so every caller that holds one holds it as something that may be
/// <c>null</c>.
/// </para>
/// </remarks>
public sealed record MediaServerConnection(Uri BaseAddress, string ApiKey)
{
    /// <summary>
    /// The pair the user typed, or <c>null</c> and the reason it is not an
    /// address. Both fields are wanted together: a URL with no key reaches a
    /// server that will not answer, which is a worse thing to store than nothing.
    /// </summary>
    /// <param name="problem">
    /// What to put next to the field. <c>null</c> when a connection came back.
    /// </param>
    public static MediaServerConnection? From(string? url, string? apiKey, out string? problem)
    {
        var typed = url?.Trim() ?? string.Empty;
        var key = apiKey?.Trim() ?? string.Empty;

        if (typed.Length == 0)
        {
            problem = "Enter the address of your media server, for example http://192.168.1.10:8096 "
                + "— or leave both fields empty, which is what most installations do.";

            return null;
        }

        if (key.Length == 0)
        {
            problem = "Enter an API key as well. In Jellyfin it is made under Dashboard → API keys, "
                + "and it needs no user name and no password.";

            return null;
        }

        // Somebody typing the address of a server on their own network types a
        // host and a port. Refusing that over a missing scheme would be
        // pedantry about the one part the tool can supply itself, and http is
        // what a Jellyfin on a LAN answers on.
        var written = typed.Contains("://", StringComparison.Ordinal) ? typed : "http://" + typed;

        if (!Uri.TryCreate(written, UriKind.Absolute, out var address)
            || (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(address.Host))
        {
            problem = $"'{typed}' is not an address the tool can reach. It looks like "
                + "http://host:8096, or https://host/jellyfin behind a proxy.";

            return null;
        }

        problem = null;

        // Everything after the path is dropped and a trailing slash is put on:
        // what is stored is a base to hang endpoints off, and a query string
        // somebody pasted along with the address would end up in the middle of
        // every request.
        var settled = new Uri(address.GetLeftPart(UriPartial.Path));

        return new MediaServerConnection(
            settled.AbsolutePath.EndsWith('/') ? settled : new Uri(settled.OriginalString + "/"),
            key);
    }

    /// <summary>
    /// One endpoint on this server. Relative and never rooted, so a server behind
    /// a proxy at <c>/jellyfin</c> keeps its prefix instead of losing it to a
    /// leading slash.
    /// </summary>
    public Uri Endpoint(string relative)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relative);

        return new Uri(BaseAddress, relative.TrimStart('/'));
    }

    /// <summary>The address alone, which is the half that may be shown and stored in the open.</summary>
    public string Address => BaseAddress.ToString();

    /// <summary>
    /// The key is a credential, and a record prints all of itself. Anything that
    /// interpolates one of these into a log line would otherwise write the key
    /// into the container's log — which ADR 0009 says never happens.
    /// </summary>
    public override string ToString() => Address;
}

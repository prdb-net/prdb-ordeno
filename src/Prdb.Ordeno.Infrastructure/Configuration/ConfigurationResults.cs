using Prdb.Ordeno.Core.Configuration;
using Prdb.Ordeno.Core.MediaServer;

namespace Prdb.Ordeno.Infrastructure.Configuration;

/// <summary>
/// Every write to the configuration answers with the configuration as it now
/// stands, plus what went wrong if anything did. The caller does not have to ask
/// again to find out what the screen should say, and the two can never disagree.
/// </summary>
/// <param name="Message">
/// Why the change was refused, in words meant for the person who made it.
/// <c>null</c> when it was accepted.
/// </param>
public sealed record ConfigurationChange(OrdenoConfiguration Configuration, string? Message)
{
    public bool Accepted => Message is null;

    public static ConfigurationChange Made(OrdenoConfiguration configuration) => new(configuration, null);

    public static ConfigurationChange Refused(OrdenoConfiguration configuration, string message) =>
        new(configuration, message);
}

/// <summary>
/// What came of setting or testing the optional media server connection.
/// </summary>
/// <remarks>
/// It carries more than a refusal because this is the one place a media server
/// fails out loud (ADR 0018), and because a connection can be perfectly usable
/// and still worth a sentence: a date format that discards every date, or a
/// server that answers and holds nothing this tool filed.
/// </remarks>
/// <param name="Check">
/// What the server said about itself. <c>null</c> when nothing was asked,
/// because what was typed was not an address or there is nothing configured.
/// </param>
/// <param name="Message">
/// Why nothing was stored, in words for the person who typed it. <c>null</c>
/// when the connection was kept — including when <paramref name="Check"/> has
/// something to complain about, which is a working connection with a problem
/// behind it rather than a refused change.
/// </param>
public sealed record MediaServerChange(
    OrdenoConfiguration Configuration,
    MediaServerCheck? Check,
    string? Message)
{
    public bool Accepted => Message is null;

    public static MediaServerChange Made(OrdenoConfiguration configuration, MediaServerCheck check) =>
        new(configuration, check, null);

    public static MediaServerChange Refused(
        OrdenoConfiguration configuration,
        string message,
        MediaServerCheck? check = null) =>
        new(configuration, check, message);
}

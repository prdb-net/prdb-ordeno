using Prdb.Ordeno.Core.Configuration;

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

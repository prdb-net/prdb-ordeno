using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Prdb.Ordeno.Core.MediaServer;

namespace Prdb.Ordeno.Infrastructure.MediaServer;

public static class MediaServerServiceCollectionExtensions
{
    /// <summary>
    /// The optional connection of ADR 0018. It is registered whether or not one
    /// is configured: what decides that is a row in the database, not the
    /// composition, so that nothing here has to be re-wired when a user fills the
    /// two fields in.
    /// </summary>
    public static IServiceCollection AddOrdenoMediaServer(this IServiceCollection services)
    {
        services.AddMediaServerTransport();

        // One layout ships, so one client does (ADR 0008). A second media server
        // is a second implementation picked by the configured layout, and it
        // arrives with measurements rather than with a guess at its API.
        services.TryAddSingleton<IMediaServerClient, JellyfinClient>();

        services.AddScoped<MediaServerService>();

        return services;
    }
}

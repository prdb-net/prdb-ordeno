using Microsoft.Extensions.DependencyInjection;

namespace Prdb.Ordeno.Infrastructure.MediaServer;

/// <summary>
/// The connection every request to the media server goes through.
/// </summary>
/// <remarks>
/// Its one setting is the interesting one: redirects are not followed. The API
/// key travels in <c>Authorization</c>, and every HTTP stack that strips that
/// header on a cross-origin redirect strips it on the basis of a scheme this one
/// is not — so a redirect is reported to the user as a redirect, with the
/// address it points at, rather than followed with the key attached.
/// </remarks>
public static class MediaServerTransport
{
    public const string HttpClientName = "media-server";

    public static IServiceCollection AddMediaServerTransport(this IServiceCollection services)
    {
        services
            .AddHttpClient(HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });

        return services;
    }
}

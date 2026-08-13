using Microsoft.Extensions.DependencyInjection;

namespace Prdb.Ordeno.Infrastructure.Configuration;

/// <summary>
/// The connection every SDK client in this application sends through.
/// </summary>
/// <remarks>
/// Each caller builds its own <c>PrdbClient</c>, because the API key belongs to
/// the request rather than to the application and can change while the container
/// runs. The transport underneath is shared and pooled here, so a key check and
/// an identification run do not each open their own socket to the same host.
/// </remarks>
public static class PrdbTransport
{
    /// <summary>The named client whose handler the SDK sends through.</summary>
    public const string HttpClientName = "prdb";

    public static IServiceCollection AddPrdbTransport(this IServiceCollection services)
    {
        services
            .AddHttpClient(HttpClientName)
            // The default primary handler follows redirects, and the SDK refuses
            // to build on one that does: a redirect it never sees is a redirect
            // whose cross-origin rule never runs, and nothing below strips
            // X-Api-Key.
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });

        return services;
    }
}

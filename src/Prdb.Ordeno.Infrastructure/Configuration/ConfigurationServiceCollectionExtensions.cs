using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Prdb.Ordeno.Core.Configuration;

namespace Prdb.Ordeno.Infrastructure.Configuration;

public static class ConfigurationServiceCollectionExtensions
{
    public static IServiceCollection AddOrdenoConfiguration(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IDirectoryInspector, DirectoryInspector>();
        services.TryAddSingleton<IPrdbApiKeyCheck, PrdbApiKeyCheck>();
        services.AddScoped<ConfigurationService>();

        // The connections the SDK's clients send through. A key check builds its
        // own client — the key belongs to the request, not to the application —
        // but the transport underneath is pooled and rotated here, so checking a
        // key repeatedly does not open a socket every time.
        services
            .AddHttpClient(PrdbApiKeyCheck.HttpClientName)
            // The default primary handler follows redirects, and the SDK refuses
            // to build on one that does: a redirect it never sees is a redirect
            // whose cross-origin rule never runs, and nothing below strips
            // X-Api-Key.
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });

        return services;
    }
}

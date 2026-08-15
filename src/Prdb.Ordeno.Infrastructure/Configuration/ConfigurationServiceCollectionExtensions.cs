using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Prdb.Ordeno.Core.Configuration;
using Prdb.Ordeno.Infrastructure.MediaServer;

namespace Prdb.Ordeno.Infrastructure.Configuration;

public static class ConfigurationServiceCollectionExtensions
{
    public static IServiceCollection AddOrdenoConfiguration(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IDirectoryInspector, DirectoryInspector>();
        services.TryAddSingleton<IPrdbApiKeyCheck, PrdbApiKeyCheck>();
        services.AddScoped<ConfigurationService>();

        services.AddPrdbTransport();

        // Onboarding collects the optional media server connection too, and the
        // field is checked before it is stored like every other one.
        services.AddOrdenoMediaServer();

        return services;
    }
}

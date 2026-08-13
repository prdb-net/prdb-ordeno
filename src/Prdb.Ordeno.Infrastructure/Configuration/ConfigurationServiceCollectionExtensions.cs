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

        services.AddPrdbTransport();

        return services;
    }
}

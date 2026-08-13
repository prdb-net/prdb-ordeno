using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Prdb.Ordeno.Core.Identification;
using Prdb.Ordeno.Infrastructure.Configuration;

namespace Prdb.Ordeno.Infrastructure.Identification;

public static class IdentificationServiceCollectionExtensions
{
    public static IServiceCollection AddOrdenoIdentification(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        // The same connection the key check sends through: one host, one handler
        // pool, and one place where a redirect off it is refused. Registering it
        // twice is registering it once — which is what makes this slice usable
        // without the configuration one.
        services.AddPrdbTransport();

        services.TryAddSingleton<IFileHashes, OsHashes>();
        services.TryAddSingleton<IPerceptualHashes, PerceptualHashes>();
        services.TryAddSingleton<IVideoIdentification, PrdbVideoIdentification>();

        services.AddScoped<IdentificationService>();
        services.AddScoped<PerceptualHashService>();

        // Singleton, because what it holds is the fact that a run is under way,
        // and how long prdb asked to be left alone.
        services.AddSingleton<IdentificationRunner>();

        return services;
    }
}

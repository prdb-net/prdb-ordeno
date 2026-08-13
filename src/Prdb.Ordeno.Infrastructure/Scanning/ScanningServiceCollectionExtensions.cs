using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Prdb.Ordeno.Core.Configuration;
using Prdb.Ordeno.Core.Scanning;
using Prdb.Ordeno.Infrastructure.Configuration;

namespace Prdb.Ordeno.Infrastructure.Scanning;

public static class ScanningServiceCollectionExtensions
{
    public static IServiceCollection AddOrdenoScanning(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ISourceWalker, SourceWalker>();

        // The same check the configuration screen makes, for the same reason: a
        // directory is usable now or it is not, whatever it was when it was
        // added. TryAdd, because onboarding registers it too.
        services.TryAddSingleton<IDirectoryInspector, DirectoryInspector>();
        services.AddScoped<ScanService>();

        // Singleton, because what it holds is the fact that a scan is running.
        // One per request would be one gate per request, which is no gate at all.
        services.AddSingleton<ScanRunner>();

        return services;
    }
}

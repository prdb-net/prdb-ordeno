using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Prdb.Ordeno.Core.Library;

namespace Prdb.Ordeno.Infrastructure.Library;

public static class LibraryServiceCollectionExtensions
{
    /// <summary>
    /// The path computation, the two things it asks about a filesystem and a
    /// file, and nothing that writes. Filing is composed from these; a sidecar
    /// (#18) will be too.
    /// </summary>
    public static IServiceCollection AddOrdenoLibrary(this IServiceCollection services)
    {
        services.TryAddSingleton<ISceneDirectories, SceneDirectories>();
        services.TryAddSingleton<IVideoQualities, VideoQualities>();
        services.TryAddSingleton<TargetPaths>();

        return services;
    }
}

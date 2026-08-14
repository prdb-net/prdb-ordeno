using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Prdb.Ordeno.Core.Library;

namespace Prdb.Ordeno.Infrastructure.Library;

public static class LibraryServiceCollectionExtensions
{
    /// <summary>
    /// The path computation and the one thing it asks the filesystem. Nothing
    /// here moves a file or writes a sidecar — that is #17 and #18, and this is
    /// what both of them will be given.
    /// </summary>
    public static IServiceCollection AddOrdenoLibrary(this IServiceCollection services)
    {
        services.TryAddSingleton<ISceneDirectories, SceneDirectories>();
        services.TryAddSingleton<TargetPaths>();

        return services;
    }
}

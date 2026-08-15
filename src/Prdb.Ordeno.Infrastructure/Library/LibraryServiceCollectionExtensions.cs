using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Prdb.Ordeno.Core.Library;

namespace Prdb.Ordeno.Infrastructure.Library;

public static class LibraryServiceCollectionExtensions
{
    /// <summary>
    /// Filing: what would happen, and what carries it out. A sidecar (#18) will
    /// be composed from the same pieces.
    /// </summary>
    /// <remarks>
    /// The runner is a singleton because it is the gate — one run at a time, and
    /// a status the screen reads while it works. The service is scoped, like
    /// everything that holds a database context.
    /// </remarks>
    public static IServiceCollection AddOrdenoLibrary(this IServiceCollection services)
    {
        services.TryAddSingleton<ISceneDirectories, SceneDirectories>();
        services.TryAddSingleton<IVideoQualities, VideoQualities>();
        services.TryAddSingleton<TargetPaths>();
        services.TryAddSingleton<FilingPlanner>();
        services.TryAddSingleton<LibraryMoves>();
        services.TryAddSingleton<FilingRunner>();
        services.TryAddScoped<FilingService>();

        return services;
    }
}

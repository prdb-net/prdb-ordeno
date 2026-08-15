using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Prdb.Ordeno.Core.Library;
using Prdb.Ordeno.Core.Review;
using Prdb.Ordeno.Infrastructure.Configuration;
using Prdb.Ordeno.Infrastructure.Review;

namespace Prdb.Ordeno.Infrastructure.Library;

public static class LibraryServiceCollectionExtensions
{
    /// <summary>
    /// Filing: what would happen, what carries it out, and the sidecar that goes
    /// in next to what moved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The runner is a singleton because it is the gate — one run at a time, and
    /// a status the screen reads while it works. The service is scoped, like
    /// everything that holds a database context.
    /// </para>
    /// <para>
    /// Filing asks prdb what it is filing, so this slice needs
    /// <see cref="IVideoLookup"/> — the review queue's own registration, and
    /// deliberately not a second client. Both are the one transport that refuses
    /// a redirect off the prdb host while the key is on the request.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddOrdenoLibrary(this IServiceCollection services)
    {
        // The same connection every other caller of prdb sends through, and
        // registering it twice is registering it once — which is what makes this
        // slice usable without the review one.
        services.AddPrdbTransport();
        services.TryAddSingleton<IVideoLookup, PrdbVideoLookup>();

        services.TryAddSingleton<ISceneDirectories, SceneDirectories>();
        services.TryAddSingleton<IVideoQualities, VideoQualities>();
        services.TryAddSingleton<TargetPaths>();
        services.TryAddSingleton<FilingPlanner>();
        services.TryAddSingleton<LibraryMoves>();

        // One object under both names: the planner asks it what is at a path and
        // the run asks it to write, and a second instance would be a second
        // answer to the same question.
        services.TryAddSingleton<Sidecars>();
        services.TryAddSingleton<ISidecars>(provider => provider.GetRequiredService<Sidecars>());

        services.TryAddSingleton<FilingRunner>();
        services.TryAddScoped<FilingService>();

        return services;
    }
}

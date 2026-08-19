using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Prdb.Ordeno.Infrastructure.Library;

namespace Prdb.Ordeno.Infrastructure.History;

public static class HistoryServiceCollectionExtensions
{
    /// <summary>
    /// The operation log and the way back — ADR 0028 and ADR 0029.
    /// </summary>
    /// <remarks>
    /// The runner is a singleton because it holds the status the screen reads
    /// while an undo works; the services are scoped, like everything that holds a
    /// database context. The gate is registered here as well as by the library
    /// slice, and registering it twice is registering it once — which is the
    /// point: filing and undoing must not be behind two different gates.
    /// </remarks>
    public static IServiceCollection AddOrdenoHistory(this IServiceCollection services)
    {
        // The way back moves files, so it needs everything filing needs to move
        // one — including the seams that decide whose a sidecar and an image are.
        services.AddOrdenoLibrary();

        services.TryAddSingleton<LibraryGate>();
        services.TryAddSingleton<UndoRunner>();

        services.TryAddScoped<OperationLog>();
        services.TryAddScoped<HistoryService>();
        services.TryAddScoped<UndoService>();

        return services;
    }
}

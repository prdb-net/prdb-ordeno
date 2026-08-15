using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Prdb.Ordeno.Core.Review;
using Prdb.Ordeno.Infrastructure.Configuration;

namespace Prdb.Ordeno.Infrastructure.Review;

public static class ReviewServiceCollectionExtensions
{
    public static IServiceCollection AddOrdenoReview(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        // The same connection every other caller of prdb sends through: one host,
        // one handler pool, and one place where a redirect off it is refused.
        services.AddPrdbTransport();

        services.TryAddSingleton<IVideoLookup, PrdbVideoLookup>();

        services.AddScoped<ReviewQueueService>();

        // No runner and no worker, deliberately. Everything in this slice happens
        // because somebody pressed something and is waiting for the answer —
        // there is no run here that outlives its request.
        return services;
    }
}

using Microsoft.AspNetCore.Http.HttpResults;

using Prdb.Ordeno.Infrastructure.Library;

namespace Prdb.Ordeno.Host.Library;

internal static class FilingEndpoints
{
    /// <summary>
    /// Behind the password like everything else, and this group more plainly
    /// than most: one of these moves files the user cannot get back.
    /// </summary>
    public static IEndpointRouteBuilder MapFiling(this IEndpointRouteBuilder endpoints)
    {
        var filing = endpoints.MapGroup("/api/filing").WithTags("Filing");

        // What was last worked out, and what the last run did. Read by the
        // screen while either is under way, since both outlive the request that
        // started them.
        filing.MapGet("/", (FilingRunner runner) => TypedResults.Ok(FilingState.Of(runner.Status)));

        // Works out what would happen and answers as soon as that is under way.
        // It reads the header of every video waiting to be filed, which on a
        // first pass over a library is longer than a request should be held open
        // for.
        filing.MapPost("/plan", (FilingRunner runner, IHostApplicationLifetime lifetime) =>
        {
            runner.TryPlan(lifetime.ApplicationStopping);

            return TypedResults.Ok(FilingState.Of(runner.Status));
        });

        // The one that moves files. It is a POST from a button somebody pressed
        // after reading the plan — ADR 0022 — and there is no timer behind it
        // until there is a way back (#19).
        filing.MapPost("/", (FilingRunner runner, IHostApplicationLifetime lifetime) =>
        {
            // Deliberately not the request's token. A library is minutes of
            // copying and the browser is long gone; what may stop this is the
            // container shutting down, which has to reach the file being copied.
            runner.TryFile(lifetime.ApplicationStopping);

            return TypedResults.Ok(FilingState.Of(runner.Status));
        });

        return endpoints;
    }
}

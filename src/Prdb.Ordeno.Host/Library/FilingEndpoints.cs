using Microsoft.AspNetCore.Http.HttpResults;

using Prdb.Ordeno.Infrastructure.Library;

namespace Prdb.Ordeno.Host.Library;

internal static class FilingEndpoints
{
    /// <summary>
    /// What a request is told when the gate is shut. Filing and the way back
    /// share one (<c>LibraryGate</c>), so this is what somebody pressing a
    /// button while an undo is working gets — a sentence rather than a button
    /// that quietly did nothing.
    /// </summary>
    private const string Busy =
        "Something else is rearranging the library just now. Nothing was started; try again in a "
        + "moment.";


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
            var started = runner.TryPlan(lifetime.ApplicationStopping);

            return TypedResults.Ok(FilingState.Of(runner.Status, started ? null : Busy));
        });

        // The one that moves files. It is a POST from a button somebody pressed
        // after reading the plan — ADR 0022 — and there is still no timer behind
        // it: what it writes is now in the operation log, where a run can be put
        // back (ADR 0029), and what a timer would owe an undone file is the
        // question that decides when one arrives.
        filing.MapPost("/", (FilingRunner runner, IHostApplicationLifetime lifetime) =>
        {
            // Deliberately not the request's token. A library is minutes of
            // copying and the browser is long gone; what may stop this is the
            // container shutting down, which has to reach the file being copied.
            var started = runner.TryFile(lifetime.ApplicationStopping);

            return TypedResults.Ok(FilingState.Of(runner.Status, started ? null : Busy));
        });

        return endpoints;
    }
}

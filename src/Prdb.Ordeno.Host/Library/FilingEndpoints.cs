using Microsoft.AspNetCore.Http.HttpResults;

using Prdb.Ordeno.Core.History;
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
        filing.MapGet("/", async Task<Ok<FilingState>> (
            FilingRunner runner,
            FilingService service,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(FilingState.Of(
                runner.Status,
                await service.UnattendedAsync(cancellationToken))));

        // Works out what would happen and answers as soon as that is under way.
        // It reads the header of every video waiting to be filed, which on a
        // first pass over a library is longer than a request should be held open
        // for.
        filing.MapPost("/plan", async Task<Ok<FilingState>> (
            FilingRunner runner,
            FilingService service,
            IHostApplicationLifetime lifetime,
            CancellationToken cancellationToken) =>
        {
            var started = runner.TryPlan(lifetime.ApplicationStopping);

            return TypedResults.Ok(FilingState.Of(
                runner.Status,
                await service.UnattendedAsync(cancellationToken),
                started ? null : Busy));
        });

        // The one that moves files. It is a POST from a button somebody pressed
        // after reading the plan — ADR 0022 — and the timer (ADR 0031) is the
        // same call with nobody in front of it, which is why the run is told
        // which it was rather than working it out from where it came from.
        filing.MapPost("/", async Task<Ok<FilingState>> (
            FilingRunner runner,
            FilingService service,
            IHostApplicationLifetime lifetime,
            CancellationToken cancellationToken) =>
        {
            // Deliberately not the request's token. A library is minutes of
            // copying and the browser is long gone; what may stop this is the
            // container shutting down, which has to reach the file being copied.
            var started = runner.TryFile(AskedBy.Person, lifetime.ApplicationStopping);

            return TypedResults.Ok(FilingState.Of(
                runner.Status,
                await service.UnattendedAsync(cancellationToken),
                started ? null : Busy));
        });

        // ADR 0030's way out of a hold: one file, or every held file at once,
        // because undoing a run of two hundred is what leaves two hundred holds.
        // Neither moves anything — it makes a file ordinary again, and the plan
        // and the button still stand between it and the library. Which is why
        // this answers inside the request and works out the plan again
        // afterwards: what was on the screen no longer describes what a run
        // would do.
        filing.MapDelete("/holds/{fileId:int}", async Task<Ok<FilingState>> (
            int fileId,
            FilingRunner runner,
            FilingService service,
            IHostApplicationLifetime lifetime,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await ReleaseAsync(fileId, runner, service, lifetime, cancellationToken)));

        filing.MapDelete("/holds", async Task<Ok<FilingState>> (
            FilingRunner runner,
            FilingService service,
            IHostApplicationLifetime lifetime,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await ReleaseAsync(null, runner, service, lifetime, cancellationToken)));

        return endpoints;
    }

    /// <summary>
    /// Takes the hold off a file an undo put back, and works the plan out again.
    /// </summary>
    /// <remarks>
    /// The re-plan is not a courtesy. A released file is one the run would now
    /// move, and leaving the screen showing the plan from before would leave
    /// somebody looking at a preview that is missing the file they just acted on
    /// — which is the one thing ADR 0022 asks of this screen. A gate that is shut
    /// says so instead; the release has already happened either way.
    /// </remarks>
    private static async Task<FilingState> ReleaseAsync(
        int? fileId,
        FilingRunner runner,
        FilingService service,
        IHostApplicationLifetime lifetime,
        CancellationToken cancellationToken)
    {
        await service.ReleaseAsync(fileId, cancellationToken);

        var started = runner.TryPlan(lifetime.ApplicationStopping);

        return FilingState.Of(
            runner.Status,
            await service.UnattendedAsync(cancellationToken),
            started ? null : Busy);
    }
}

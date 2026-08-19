using Microsoft.AspNetCore.Http.HttpResults;

using Prdb.Ordeno.Infrastructure.History;

namespace Prdb.Ordeno.Host.History;

internal static class HistoryEndpoints
{
    /// <summary>
    /// Behind the password like everything else, and this group as plainly as
    /// filing: half of it moves files that a user cannot get back — back.
    /// </summary>
    public static IEndpointRouteBuilder MapHistory(this IEndpointRouteBuilder endpoints)
    {
        var history = endpoints.MapGroup("/api/history").WithTags("History");

        // The log itself, newest first. Paged rather than capped: this is a
        // record somebody scrolls back through until they find the night they
        // are looking for.
        history.MapGet("/", async Task<Ok<HistoryState>> (
            HistoryService service,
            CancellationToken cancellationToken,
            int page = 1) =>
            TypedResults.Ok(HistoryStates.Of(await service.ReadAsync(page, cancellationToken))));

        // What the way back is doing. Read by the screen while a check or an
        // undo is under way, since both outlive the request that started them.
        history.MapGet("/undo", (UndoRunner runner) => TypedResults.Ok(UndoState.Of(runner.Status)));

        // What putting a run back would do, and nothing else. It reads every
        // file the run filed, which on a batch is longer than a request should
        // be held open for, so it answers as soon as the check is under way.
        history.MapPost("/runs/{runId:int}/undo/check", (
            int runId,
            UndoRunner runner,
            IHostApplicationLifetime lifetime) =>
        {
            runner.TryCheck(runId, operationId: null, lifetime.ApplicationStopping);

            return TypedResults.Ok(UndoState.Of(runner.Status));
        });

        // The one that moves files back. A POST from a button somebody pressed
        // after reading the check — the same shape as filing, and the same
        // deliberate choice of token: what may stop this is the container
        // shutting down, not the browser going away.
        history.MapPost("/runs/{runId:int}/undo", (
            int runId,
            UndoRunner runner,
            IHostApplicationLifetime lifetime) =>
        {
            runner.TryUndo(runId, operationId: null, lifetime.ApplicationStopping);

            return TypedResults.Ok(UndoState.Of(runner.Status));
        });

        // And the same pair for one operation — the file somebody is looking at.
        // ADR 0029 has two units and only two.
        history.MapPost("/operations/{operationId:int}/undo/check", (
            int operationId,
            UndoRunner runner,
            IHostApplicationLifetime lifetime) =>
        {
            runner.TryCheck(runId: null, operationId, lifetime.ApplicationStopping);

            return TypedResults.Ok(UndoState.Of(runner.Status));
        });

        history.MapPost("/operations/{operationId:int}/undo", (
            int operationId,
            UndoRunner runner,
            IHostApplicationLifetime lifetime) =>
        {
            runner.TryUndo(runId: null, operationId, lifetime.ApplicationStopping);

            return TypedResults.Ok(UndoState.Of(runner.Status));
        });

        return endpoints;
    }
}

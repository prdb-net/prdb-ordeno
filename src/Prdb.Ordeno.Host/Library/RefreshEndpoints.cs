using Microsoft.AspNetCore.Http.HttpResults;

using Prdb.Ordeno.Core.History;
using Prdb.Ordeno.Infrastructure.Library;

namespace Prdb.Ordeno.Host.Library;

internal static class RefreshEndpoints
{
    /// <summary>
    /// The same sentence filing gives, because it is the same gate: one run at a
    /// time over one library, whether it is filing, undoing or checking.
    /// </summary>
    private const string Busy =
        "Something else is rearranging the library just now. Nothing was started; try again in a "
        + "moment.";

    /// <summary>
    /// What the tool has filed, checked against what prdb says now — ADR 0032.
    /// Two endpoints, because there are only two things to do: read where it
    /// stands, and start a run.
    /// </summary>
    public static IEndpointRouteBuilder MapRefresh(this IEndpointRouteBuilder endpoints)
    {
        var refresh = endpoints.MapGroup("/api/refresh").WithTags("Refresh");

        refresh.MapGet("/", async Task<Ok<RefreshState>> (
            RefreshRunner runner,
            RefreshService service,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(RefreshState.Of(
                runner.Status,
                await service.UnattendedAsync(cancellationToken),
                await service.StandingAsync(cancellationToken))));

        // No plan endpoint next to this one, and that is ADR 0032 rather than an
        // omission: a preview stands between somebody and a move that loses a
        // file, this run moves nothing, and finding out what it would write costs
        // the requests the run itself costs.
        refresh.MapPost("/", async Task<Ok<RefreshState>> (
            RefreshRunner runner,
            RefreshService service,
            IHostApplicationLifetime lifetime,
            CancellationToken cancellationToken) =>
        {
            // Deliberately not the request's token. Walking a library is minutes
            // of reading somebody's NAS and the browser is long gone; what may
            // stop this is the container shutting down.
            var started = runner.TryRefresh(AskedBy.Person, lifetime.ApplicationStopping);

            return TypedResults.Ok(RefreshState.Of(
                runner.Status,
                await service.UnattendedAsync(cancellationToken),
                await service.StandingAsync(cancellationToken),
                started ? null : Busy));
        });

        return endpoints;
    }
}

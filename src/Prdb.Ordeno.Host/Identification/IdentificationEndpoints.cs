using Microsoft.AspNetCore.Http.HttpResults;

using Prdb.Ordeno.Host.Scanning;
using Prdb.Ordeno.Infrastructure.Identification;
using Prdb.Ordeno.Infrastructure.Scanning;

namespace Prdb.Ordeno.Host.Identification;

internal static class IdentificationEndpoints
{
    /// <summary>
    /// Behind the password, like everything else. It spends the user's prdb
    /// quota, which is a second reason.
    /// </summary>
    public static IEndpointRouteBuilder MapIdentification(this IEndpointRouteBuilder endpoints)
    {
        var identification = endpoints.MapGroup("/api/identification").WithTags("Identification");

        // Asks now rather than at the next tick, and answers with the downloads
        // screen's whole state — the same document the screen already polls, so
        // that pressing the button and watching it happen are one thing.
        //
        // There is no GET here for the same reason: what a run has produced is
        // part of what the tool knows about those files, and that lives on
        // /api/scan.
        identification.MapPost("/", async Task<Ok<ScanState>> (
            ScanService scanning,
            PerceptualHashService hashing,
            ScanRunner scan,
            IdentificationRunner runner,
            IHostApplicationLifetime lifetime,
            CancellationToken cancellationToken) =>
        {
            // Not the request's token: a run over a library outlives the
            // response, and what may stop it is the container shutting down.
            runner.TryStart(lifetime.ApplicationStopping);

            return TypedResults.Ok(await DownloadsState.ReadAsync(
                scanning,
                hashing,
                scan.Status,
                runner.Status,
                cancellationToken));
        });

        return endpoints;
    }
}

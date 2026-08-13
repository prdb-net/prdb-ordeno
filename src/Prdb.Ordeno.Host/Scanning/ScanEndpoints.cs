using Microsoft.AspNetCore.Http.HttpResults;

using Prdb.Ordeno.Infrastructure.Identification;
using Prdb.Ordeno.Infrastructure.Scanning;

namespace Prdb.Ordeno.Host.Scanning;

internal static class ScanEndpoints
{
    /// <summary>
    /// Behind the password like everything else. What is in someone's download
    /// directories is exactly the sort of thing the password is there for.
    /// </summary>
    public static IEndpointRouteBuilder MapScanning(this IEndpointRouteBuilder endpoints)
    {
        var scan = endpoints.MapGroup("/api/scan").WithTags("Scanning");

        scan.MapGet("/", async Task<Ok<ScanState>> (
            ScanService service,
            PerceptualHashService hashing,
            ScanRunner runner,
            IdentificationRunner identification,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await DownloadsState.ReadAsync(
                service,
                hashing,
                runner.Status,
                identification.Status,
                cancellationToken)));

        // Starts a scan and answers with the state as it is now, rather than
        // holding the request open: a first pass over an existing library is
        // minutes of walking, and a browser waiting that long has already given
        // up. Asking while one is running is not an error — the answer is a scan
        // in progress either way, which is what the caller wanted.
        scan.MapPost("/", async Task<Ok<ScanState>> (
            ScanService service,
            PerceptualHashService hashing,
            ScanRunner runner,
            IdentificationRunner identification,
            IHostApplicationLifetime lifetime,
            CancellationToken cancellationToken) =>
        {
            // Deliberately not the request's token. The scan outlives the
            // response; what may stop it is the container shutting down.
            runner.TryStart(lifetime.ApplicationStopping);

            return TypedResults.Ok(await DownloadsState.ReadAsync(
                service,
                hashing,
                runner.Status,
                identification.Status,
                cancellationToken));
        });

        return endpoints;
    }
}

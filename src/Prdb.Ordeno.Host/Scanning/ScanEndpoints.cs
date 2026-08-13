using Microsoft.AspNetCore.Http.HttpResults;

using Prdb.Ordeno.Core.Scanning;
using Prdb.Ordeno.Infrastructure.Scanning;

namespace Prdb.Ordeno.Host.Scanning;

/// <summary>
/// One watched directory and what is in it.
/// </summary>
public sealed record ScannedSourceState(
    int SourceId,
    string Path,
    bool Reachable,
    string? Problem,
    int Ready,
    int Settling,
    int Total);

/// <summary>
/// One video the tool has found. <paramref name="Ready"/> means it has stopped
/// being written to — not that anything has been done with it.
/// </summary>
public sealed record ScannedFileState(
    int Id,
    int SourceId,
    string Path,
    string Name,
    long SizeBytes,
    bool Ready,
    DateTimeOffset FirstSeenAt);

/// <summary>
/// What the last scan found, and whether one is running. The file list is capped
/// — <paramref name="Total"/> is the real number.
/// </summary>
public sealed record ScanState(
    bool Scanning,
    DateTimeOffset? LastScanStartedAt,
    DateTimeOffset? LastScanFinishedAt,
    string? Problem,
    bool OnboardingComplete,
    IReadOnlyList<ScannedSourceState> Sources,
    IReadOnlyList<ScannedFileState> Files,
    int Ready,
    int Settling,
    int Total,
    string WhatItFound);

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
            ScanRunner runner,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(StateOf(await service.ReadAsync(cancellationToken), runner.Status)));

        // Starts a scan and answers with the state as it is now, rather than
        // holding the request open: a first pass over an existing library is
        // minutes of walking, and a browser waiting that long has already given
        // up. Asking while one is running is not an error — the answer is a scan
        // in progress either way, which is what the caller wanted.
        scan.MapPost("/", async Task<Ok<ScanState>> (
            ScanService service,
            ScanRunner runner,
            IHostApplicationLifetime lifetime,
            CancellationToken cancellationToken) =>
        {
            // Deliberately not the request's token. The scan outlives the
            // response; what may stop it is the container shutting down.
            runner.TryStart(lifetime.ApplicationStopping);

            return TypedResults.Ok(StateOf(await service.ReadAsync(cancellationToken), runner.Status));
        });

        return endpoints;
    }

    private static ScanState StateOf(Inventory inventory, ScanRun run) => new(
        Scanning: run.Running,
        LastScanStartedAt: run.StartedAt,
        LastScanFinishedAt: run.FinishedAt,
        Problem: run.Problem,
        OnboardingComplete: inventory.OnboardingComplete,
        Sources:
        [
            .. inventory.Sources.Select(source => new ScannedSourceState(
                source.SourceId,
                source.Path,
                source.Reachable,
                source.Problem,
                source.Ready,
                source.Settling,
                source.Total)),
        ],
        Files:
        [
            .. inventory.Files.Select(file => new ScannedFileState(
                file.Id,
                file.SourceId,
                file.Path,
                file.Name,
                file.SizeBytes,
                file.Ready,
                file.FirstSeenAt)),
        ],
        Ready: inventory.Ready,
        Settling: inventory.Settling,
        Total: inventory.Total,
        WhatItFound: inventory.WhatItFound);
}

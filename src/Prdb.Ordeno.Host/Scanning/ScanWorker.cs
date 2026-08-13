using Prdb.Ordeno.Core.Scanning;
using Prdb.Ordeno.Infrastructure.Scanning;

namespace Prdb.Ordeno.Host.Scanning;

/// <summary>
/// Looks in the download directories every few minutes, for as long as the
/// container runs. This is the "and then keeps doing that on its own" half of
/// VISION.md — the tool is set up once and left alone, so nothing here waits for
/// a person.
/// </summary>
/// <remarks>
/// A timer rather than filesystem notifications: ADR 0016. Until onboarding has
/// been finished the scan itself does nothing, so this ticks quietly through a
/// fresh installation rather than being started later by something clever.
/// </remarks>
internal sealed class ScanWorker(
    ScanRunner runner,
    TimeProvider time,
    ILogger<ScanWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Volumes are mounted around the same moment this starts. Scanning
            // into that race would report a perfectly good share as unreachable
            // and log a warning the user then goes looking for.
            await Task.Delay(ScanSchedule.FirstScanDelay, time, stoppingToken);

            using var timer = new PeriodicTimer(ScanSchedule.Interval, time);

            do
            {
                // The runner logs what happened and swallows what went wrong, so
                // that one bad scan is one bad scan rather than the end of the
                // only thing keeping this tool up to date.
                await runner.RunAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Scanning stopped: the tool is shutting down.");
        }
    }
}

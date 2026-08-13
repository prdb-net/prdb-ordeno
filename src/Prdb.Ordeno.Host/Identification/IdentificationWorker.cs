using Prdb.Ordeno.Core.Identification;
using Prdb.Ordeno.Infrastructure.Identification;

namespace Prdb.Ordeno.Host.Identification;

/// <summary>
/// Asks prdb about whatever the scan has found to be finished, every few
/// minutes, for as long as the container runs.
/// </summary>
/// <remarks>
/// It is a second timer rather than something the scan calls at the end of its
/// own run, because the two fail differently: a share that cannot be read must
/// not stop the tool asking about the files it already knows, and prdb being
/// down must not stop it looking in the download directories. A run that finds
/// nothing to ask about costs one query and no request.
/// </remarks>
internal sealed class IdentificationWorker(
    IdentificationRunner runner,
    TimeProvider time,
    ILogger<IdentificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Longer than the scan's first delay on purpose: on a fresh start it
            // is the first scan that produces the work, and a run before it has
            // happened is a query that answers nothing.
            await Task.Delay(IdentificationSchedule.FirstRunDelay, time, stoppingToken);

            using var timer = new PeriodicTimer(IdentificationSchedule.Interval, time);

            do
            {
                // The runner records what happened and swallows what went wrong,
                // so that one bad run is one bad run.
                await runner.RunAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Identifying stopped: the tool is shutting down.");
        }
    }
}

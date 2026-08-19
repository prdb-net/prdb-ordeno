using Prdb.Ordeno.Core.History;
using Prdb.Ordeno.Core.Library;
using Prdb.Ordeno.Infrastructure.Library;

namespace Prdb.Ordeno.Host.Library;

/// <summary>
/// Files what the tool has recognised, every quarter of an hour, for as long as
/// somebody leaves the switch on — ADR 0031.
/// </summary>
/// <remarks>
/// <para>
/// The timer ADR 0022 deferred, and deliberately nothing more than a timer: it
/// calls the same <see cref="FilingRunner.TryFile"/> the button does, which
/// works the plan out again as it reaches each file. Nothing about filing knows
/// whether anybody is watching, apart from the account it leaves behind.
/// </para>
/// <para>
/// Two things stand between the tick and a run, and both are checked here rather
/// than inside the run. The switch, because a tool that files on its own because
/// it was upgraded is the surprise the opt-in rule exists to prevent; and
/// whether anything is waiting at all, because working out what would happen
/// reads the header of every settled video and doing that four times an hour on
/// somebody's NAS to be told "nothing has arrived" is the kind of background
/// noise that gets a tool uninstalled.
/// </para>
/// </remarks>
internal sealed class FilingWorker(
    FilingRunner runner,
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<FilingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Longer than the scan's first delay, for the reason the
            // identification run's is: the volumes are mounted around the moment
            // this starts, and it is the first scan that says whether anything
            // in them has settled.
            await Task.Delay(FilingSchedule.FirstRunDelay, time, stoppingToken);

            using var timer = new PeriodicTimer(FilingSchedule.Interval, time);

            do
            {
                await FileIfAskedToAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Unattended filing stopped: the tool is shutting down.");
        }
    }

    /// <summary>
    /// One tick: the switch, then whether there is anything to do, then the same
    /// run the button starts.
    /// </summary>
    /// <remarks>
    /// Nothing here throws. A database that could not be read this minute is one
    /// missed tick rather than the end of the only thing filing this
    /// installation, and the runner already swallows whatever a run goes wrong
    /// with.
    /// </remarks>
    private async Task FileIfAskedToAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<FilingService>();

            if (!await service.UnattendedAsync(stoppingToken))
            {
                return;
            }

            if (!await service.AnythingWaitingAsync(stoppingToken))
            {
                logger.LogDebug("Nothing is waiting to be filed; no unattended run is started.");

                return;
            }

            // The stopping token rather than anything of this method's: the run
            // outlives the tick that started it, and what may interrupt it is the
            // container going away — which has to reach the file being copied.
            if (!runner.TryFile(AskedBy.Timer, stoppingToken))
            {
                // The last run is still going, or somebody is undoing something.
                // Both are answers to "file now", and the gate is what makes them
                // one at a time (ADR 0029).
                logger.LogDebug("The library is busy; this unattended run is not started.");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not work out whether to file without being asked.");
        }
    }
}

using Prdb.Ordeno.Core.History;
using Prdb.Ordeno.Core.Library;
using Prdb.Ordeno.Infrastructure.Library;

namespace Prdb.Ordeno.Host.Library;

/// <summary>
/// Checks a slice of the library against what prdb says now, once a day, for as
/// long as somebody leaves the switch on — ADR 0032.
/// </summary>
/// <remarks>
/// <para>
/// A timer and nothing more, like <see cref="FilingWorker"/>: it calls the same
/// <see cref="RefreshRunner.TryRefresh"/> the button does. The one thing it
/// changes is how far the run goes — a slice rather than the library — which is
/// what fixes what a night costs whatever size the library is.
/// </para>
/// <para>
/// Two things stand between a tick and a run. The switch, because a tool that
/// starts rewriting files because it was upgraded is the surprise the opt-in
/// rule exists to prevent; and whether this library holds anything the tool
/// filed at all, because a run over nothing is still a run that opens a row and
/// reads a table.
/// </para>
/// </remarks>
internal sealed class RefreshWorker(
    RefreshRunner runner,
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<RefreshWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(RefreshSchedule.FirstRunDelay, time, stoppingToken);

            using var timer = new PeriodicTimer(RefreshSchedule.Interval, time);

            do
            {
                await CheckIfAskedToAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Unattended metadata refreshing stopped: the tool is shutting down.");
        }
    }

    /// <summary>
    /// One tick: the switch, then whether there is a library to check, then the
    /// same run the button starts.
    /// </summary>
    /// <remarks>
    /// Nothing here throws. A database that could not be read today is one missed
    /// day of a feature whose whole point is that it comes round again, and the
    /// runner already swallows whatever a run goes wrong with.
    /// </remarks>
    private async Task CheckIfAskedToAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<RefreshService>();

            if (!await service.UnattendedAsync(stoppingToken))
            {
                return;
            }

            if ((await service.StandingAsync(stoppingToken)).Scenes == 0)
            {
                logger.LogDebug("This library holds nothing this tool filed; no check is started.");

                return;
            }

            // The stopping token rather than anything of this method's: the run
            // outlives the tick that started it, and what may interrupt it is the
            // container going away.
            if (!runner.TryRefresh(AskedBy.Timer, stoppingToken))
            {
                // Filing or an undo has the gate. Both are better uses of it than
                // this, and the check comes round again tomorrow.
                logger.LogDebug("The library is busy; this unattended check is not started.");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not work out whether to check the library.");
        }
    }
}

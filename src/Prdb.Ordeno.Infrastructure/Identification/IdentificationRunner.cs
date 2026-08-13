using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Prdb.Ordeno.Core.Identification;

namespace Prdb.Ordeno.Infrastructure.Identification;

/// <summary>
/// The one place an identification run is started from, whether the clock asked
/// for it or a person did.
/// </summary>
/// <remarks>
/// Two runs at once would ask prdb about the same files twice and pay for both,
/// so there is one gate. A refused start is not an error: somebody who presses
/// the button while a run is under way wanted a run, and there is one.
/// </remarks>
public sealed class IdentificationRunner(
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<IdentificationRunner> logger)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    /// <summary>
    /// Written by whichever thread holds the gate and read by request threads
    /// that hold nothing, so it is published rather than merely assigned.
    /// </summary>
    private IdentificationRun status = IdentificationRun.Never;

    public IdentificationRun Status => Volatile.Read(ref status);

    /// <summary>
    /// Runs and waits for it to finish. What the periodic worker calls.
    /// </summary>
    /// <remarks>
    /// A tick does nothing at all while prdb has asked to be left alone. Spending
    /// a request every five minutes to be refused again is how a rate limit
    /// turns into a rate limit that never ends.
    /// </remarks>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var run = Status;

        if (!run.MayRunAt(time.GetUtcNow()))
        {
            logger.LogDebug("Not asking prdb yet: waiting until {NotBefore}.", run.NotBefore);

            return;
        }

        if (!gate.Wait(0, CancellationToken.None))
        {
            logger.LogDebug("An identification run is already under way; this one is not started.");

            return;
        }

        await IdentifyAsync(cancellationToken);
    }

    /// <summary>
    /// Starts a run and returns immediately, having already marked it as
    /// running. What the endpoint calls: a first pass over an existing library
    /// takes longer than a request should.
    /// </summary>
    /// <remarks>
    /// A person asking goes ahead even while the tool is waiting out a refusal.
    /// The usual reason for pressing it is having just fixed the thing that
    /// caused the refusal, and being told to come back in a quarter of an hour
    /// would be the wrong answer to that.
    /// </remarks>
    /// <param name="cancellationToken">
    /// Must outlive the request that started it — the application's own stopping
    /// token.
    /// </param>
    /// <returns><c>false</c> when one was already running.</returns>
    public bool TryStart(CancellationToken cancellationToken = default)
    {
        if (!gate.Wait(0, CancellationToken.None))
        {
            return false;
        }

        Publish(Status.Started(time.GetUtcNow()));

        _ = Task.Run(
            () => IdentifyAsync(cancellationToken, alreadyStarted: true),
            CancellationToken.None);

        return true;
    }

    private async Task IdentifyAsync(CancellationToken cancellationToken, bool alreadyStarted = false)
    {
        if (!alreadyStarted)
        {
            Publish(Status.Started(time.GetUtcNow()));
        }

        try
        {
            await using var scope = scopes.CreateAsyncScope();

            var outcome = await scope.ServiceProvider
                .GetRequiredService<IdentificationService>()
                .IdentifyAsync(cancellationToken);

            Publish(Status.Finished(
                time.GetUtcNow(),
                outcome.Asked,
                outcome.Problem,
                outcome.NotBefore));
        }
        catch (OperationCanceledException)
        {
            // The container is stopping. Nothing is half done: an answer is
            // stored per batch, and the batch that was in flight simply has no
            // answer yet.
            Publish(Status.Finished(
                time.GetUtcNow(),
                0,
                "Identifying was stopped before it finished."));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The identification run failed.");

            Publish(Status.Finished(
                time.GetUtcNow(),
                0,
                "Identifying stopped with an error. The container's log has the details."));
        }
        finally
        {
            gate.Release();
        }
    }

    private void Publish(IdentificationRun run) => Volatile.Write(ref status, run);
}

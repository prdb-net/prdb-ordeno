using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Prdb.Ordeno.Core.Scanning;

namespace Prdb.Ordeno.Infrastructure.Scanning;

/// <summary>
/// The one place a scan is started from, whether the clock asked for it or a
/// person did.
/// </summary>
/// <remarks>
/// It exists because two scans at once would be two walks writing the same rows,
/// and because someone pressing the button while the periodic scan is halfway
/// through should get the scan that is already running rather than a second one.
/// A refused start is therefore not an error: the answer to "scan now" is a scan
/// in progress either way.
/// </remarks>
public sealed class ScanRunner(
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<ScanRunner> logger)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    /// <summary>
    /// Written by whichever thread holds the gate and read by request threads
    /// that hold nothing, so it is published rather than merely assigned.
    /// </summary>
    private ScanRun status = ScanRun.Never;

    /// <summary>The last scan, or the one running now.</summary>
    public ScanRun Status => Volatile.Read(ref status);

    /// <summary>
    /// Scans and waits for it to finish. What the periodic worker calls, so that
    /// one tick cannot overtake the one before it.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        if (!gate.Wait(0, CancellationToken.None))
        {
            logger.LogDebug("A scan is already running; this one is not started.");

            return;
        }

        await ScanAsync(cancellationToken);
    }

    /// <summary>
    /// Starts a scan and returns immediately, having already marked it as
    /// running. What the endpoint calls: a first pass over an existing library
    /// takes longer than a request should, and the screen watches
    /// <see cref="Status"/> instead.
    /// </summary>
    /// <param name="cancellationToken">
    /// Must outlive the request that started it — the application's own stopping
    /// token. A scan cancelled the moment the browser has its answer would be a
    /// scan that never happens.
    /// </param>
    /// <returns><c>false</c> when one was already running.</returns>
    public bool TryStart(CancellationToken cancellationToken = default)
    {
        if (!gate.Wait(0, CancellationToken.None))
        {
            return false;
        }

        Publish(Status.Started(time.GetUtcNow()));

        _ = Task.Run(() => ScanAsync(cancellationToken, alreadyStarted: true), CancellationToken.None);

        return true;
    }

    /// <summary>Runs the scan with the gate held, and releases it whatever happens.</summary>
    private async Task ScanAsync(CancellationToken cancellationToken, bool alreadyStarted = false)
    {
        if (!alreadyStarted)
        {
            Publish(Status.Started(time.GetUtcNow()));
        }

        try
        {
            await using var scope = scopes.CreateAsyncScope();

            await scope.ServiceProvider
                .GetRequiredService<ScanService>()
                .ScanAsync(cancellationToken);

            Publish(Status.Finished(time.GetUtcNow()));
        }
        catch (OperationCanceledException)
        {
            // The container is stopping. Nothing was moved and nothing is half
            // done — the next start scans again from what is on disk then.
            Publish(Status.Finished(time.GetUtcNow(), "The scan was stopped before it finished."));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The scan failed.");

            Publish(Status.Finished(
                time.GetUtcNow(),
                "The scan stopped with an error. The container's log has the details."));
        }
        finally
        {
            gate.Release();
        }
    }

    private void Publish(ScanRun run) => Volatile.Write(ref status, run);
}

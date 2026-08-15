using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Prdb.Ordeno.Core.Library;

namespace Prdb.Ordeno.Infrastructure.Library;

/// <summary>
/// The one place filing is started from — and, until there is a way back, only
/// ever by somebody asking for it.
/// </summary>
/// <remarks>
/// <para>
/// There is no timer here, and its absence is the decision in ADR 0022 rather
/// than an omission. When the operation log
/// (<see href="https://github.com/prdb-net/prdb-ordeno/issues/19">#19</see>)
/// exists, a worker calls <see cref="TryFile"/> on an interval and nothing else
/// about this changes.
/// </para>
/// <para>
/// One gate over both entry points. Planning while a run is filing would show a
/// preview of a library that is being rearranged underneath it, and two runs at
/// once would have two copies of the same plan moving the same file.
/// </para>
/// </remarks>
public sealed class FilingRunner(
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<FilingRunner> logger)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    /// <summary>
    /// Written by whichever thread holds the gate and read by request threads
    /// that hold nothing, so it is published rather than merely assigned.
    /// </summary>
    private FilingRun status = FilingRun.Never;

    public FilingRun Status => Volatile.Read(ref status);

    /// <summary>
    /// Works out what filing would do and answers immediately, having marked the
    /// run as started. A first pass over an existing library reads the header of
    /// every video in it, which is longer than a request should be held open
    /// for.
    /// </summary>
    /// <returns><c>false</c> when something was already under way.</returns>
    public bool TryPlan(CancellationToken cancellationToken = default) =>
        TryStart(filing: false, cancellationToken);

    /// <summary>
    /// Carries out what the plan says, working it out again as it goes.
    /// </summary>
    /// <param name="cancellationToken">
    /// Must outlive the request that started it — the application's own stopping
    /// token, so that a shutdown reaches the file being copied.
    /// </param>
    /// <returns><c>false</c> when something was already under way.</returns>
    public bool TryFile(CancellationToken cancellationToken = default) =>
        TryStart(filing: true, cancellationToken);

    private bool TryStart(bool filing, CancellationToken cancellationToken)
    {
        if (!gate.Wait(0, CancellationToken.None))
        {
            return false;
        }

        Publish(Status.Started(time.GetUtcNow(), filing));

        _ = Task.Run(() => RunAsync(filing, cancellationToken), CancellationToken.None);

        return true;
    }

    private async Task RunAsync(bool filing, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<FilingService>();

            if (filing)
            {
                var report = await service.FileAsync(cancellationToken);

                logger.LogInformation(
                    "Filed {Filed} of {Total} videos.",
                    report.Results.Count(result => result.Filed),
                    report.Results.Count);

                Publish(Status.Filed(time.GetUtcNow(), report.Results, report.Problem));
            }
            else
            {
                var preview = await service.PlanAsync(cancellationToken);

                Publish(Status.Planned(time.GetUtcNow(), preview.Plans, preview.Problem));
            }
        }
        catch (OperationCanceledException)
        {
            // The container is stopping. Nothing is half done: the move itself
            // either finished or left the original where it was, and what had
            // not been reached was not touched.
            Publish(filing
                ? Status.Filed(time.GetUtcNow(), [], "Filing was stopped before it finished.")
                : Status.Planned(time.GetUtcNow(), [], "The tool stopped before it worked this out."));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The filing run failed.");

            Publish(filing
                ? Status.Filed(time.GetUtcNow(), [], "Filing stopped with an error. The container's log has the details.")
                : Status.Planned(time.GetUtcNow(), [], "Working out what would happen stopped with an error. The container's log has the details."));
        }
        finally
        {
            gate.Release();
        }
    }

    private void Publish(FilingRun run) => Volatile.Write(ref status, run);
}

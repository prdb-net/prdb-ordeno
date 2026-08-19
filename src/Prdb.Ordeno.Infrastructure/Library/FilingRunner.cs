using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Prdb.Ordeno.Core.Library;
using Prdb.Ordeno.Infrastructure.MediaServer;

namespace Prdb.Ordeno.Infrastructure.Library;

/// <summary>
/// The one place filing is started from — and, until there is a way back, only
/// ever by somebody asking for it.
/// </summary>
/// <remarks>
/// <para>
/// There is no timer here, and its absence is the decision in ADR 0022 rather
/// than an omission. The operation log and its undo now exist, so what is left
/// before a worker calls <see cref="TryFile"/> on an interval is the question
/// ADR 0029 handed it: what an undone file means to a run nobody is watching.
/// </para>
/// <para>
/// One gate over both entry points, and the way back takes the same one
/// (<see cref="LibraryGate"/>). Planning while a run is filing would show a
/// preview of a library that is being rearranged underneath it, and two runs at
/// once would have two copies of the same plan moving the same file.
/// </para>
/// </remarks>
public sealed class FilingRunner(
    IServiceScopeFactory scopes,
    LibraryGate gate,
    TimeProvider time,
    ILogger<FilingRunner> logger)
{

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
        if (!gate.TryEnter())
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

                // Only now, and only after the run has been reported. Everything
                // above is the filing path and ADR 0018 keeps the media server
                // out of it: the files have moved, the rows are written and the
                // screen already says so before anything is asked of a server
                // that may be switched off.
                await TellTheMediaServerAsync(scope.ServiceProvider, report, cancellationToken);
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
            gate.Leave();
        }
    }

    /// <summary>
    /// Asks the media server to read the sidecars this run has just written, so
    /// that a rewritten one appears without waiting out the tolerance window a
    /// scan obeys.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing in here can fail a filing. It runs after the report is published,
    /// it swallows everything, and a configured connection that is down is a line
    /// in the container's log and never a file that did not get filed.
    /// </para>
    /// <para>
    /// A shutdown skips it altogether. The container is going away and the videos
    /// are already in the library; the next scan over there finds them, which is
    /// what happens for everyone who left the connection blank.
    /// </para>
    /// </remarks>
    private async Task TellTheMediaServerAsync(
        IServiceProvider services,
        FilingReport report,
        CancellationToken cancellationToken)
    {
        // Only the ones that came out of this run with a sidecar next to them.
        // A video filed without one has nothing new for the server to read.
        var filed = report.Results
            .Where(result => result.Filed && result.Plan.Sidecar.Writes && result.Sidecar is null)
            .Select(result => result.Plan.TargetPath)
            .OfType<string>()
            .ToList();

        if (filed.Count == 0 || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await services.GetRequiredService<MediaServerService>().RefreshAsync(filed, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "The media server could not be told what was filed.");
        }
    }

    private void Publish(FilingRun run) => Volatile.Write(ref status, run);
}

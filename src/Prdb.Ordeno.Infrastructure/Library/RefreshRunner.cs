using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Prdb.Ordeno.Core.History;
using Prdb.Ordeno.Core.Library;
using Prdb.Ordeno.Infrastructure.MediaServer;

namespace Prdb.Ordeno.Infrastructure.Library;

/// <summary>
/// The one place a metadata refresh is started from, whether somebody asked for
/// it or the clock did — ADR 0032.
/// </summary>
/// <remarks>
/// <para>
/// Shaped like <see cref="FilingRunner"/> and for the same reasons: the run
/// outlives the request that started it, the screen reads a status while it
/// works, and who asked travels with it because the log and the screen both have
/// to say so.
/// </para>
/// <para>
/// It takes the same gate filing and undo take (<see cref="LibraryGate"/>).
/// Rewriting a sidecar in a scene directory an undo is taking apart is two plans
/// over one directory, which is what the gate exists to prevent.
/// </para>
/// <para>
/// There is no preview here, and that is ADR 0032 rather than an omission: a
/// preview stands between a user and a move that loses a file, this run moves
/// nothing, and working out what it would write costs exactly the requests the
/// run itself costs.
/// </para>
/// </remarks>
public sealed class RefreshRunner(
    IServiceScopeFactory scopes,
    LibraryGate gate,
    TimeProvider time,
    ILogger<RefreshRunner> logger)
{
    /// <summary>
    /// Written by whichever thread holds the gate and read by request threads
    /// that hold nothing, so it is published rather than merely assigned.
    /// </summary>
    private RefreshRun status = RefreshRun.Never;

    public RefreshRun Status => Volatile.Read(ref status);

    /// <summary>
    /// Checks the library against what prdb says now.
    /// </summary>
    /// <param name="askedBy">
    /// Who this run is for. It decides how far the run goes — a person gets the
    /// whole library, the timer gets <see cref="RefreshSchedule.Slice"/> of it —
    /// and what an empty run leaves in the log.
    /// </param>
    /// <param name="cancellationToken">
    /// Must outlive the request that started it: the application's own stopping
    /// token, so that a shutdown reaches a run walking a library.
    /// </param>
    /// <returns><c>false</c> when something was already rearranging the library.</returns>
    public bool TryRefresh(
        AskedBy askedBy = AskedBy.Person,
        CancellationToken cancellationToken = default)
    {
        if (!gate.TryEnter())
        {
            return false;
        }

        Publish(Status.Started(time.GetUtcNow(), askedBy));

        _ = Task.Run(() => RunAsync(askedBy, cancellationToken), CancellationToken.None);

        return true;
    }

    private async Task RunAsync(AskedBy askedBy, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<RefreshService>();

            var report = await service.RefreshAsync(
                askedBy,
                askedBy is AskedBy.Timer ? RefreshSchedule.Slice : null,
                cancellationToken);

            logger.LogInformation(
                "Checked {Checked} scenes: {Sidecars} metadata files rewritten, {Images} images written.",
                report.Checked,
                report.Sidecars,
                report.Images);

            Publish(Status.Finished(time.GetUtcNow(), report));

            // Only now, and only for what actually changed. Everything above is
            // a write path and ADR 0018 keeps the media server out of those: the
            // files are written and the screen already says so before anything
            // is asked of a server that may be switched off.
            await TellTheMediaServerAsync(scope.ServiceProvider, report, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // The container is stopping. Every document this run wrote is whole —
            // each is a write and a rename — and what it had not reached was not
            // touched, which is also what its stamps say.
            Publish(Status.Finished(
                time.GetUtcNow(),
                RefreshReport.Nothing with
                {
                    Problem = "The check was stopped before it finished. Nothing it had already "
                        + "written is affected.",
                }));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The metadata refresh failed.");

            Publish(Status.Finished(
                time.GetUtcNow(),
                RefreshReport.Nothing with
                {
                    Problem = "Checking the library stopped with an error. The container's log has "
                        + "the details.",
                }));
        }
        finally
        {
            gate.Leave();
        }
    }

    /// <summary>
    /// Asks the media server to read the sidecars this run rewrote, so that a
    /// corrected title appears without waiting out the tolerance window a scan
    /// obeys.
    /// </summary>
    /// <remarks>
    /// Nothing in here can fail a refresh, and a run that rewrote nothing asks
    /// nothing — which is what makes the comparison in ADR 0033 pay for itself
    /// twice. A shutdown skips it: the documents are on disk and the next scan
    /// over there finds them, exactly as it does for everyone who left the
    /// connection blank.
    /// </remarks>
    private async Task TellTheMediaServerAsync(
        IServiceProvider services,
        RefreshReport report,
        CancellationToken cancellationToken)
    {
        var changed = report.Notes
            .Where(note => note.WroteSidecar)
            .Select(note => note.VideoPath)
            .ToList();

        if (changed.Count == 0 || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await services.GetRequiredService<MediaServerService>().RefreshAsync(changed, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "The media server could not be told what was rewritten.");
        }
    }

    private void Publish(RefreshRun run) => Volatile.Write(ref status, run);
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Prdb.Ordeno.Core.History;
using Prdb.Ordeno.Infrastructure.Library;

namespace Prdb.Ordeno.Infrastructure.History;

/// <summary>
/// The one place an undo is started from, and the same gate filing runs behind.
/// </summary>
/// <remarks>
/// <para>
/// The shape is <c>FilingRunner</c>'s, because it is the same problem: putting a
/// batch back is minutes of copying on a NAS, which is longer than a request
/// should be held open for, so this answers as soon as a run is under way and the
/// screen reads the status until it stops.
/// </para>
/// <para>
/// One thing at a time across both — <see cref="LibraryGate"/>. An undo while a
/// filing is under way would be two plans moving the same file in opposite
/// directions.
/// </para>
/// </remarks>
public sealed class UndoRunner(
    IServiceScopeFactory scopes,
    LibraryGate gate,
    TimeProvider time,
    ILogger<UndoRunner> logger)
{
    /// <summary>
    /// Written by whichever thread holds the gate and read by request threads
    /// that hold nothing, so it is published rather than merely assigned.
    /// </summary>
    private UndoRun status = UndoRun.Never;

    public UndoRun Status => Volatile.Read(ref status);

    /// <summary>
    /// Works out what putting a run back would do, and answers immediately. It
    /// reads every file the run filed, which on a batch of two hundred is longer
    /// than a request.
    /// </summary>
    /// <returns><c>false</c> when something was already under way.</returns>
    public bool TryCheck(int? runId, int? operationId, CancellationToken cancellationToken = default) =>
        TryStart(undoing: false, runId, operationId, cancellationToken);

    /// <summary>
    /// Puts it back.
    /// </summary>
    /// <param name="cancellationToken">
    /// Must outlive the request that started it — the application's own stopping
    /// token, so that a shutdown reaches the file being copied.
    /// </param>
    /// <returns><c>false</c> when something was already under way.</returns>
    public bool TryUndo(int? runId, int? operationId, CancellationToken cancellationToken = default) =>
        TryStart(undoing: true, runId, operationId, cancellationToken);

    private bool TryStart(
        bool undoing,
        int? runId,
        int? operationId,
        CancellationToken cancellationToken)
    {
        if (!gate.TryEnter())
        {
            return false;
        }

        Publish(Status.Started(time.GetUtcNow(), undoing, runId, operationId));

        _ = Task.Run(
            () => RunAsync(undoing, runId, operationId, cancellationToken),
            CancellationToken.None);

        return true;
    }

    private async Task RunAsync(
        bool undoing,
        int? runId,
        int? operationId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<UndoService>();

            if (undoing)
            {
                var report = await service.UndoAsync(runId, operationId, cancellationToken);

                logger.LogInformation(
                    "Put {Returned} of {Total} operations back.",
                    report.Results.Count(result => result.Returned),
                    report.Results.Count);

                Publish(Status.Undone(time.GetUtcNow(), report.Results, report.Problem));
            }
            else
            {
                var preview = await service.CheckAsync(runId, operationId, cancellationToken);

                Publish(Status.Checked(time.GetUtcNow(), preview.Plans, preview.Problem));
            }
        }
        catch (OperationCanceledException)
        {
            // The container is stopping. Nothing is half done: a file either went
            // back or was left exactly where it was.
            Publish(undoing
                ? Status.Undone(time.GetUtcNow(), [], "The undo was stopped before it finished.")
                : Status.Checked(time.GetUtcNow(), [], "The tool stopped before it worked this out."));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The undo run failed.");

            Publish(undoing
                ? Status.Undone(time.GetUtcNow(), [], "The undo stopped with an error. The container's log has the details.")
                : Status.Checked(time.GetUtcNow(), [], "Working out what would happen stopped with an error. The container's log has the details."));
        }
        finally
        {
            gate.Leave();
        }
    }

    private void Publish(UndoRun run) => Volatile.Write(ref status, run);
}

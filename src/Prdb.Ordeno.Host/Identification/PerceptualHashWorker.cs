using Prdb.Ordeno.Infrastructure.Identification;

namespace Prdb.Ordeno.Host.Identification;

/// <summary>
/// Works through the perceptual hash backlog, one file at a time, for as long as
/// the container runs.
/// </summary>
/// <remarks>
/// <para>
/// One file at a time and never two: each one decodes twenty-five frames, and
/// this runs on the machine somebody stores their films on. A backlog that
/// finishes a day later but is not noticed is the right trade here.
/// </para>
/// <para>
/// Nothing waits for this. A file with no perceptual hash is identified without
/// one and asked about again when it has one, so the slowest part of the tool
/// holds up none of the rest.
/// </para>
/// </remarks>
internal sealed class PerceptualHashWorker(
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<PerceptualHashWorker> logger) : BackgroundService
{
    /// <summary>
    /// The wait when there is nothing to hash. Work appears only after a scan
    /// and an identification run, so looking more often than that would be a
    /// query answering "no" all day.
    /// </summary>
    private static readonly TimeSpan WhenIdle = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The pause between two files. Hashing is the one thing this tool does that
    /// somebody would notice on a shared machine, and it is never urgent.
    /// </summary>
    private static readonly TimeSpan BetweenFiles = TimeSpan.FromSeconds(5);

    /// <summary>Long enough after a start that the first scan has run.</summary>
    private static readonly TimeSpan FirstDelay = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(FirstDelay, time, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                var hashed = await HashOneAsync(stoppingToken);

                await Task.Delay(hashed ? BetweenFiles : WhenIdle, time, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("The perceptual hash backlog stopped: the tool is shutting down.");
        }
    }

    private async Task<bool> HashOneAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();

            return await scope.ServiceProvider
                .GetRequiredService<PerceptualHashService>()
                .HashNextAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // One file that went wrong in a way the hashing service did not
            // catch is not a reason to stop hashing the rest for the lifetime of
            // the container.
            logger.LogError(exception, "The perceptual hash backlog stumbled on a file.");

            return false;
        }
    }
}

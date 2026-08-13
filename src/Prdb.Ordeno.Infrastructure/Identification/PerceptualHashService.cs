using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Ordeno.Core.Identification;
using Prdb.Ordeno.Core.Scanning;
using Prdb.Ordeno.Infrastructure.Persistence;

namespace Prdb.Ordeno.Infrastructure.Identification;

/// <summary>
/// The perceptual hash backlog: one file at a time, at its own pace, for as long
/// as the container runs.
/// </summary>
/// <remarks>
/// <para>
/// Computing one decodes twenty-five frames, which is seconds to minutes of a
/// NAS's CPU per file. That is why it cannot sit in the path of a file being
/// identified, and why a file waiting for its hash holds nothing else up: it is
/// asked about without one first, and asked again once the hash exists.
/// </para>
/// <para>
/// Only files the exact hash did not settle are hashed. prdb still compares
/// perceptual hashes for equality, so hashing a file it has already recognised
/// by its <c>osHash</c> spends minutes of somebody's evening to learn what is
/// already known. Everything else is fair game, because for those files the
/// answer can still change.
/// </para>
/// </remarks>
public sealed class PerceptualHashService(
    OrdenoDbContext context,
    IPerceptualHashes hashes,
    TimeProvider time,
    ILogger<PerceptualHashService> logger)
{
    /// <summary>Hashes the next file in the backlog.</summary>
    /// <returns><c>false</c> when there was nothing to do.</returns>
    public async Task<bool> HashNextAsync(CancellationToken cancellationToken = default)
    {
        var file = await NextAsync(cancellationToken);

        if (file is null)
        {
            return false;
        }

        logger.LogDebug("Computing the perceptual hash of {Path}.", file.Path);

        var reading = await hashes.ComputeAsync(file.Path, cancellationToken);

        file.PerceptualHashState = reading.State;
        file.PerceptualHashAttempts++;
        file.PerceptualHashAt = time.GetUtcNow();

        if (reading.Computed)
        {
            file.PerceptualHash = reading.Hash;

            // Nothing else has to happen for it to be asked about again: a file
            // whose answer was given without a perceptual hash is worth another
            // question the moment one exists, and that is what the next run
            // looks for.
            logger.LogInformation("{Path} has a perceptual hash; prdb will be asked about it again.", file.Path);
        }
        else
        {
            logger.LogInformation(
                "No perceptual hash for {Path}: {State}. {Error}",
                file.Path,
                reading.State,
                reading.Error);
        }

        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();

        return true;
    }

    /// <summary>How many files are still waiting for one.</summary>
    public Task<int> BacklogAsync(CancellationToken cancellationToken = default) =>
        Waiting().CountAsync(cancellationToken);

    private async Task<DiscoveredFile?> NextAsync(CancellationToken cancellationToken) =>
        await Waiting().OrderBy(file => file.Id).FirstOrDefaultAsync(cancellationToken);

    private IQueryable<DiscoveredFile> Waiting()
    {
        var settled = Settling.SettledIfUnchangedSince(time.GetUtcNow());

        return context.DiscoveredFiles.Where(file =>
            file.PerceptualHash == null
            && file.SizeBytes > 0
            && file.UnchangedSince <= settled

            // Tried and answered already, unless the answer was a timeout — the
            // one failure that says as much about how busy the disk was as about
            // the file.
            && (file.PerceptualHashState == null
                || (file.PerceptualHashState == PerceptualHashState.TimedOut
                    && file.PerceptualHashAttempts < PerceptualHashBacklog.MaxAttempts))

            // Asked about, and not settled by the exact hash. A file prdb has
            // not been asked about yet is left for the run that will ask: it may
            // be one of the ones that needs no hashing at all.
            && context.FileIdentifications.Any(identification =>
                identification.DiscoveredFileId == file.Id
                && identification.MatchedBy != MatchRung.OsHash));
    }
}

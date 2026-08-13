using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Ordeno.Core.Configuration;
using Prdb.Ordeno.Core.Scanning;
using Prdb.Ordeno.Infrastructure.Persistence;

namespace Prdb.Ordeno.Infrastructure.Scanning;

/// <summary>
/// The first half of the loop in VISION.md: look at the download directories and
/// know what is in them. Nothing here writes, renames or deletes a single file
/// the user owns — the only thing it changes is the tool's own record of what it
/// saw.
/// </summary>
/// <remarks>
/// The walk is slow and the database takes one writer at a time, so the two are
/// kept apart: files are collected in batches and each batch is saved on its
/// own. A transaction is never open while a directory is being listed.
/// </remarks>
public sealed class ScanService(
    OrdenoDbContext context,
    IDirectoryInspector inspector,
    ISourceWalker walker,
    TimeProvider time,
    ILogger<ScanService> logger)
{
    /// <summary>
    /// How many files are carried between the filesystem and the database at a
    /// time. Small enough that the parameter list of the lookup stays well
    /// inside what SQLite accepts, large enough that a library of thousands is a
    /// handful of round trips.
    /// </summary>
    private const int BatchSize = 500;

    /// <summary>
    /// Walks every usable source directory and brings the inventory up to date.
    /// </summary>
    /// <remarks>
    /// ADR 0009: nothing is scanned until onboarding has been finished. A
    /// directory that cannot be read is skipped and keeps its rows — the share
    /// is gone, its files are not, and forgetting them would make the next scan
    /// report the whole library as newly arrived.
    /// </remarks>
    public async Task ScanAsync(CancellationToken cancellationToken = default)
    {
        var configuration = await context.Configuration.AsNoTracking().SingleAsync(cancellationToken);

        if (configuration.OnboardingCompletedAt is null)
        {
            logger.LogDebug("Not scanning: onboarding has not been finished.");

            return;
        }

        var sources = await context.SourceDirectories
            .AsNoTracking()
            .OrderBy(source => source.Id)
            .ToListAsync(cancellationToken);

        // One timestamp for the whole scan, so that "not seen by this scan" is an
        // exact comparison rather than a window with an edge to fall through.
        var scanAt = time.GetUtcNow();
        var walked = new List<int>();

        foreach (var source in sources)
        {
            var inspection = inspector.Inspect(source.Path, DirectoryRole.Source);

            if (!inspection.Usable)
            {
                logger.LogWarning(
                    "Skipped the download directory {Path} while scanning: {Problem}",
                    source.Path,
                    inspection.Message);

                continue;
            }

            var found = await ScanSourceAsync(
                source.Id,
                inspection.Path,
                configuration.TargetDirectory,
                scanAt,
                cancellationToken);

            walked.Add(source.Id);

            logger.LogInformation("Scanned {Path}: {Found} videos.", inspection.Path, found);
        }

        // Only once every directory has been walked. A file the user dragged from
        // one watched directory to another is present throughout, and forgetting
        // it after the first walk would make it arrive again in the second — with
        // a fresh quiet period, and no memory of having settled days ago.
        var gone = await ForgetFilesNotSeenAsync(walked, scanAt, cancellationToken);

        if (gone > 0)
        {
            logger.LogInformation("{Gone} videos are no longer in the download directories.", gone);
        }
    }

    /// <summary>
    /// What the tool believes is in the download directories, with every
    /// directory looked at again as this is built. Read-only.
    /// </summary>
    public async Task<Inventory> ReadAsync(CancellationToken cancellationToken = default)
    {
        var configuration = await context.Configuration.AsNoTracking().SingleAsync(cancellationToken);

        var sources = await context.SourceDirectories
            .AsNoTracking()
            .OrderBy(source => source.Id)
            .ToListAsync(cancellationToken);

        // The same rule as Settling.HasSettled, asked of a whole table: a cutoff
        // the database can compare against, so counting a library of thousands
        // stays one query instead of thousands of rows crossing the boundary.
        var now = time.GetUtcNow();
        var settled = Settling.SettledIfUnchangedSince(now);

        var counts = await context.DiscoveredFiles
            .AsNoTracking()
            .GroupBy(file => file.SourceDirectoryId)
            .Select(group => new
            {
                SourceId = group.Key,

                // Summed rather than counted with a predicate: a conditional
                // count inside a grouping is one of the shapes EF cannot put
                // into SQL, and counting a large library in the client is the
                // one thing this query exists to avoid.
                Ready = group.Sum(file => file.SizeBytes > 0 && file.UnchangedSince <= settled ? 1 : 0),
                Total = group.Count(),
            })
            .ToDictionaryAsync(row => row.SourceId, cancellationToken);

        var recent = await context.DiscoveredFiles
            .AsNoTracking()
            .OrderByDescending(file => file.FirstSeenAt)
            .ThenBy(file => file.Path)
            .Take(Inventory.Limit)
            .ToListAsync(cancellationToken);

        var byId = sources.ToDictionary(source => source.Id, source => source.Path);

        var scanned = sources
            .Select(source =>
            {
                var inspection = inspector.Inspect(source.Path, DirectoryRole.Source);
                var count = counts.GetValueOrDefault(source.Id);

                return new ScannedSource(
                    source.Id,
                    inspection.Path,
                    inspection.Usable,
                    inspection.Message,
                    Ready: count?.Ready ?? 0,
                    Settling: (count?.Total ?? 0) - (count?.Ready ?? 0));
            })
            .ToList();

        var files = recent
            .Select(file => new ScannedFile(
                file.Id,
                file.SourceDirectoryId,
                file.Path,
                Below(byId.GetValueOrDefault(file.SourceDirectoryId), file.Path),
                file.SizeBytes,
                Settling.HasSettled(file.SizeBytes, file.UnchangedSince, now),
                file.FirstSeenAt))
            .ToList();

        return new Inventory(
            OnboardingComplete: configuration.OnboardingCompletedAt is not null,
            Sources: scanned,
            Files: files);
    }

    private async Task<int> ScanSourceAsync(
        int sourceId,
        string root,
        string? library,
        DateTimeOffset scanAt,
        CancellationToken cancellationToken)
    {
        var found = 0;

        foreach (var batch in walker.Walk(root, library, cancellationToken).Chunk(BatchSize))
        {
            found += batch.Length;

            await RecordAsync(sourceId, batch, scanAt, cancellationToken);
        }

        return found;
    }

    private async Task RecordAsync(
        int sourceId,
        IReadOnlyList<ObservedFile> batch,
        DateTimeOffset scanAt,
        CancellationToken cancellationToken)
    {
        var paths = batch.Select(observed => observed.Path).ToList();

        var known = await context.DiscoveredFiles
            .Where(file => paths.Contains(file.Path))
            .ToDictionaryAsync(file => file.Path, cancellationToken);

        foreach (var observed in batch)
        {
            if (known.TryGetValue(observed.Path, out var file))
            {
                // A file that changed goes back to the start of its quiet period.
                // This is the whole of the "wait rather than act on a growing
                // file" rule: everything else only reads the timestamp it sets.
                if (file.SizeBytes != observed.SizeBytes || file.LastWriteAt != observed.LastWriteAt)
                {
                    file.SizeBytes = observed.SizeBytes;
                    file.LastWriteAt = observed.LastWriteAt;
                    file.UnchangedSince = scanAt;
                }

                // The path is what identifies a file, and it is unique across the
                // table — so a row is looked up by path alone and follows
                // whichever directory it turned up under. Two directories cannot
                // overlap, but they can be reconfigured to, and the answer to
                // that must not be a constraint violation in the middle of a scan.
                file.SourceDirectoryId = sourceId;
                file.LastSeenAt = scanAt;

                continue;
            }

            context.DiscoveredFiles.Add(new DiscoveredFile
            {
                SourceDirectoryId = sourceId,
                Path = observed.Path,
                SizeBytes = observed.SizeBytes,
                LastWriteAt = observed.LastWriteAt,
                FirstSeenAt = scanAt,
                LastSeenAt = scanAt,
                UnchangedSince = scanAt,
            });
        }

        await context.SaveChangesAsync(cancellationToken);

        // The next batch has no use for these, and a first pass over a large
        // library would otherwise hold every row it has ever touched.
        context.ChangeTracker.Clear();
    }

    /// <summary>
    /// Removes the rows for files that were not there this time. They are gone
    /// from the download directory — moved by their owner, deleted, or renamed,
    /// which is the same thing from here.
    /// </summary>
    /// <remarks>
    /// Only for the directories this scan actually walked. A share that could not
    /// be read keeps everything the tool knew about it, because "I could not
    /// look" and "there is nothing there" are not the same answer.
    /// </remarks>
    private async Task<int> ForgetFilesNotSeenAsync(
        IEnumerable<int> walked,
        DateTimeOffset scanAt,
        CancellationToken cancellationToken)
    {
        var gone = 0;

        // One statement per directory rather than one with the whole list in it:
        // a delete matching against a collection parameter is not a shape the
        // SQLite provider translates, and there are as many of these as the user
        // has download directories.
        foreach (var sourceId in walked)
        {
            gone += await context.DiscoveredFiles
                .Where(file => file.SourceDirectoryId == sourceId && file.LastSeenAt < scanAt)
                .ExecuteDeleteAsync(cancellationToken);
        }

        return gone;
    }

    /// <summary>The part of a path below its download directory, for reading.</summary>
    private static string Below(string? root, string path)
    {
        if (string.IsNullOrEmpty(root))
        {
            return path;
        }

        var prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;

        return path.StartsWith(prefix, StringComparison.Ordinal) ? path[prefix.Length..] : path;
    }
}

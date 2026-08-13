using System.Globalization;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Ordeno.Core.Identification;
using Prdb.Ordeno.Core.Scanning;
using Prdb.Ordeno.Infrastructure.Persistence;

namespace Prdb.Ordeno.Infrastructure.Identification;

/// <summary>
/// The second half of the recognition step: take the files the scan found to
/// have finished downloading, and ask prdb what they are.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here moves, renames or writes a file the user owns. What it changes
/// is the tool's own record of what prdb said — and only ever the whole record
/// for a file at once, so there is no state where half an answer has been
/// stored.
/// </para>
/// <para>
/// A file is asked about once, not once per run. Re-asking every few minutes
/// for a library of four thousand files nobody has identified yet would spend
/// the quota on the same answer forever; the two things that make a file worth
/// asking about again are its bytes changing — which clears the answer where the
/// scan notices it — and a perceptual hash arriving that the first question did
/// not carry.
/// </para>
/// </remarks>
public sealed class IdentificationService(
    OrdenoDbContext context,
    IVideoIdentification prdb,
    IFileHashes hashes,
    TimeProvider time,
    ILogger<IdentificationService> logger)
{
    public async Task<IdentificationOutcome> IdentifyAsync(CancellationToken cancellationToken = default)
    {
        var configuration = await context.Configuration.AsNoTracking().SingleAsync(cancellationToken);

        if (configuration.OnboardingCompletedAt is null)
        {
            logger.LogDebug("Not identifying anything: onboarding has not been finished.");

            return IdentificationOutcome.Nothing;
        }

        if (string.IsNullOrWhiteSpace(configuration.PrdbApiKey))
        {
            // Onboarding cannot be finished without a key that prdb accepted, so
            // this is a row that has been edited from outside rather than a
            // state the tool can reach.
            logger.LogWarning("Not identifying anything: there is no prdb API key stored.");

            return new IdentificationOutcome(
                0,
                "There is no prdb API key stored, so nothing can be identified. Put one in under "
                + "Settings.");
        }

        var asked = 0;

        // Where the last batch got to. The cursor is what makes the loop finite:
        // a file that could not be read this time stays worth asking about, and
        // without it the next batch would be the same batch forever.
        var after = 0;

        RateLimitReading? quota = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = await NextBatchAsync(after, cancellationToken);

            if (batch.Count == 0)
            {
                return new IdentificationOutcome(asked);
            }

            // prdb reports what is left of the quota on every answer, so pacing
            // costs nothing. It is checked here rather than after the answer
            // that carried it, so that stopping is only ever said when there is
            // something left to stop for.
            if (quota is { Remaining: not null } limit
                && limit.Remaining <= IdentificationSchedule.QuotaReserve)
            {
                logger.LogInformation(
                    "Stopping this run with {Remaining} prdb requests left in the hour.",
                    limit.Remaining);

                return new IdentificationOutcome(
                    asked,
                    "prdb's hourly quota for this key is nearly spent, so the rest of the files are "
                    + "left for later.",
                    WaitUntil(limit.ResetIn));
            }

            after = batch[^1].Id;

            var ready = await WithHashesAsync(batch, cancellationToken);

            if (ready.Count == 0)
            {
                continue;
            }

            var answer = await prdb.IdentifyAsync(
                configuration.PrdbApiKey,
                [.. ready.Select(Question)],
                cancellationToken);

            if (!answer.Answered)
            {
                // Everything already stored stays stored, and the files in this
                // batch stay exactly as they were. AGENTS.md's one hard rule:
                // a failed lookup produces nothing, not a guess.
                logger.LogWarning("Identification stopped after {Asked} files: {Problem}", asked, answer.Message);

                return new IdentificationOutcome(asked, answer.Message, WaitUntil(answer.RetryAfter));
            }

            await StoreAsync(answer.Results, ready, cancellationToken);

            asked += ready.Count;
            quota = answer.RateLimit;

            logger.LogInformation("Asked prdb about {Count} files.", ready.Count);
        }
    }

    /// <summary>
    /// The next files worth asking about: settled, and either never asked about
    /// or asked about before their perceptual hash existed.
    /// </summary>
    /// <remarks>
    /// Written as "no identification says otherwise" rather than as a join, so
    /// that it stays one query over a table that is mostly files already dealt
    /// with.
    /// </remarks>
    private async Task<List<DiscoveredFile>> NextBatchAsync(int after, CancellationToken cancellationToken)
    {
        var settled = Settling.SettledIfUnchangedSince(time.GetUtcNow());

        return await context.DiscoveredFiles
            .Where(file =>
                file.Id > after
                && file.SizeBytes > 0
                && file.UnchangedSince <= settled
                && !context.FileIdentifications.Any(identification =>
                    identification.DiscoveredFileId == file.Id
                    && (identification.AskedWithPerceptualHash || file.PerceptualHash == null)))
            .OrderBy(file => file.Id)
            .Take(IdentificationSchedule.MaxBatch)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Computes the exact hash for anything that has not got one, and drops the
    /// files that could not be read.
    /// </summary>
    /// <remarks>
    /// A file that is locked or has just gone is left out of the question
    /// entirely rather than asked about without its hash. Asking without it
    /// would get an answer off a lower rung and store it, and the file would
    /// never be asked about again — a permanent worse answer bought with one
    /// busy moment.
    /// </remarks>
    private async Task<List<DiscoveredFile>> WithHashesAsync(
        List<DiscoveredFile> batch,
        CancellationToken cancellationToken)
    {
        var ready = new List<DiscoveredFile>(batch.Count);

        foreach (var file in batch)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (file.OsHash is not null)
            {
                ready.Add(file);

                continue;
            }

            var reading = hashes.OsHashOf(file.Path);

            switch (reading.State)
            {
                case OsHashState.Computed:
                    file.OsHash = reading.Hash;
                    ready.Add(file);

                    break;

                // Under 128 KiB. It has no hash and never will, and it is
                // identified by its name or not at all.
                case OsHashState.TooSmall:
                    ready.Add(file);

                    break;

                default:
                    logger.LogDebug("Leaving {Path} for the next run: it could not be read.", file.Path);

                    break;
            }
        }

        await context.SaveChangesAsync(cancellationToken);

        return ready;
    }

    /// <summary>
    /// The client-assigned <c>ref</c> the answer is mapped back by. The row's
    /// own id, because it is the one thing that is unique and still there when
    /// the answer arrives.
    /// </summary>
    private static string Reference(DiscoveredFile file) =>
        file.Id.ToString(CultureInfo.InvariantCulture);

    private static FileToIdentify Question(DiscoveredFile file) => new(
        Reference(file),

        // The name, not the path. prdb reads a release name out of it, and the
        // directories on somebody's NAS are not part of the question.
        Path.GetFileName(file.Path),
        file.SizeBytes,
        file.OsHash,
        file.PerceptualHash);

    private async Task StoreAsync(
        IReadOnlyList<RecognisedFile> results,
        IReadOnlyList<DiscoveredFile> asked,
        CancellationToken cancellationToken)
    {
        var byRef = asked.ToDictionary(Reference, StringComparer.Ordinal);

        var ids = asked.Select(file => file.Id).ToList();

        var stored = await context.FileIdentifications
            .Include(identification => identification.Candidates)
            .Where(identification => ids.Contains(identification.DiscoveredFileId))
            .ToDictionaryAsync(identification => identification.DiscoveredFileId, cancellationToken);

        var at = time.GetUtcNow();

        foreach (var result in results)
        {
            if (!byRef.TryGetValue(result.Ref, out var file))
            {
                logger.LogWarning("prdb answered for {Ref}, which this run did not ask about.", result.Ref);

                continue;
            }

            if (!stored.TryGetValue(file.Id, out var identification))
            {
                identification = new FileIdentification { DiscoveredFileId = file.Id };
                context.FileIdentifications.Add(identification);
            }
            else
            {
                // The answer is replaced whole. A candidate left over from an
                // earlier, more uncertain answer would read as part of this one.
                identification.Candidates.Clear();
            }

            identification.AskedAt = at;
            identification.Confidence = result.Confidence;
            identification.MatchedBy = result.MatchedBy;
            identification.VideoId = result.VideoId;
            identification.Title = result.Title;
            identification.ReleaseDate = result.ReleaseDate;
            identification.SiteId = result.SiteId;
            identification.SiteTitle = result.SiteTitle;
            identification.AskedWithPerceptualHash = file.PerceptualHash is not null;

            for (var position = 0; position < result.Candidates.Count; position++)
            {
                identification.Candidates.Add(new IdentificationCandidate
                {
                    Position = position,
                    VideoId = result.Candidates[position],
                });
            }
        }

        await context.SaveChangesAsync(cancellationToken);

        // The next batch has no use for any of this, and a first pass over a
        // large library would otherwise hold every row it has touched.
        context.ChangeTracker.Clear();
    }

    private DateTimeOffset? WaitUntil(TimeSpan? wait) =>
        wait is null ? null : time.GetUtcNow() + wait.Value;
}

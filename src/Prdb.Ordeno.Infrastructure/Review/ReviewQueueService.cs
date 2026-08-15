using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Ordeno.Core.Identification;
using Prdb.Ordeno.Core.Review;
using Prdb.Ordeno.Infrastructure.Persistence;

namespace Prdb.Ordeno.Infrastructure.Review;

/// <summary>
/// The last rung of the ladder, which is a person: what prdb could not settle,
/// and what somebody decided about it.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here moves, renames or deletes a file. It reads what is waiting and
/// writes down answers — ADR 0023 — and filing reads those answers the next time
/// somebody asks for a run.
/// </para>
/// <para>
/// It asks prdb two things, both on behalf of the person in front of the screen:
/// what the candidates are, so that choosing between them is reading rather than
/// guessing, and what matches a search. Neither is an identification and neither
/// is stored as one.
/// </para>
/// </remarks>
public sealed class ReviewQueueService(
    OrdenoDbContext context,
    IVideoLookup videos,
    TimeProvider time,
    ILogger<ReviewQueueService> logger)
{
    /// <summary>How many search results one press of the button brings back.</summary>
    public const int SearchResults = 20;

    /// <summary>
    /// One page of the queue, with the counts for the whole of it.
    /// </summary>
    /// <param name="site">
    /// Narrows to one site, or to the files no site could be read from when
    /// <paramref name="noSite"/> says so. The two are different questions and a
    /// null cannot ask both.
    /// </param>
    public async Task<ReviewQueue> ReadAsync(
        ReviewFilter filter = ReviewFilter.Waiting,
        Guid? site = null,
        bool noSite = false,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        var configuration = await context.Configuration.AsNoTracking().SingleAsync(cancellationToken);

        if (configuration.OnboardingCompletedAt is null)
        {
            return ReviewQueue.Empty(
                filter,
                site,
                "Nothing is in the queue until the setup is finished.");
        }

        var wanted = Math.Max(1, page);

        var rows = Filtered(filter, site, noSite);

        var total = await rows.CountAsync(cancellationToken);

        // Asking for page nine of eight is what happens when somebody empties the
        // last page: the answer is the last page there is, not an empty screen
        // with a "next" button on it.
        var pages = total == 0 ? 1 : (total + ReviewQueue.PageSize - 1) / ReviewQueue.PageSize;
        var shown = Math.Min(wanted, pages);

        var found = await rows
            .OrderBy(row => row.File.Id)
            .Skip((shown - 1) * ReviewQueue.PageSize)
            .Take(ReviewQueue.PageSize)
            .Select(row => new
            {
                row.File.Id,
                row.File.SourceDirectoryId,
                row.File.Path,
                row.File.SizeBytes,
                row.File.FirstSeenAt,
                Recognition = new Recognition(
                    row.Identification.Confidence,
                    row.Identification.MatchedBy,
                    row.Identification.VideoId,
                    row.Identification.Title,
                    row.Identification.ReleaseDate,
                    row.Identification.SiteTitle,
                    row.Identification.Candidates.Count,
                    row.Identification.AskedAt),
                Decision = row.Decision == null
                    ? null
                    : new Resolution(
                        row.Decision.Kind,
                        row.Decision.From,
                        row.Decision.DecidedAt,
                        row.Decision.VideoId,
                        row.Decision.Title,
                        row.Decision.ReleaseDate,
                        row.Decision.SiteTitle),
            })
            .ToListAsync(cancellationToken);

        var sources = await SourcesAsync(cancellationToken);
        var candidates = await CandidatesAsync(
            // Empty is the same as absent here. Asking prdb with a key that is
            // not one spends a request to be told so, on a screen somebody is
            // waiting in front of.
            string.IsNullOrWhiteSpace(configuration.PrdbApiKey) ? null : configuration.PrdbApiKey,
            [.. found.Select(row => row.Id)],
            cancellationToken);

        return new ReviewQueue(
            filter,
            site,
            [
                .. found.Select(row => new ReviewEntry(
                    row.Id,
                    Below(sources.GetValueOrDefault(row.SourceDirectoryId), row.Path),
                    row.Path,
                    row.SizeBytes,
                    row.FirstSeenAt,
                    row.Recognition,
                    candidates.Described.GetValueOrDefault(row.Id, []),
                    row.Decision)),
            ],
            shown,
            total,
            await SummariseAsync(cancellationToken),
            await SitesAsync(cancellationToken),
            candidates.Problem);
    }

    /// <summary>
    /// The videos matching what somebody typed. It spends a request against their
    /// prdb quota every time, which is what a search is.
    /// </summary>
    public async Task<VideoLookupAnswer> SearchAsync(
        string query,
        Guid? site = null,
        CancellationToken cancellationToken = default)
    {
        var apiKey = await ApiKeyAsync(cancellationToken);

        return apiKey is null
            ? VideoLookupAnswer.Stopped(
                "There is no prdb API key stored, so nothing can be looked up. Put one in under "
                + "Settings.")
            : await videos.SearchAsync(apiKey, query, site, 1, SearchResults, cancellationToken);
    }

    /// <summary>
    /// A person names the video. What the video is comes from prdb rather than
    /// from the browser: these words become a directory name and a file name, and
    /// ADR 0023 draws the line at "the id is an identifier, everything that turns
    /// into a name is fetched".
    /// </summary>
    public async Task<ReviewDecision> AssignAsync(
        int fileId,
        Guid videoId,
        CancellationToken cancellationToken = default)
    {
        var file = await context.DiscoveredFiles
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == fileId, cancellationToken);

        if (file is null)
        {
            return ReviewDecision.Refused(
                "That file is not in a download directory any more, so there is nothing to decide "
                + "about. It was moved, renamed or deleted.",
                await SummariseAsync(cancellationToken));
        }

        var apiKey = await ApiKeyAsync(cancellationToken);

        if (apiKey is null)
        {
            return ReviewDecision.Refused(
                "There is no prdb API key stored, so the tool cannot check what that video is. Put "
                + "one in under Settings.",
                await SummariseAsync(cancellationToken));
        }

        var answer = await videos.DescribeAsync(apiKey, [videoId], cancellationToken);

        if (!answer.Answered)
        {
            return ReviewDecision.Refused(answer.Message!, await SummariseAsync(cancellationToken));
        }

        if (answer.Videos.FirstOrDefault(video => video.VideoId == videoId) is not { } named)
        {
            return ReviewDecision.Refused(
                "prdb does not know that video, so nothing was written down. It may have been "
                + "merged into another one since the tool was told about it.",
                await SummariseAsync(cancellationToken));
        }

        // Which of the two it was is worked out here rather than taken from the
        // request: the row says how somebody arrived at a video, and a page
        // claiming to have confirmed a candidate it never showed would make that
        // record worth nothing.
        var from = await context.FileIdentifications
            .AsNoTracking()
            .AnyAsync(
                identification => identification.DiscoveredFileId == fileId
                    && identification.Candidates.Any(candidate => candidate.VideoId == videoId),
                cancellationToken)
            ? ResolvedFrom.Candidate
            : ResolvedFrom.Search;

        await RecordAsync(
            fileId,
            resolution =>
            {
                resolution.Kind = ResolutionKind.Assigned;
                resolution.From = from;
                resolution.VideoId = named.VideoId;
                resolution.Title = named.Title;
                resolution.ReleaseDate = named.ReleaseDate;
                resolution.SiteId = named.SiteId;
                resolution.SiteTitle = named.SiteTitle;
            },
            cancellationToken);

        logger.LogInformation("File {FileId} was assigned to video {VideoId} by hand.", fileId, videoId);

        return await DecidedAsync(fileId, cancellationToken);
    }

    /// <summary>
    /// A person says this one is not to be filed. Nothing is deleted and nothing
    /// is hidden from the inventory — the file stops being offered, and that is
    /// the whole of it.
    /// </summary>
    public async Task<ReviewDecision> DismissAsync(
        int fileId,
        CancellationToken cancellationToken = default)
    {
        if (!await context.DiscoveredFiles.AnyAsync(row => row.Id == fileId, cancellationToken))
        {
            return ReviewDecision.Refused(
                "That file is not in a download directory any more, so there is nothing to dismiss.",
                await SummariseAsync(cancellationToken));
        }

        await RecordAsync(fileId, Dismissal, cancellationToken);

        return await DecidedAsync(fileId, cancellationToken);
    }

    /// <summary>
    /// The first day's version of the same thing. A thousand files that are all
    /// samples are one decision somebody makes once, and a queue that can only be
    /// worked a row at a time is a queue nobody empties.
    /// </summary>
    /// <remarks>
    /// Files that have gone in the meantime are skipped rather than refused: this
    /// is a selection somebody made on a screen a minute ago, and one row of it
    /// being stale is not a reason to leave the other nine hundred undone.
    /// </remarks>
    public async Task<ReviewDecision> DismissAsync(
        IReadOnlyList<int> fileIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileIds);

        var wanted = fileIds.Distinct().ToList();

        var present = await context.DiscoveredFiles
            .AsNoTracking()
            .Where(row => wanted.Contains(row.Id))
            .Select(row => row.Id)
            .ToListAsync(cancellationToken);

        foreach (var fileId in present)
        {
            await RecordAsync(fileId, Dismissal, cancellationToken);
        }

        logger.LogInformation("{Count} files were dismissed by hand.", present.Count);

        var summary = await SummariseAsync(cancellationToken);

        return new ReviewDecision(
            present.Count > 0,
            null,
            summary,
            present.Count == wanted.Count
                ? null
                : $"{wanted.Count - present.Count} of them are not in a download directory any more, "
                    + "so they were left alone.");
    }

    /// <summary>
    /// Undoes a decision: the file goes back to waiting for one. It is the way
    /// back from a wrong button, and the reason a dismissal never needed to
    /// delete anything.
    /// </summary>
    public async Task<ReviewDecision> ForgetAsync(
        int fileId,
        CancellationToken cancellationToken = default)
    {
        await context.FileResolutions
            .Where(resolution => resolution.DiscoveredFileId == fileId)
            .ExecuteDeleteAsync(cancellationToken);

        return await DecidedAsync(fileId, cancellationToken);
    }

    private static void Dismissal(FileResolution resolution)
    {
        resolution.Kind = ResolutionKind.Dismissed;

        // A dismissal came from neither a candidate nor a search, and it names no
        // video. Cleared rather than left, because this may be replacing an
        // assignment somebody has thought better of.
        resolution.From = null;
        resolution.VideoId = null;
        resolution.Title = null;
        resolution.ReleaseDate = null;
        resolution.SiteId = null;
        resolution.SiteTitle = null;
    }

    /// <summary>
    /// Writes the one decision this file has. A second one replaces the first:
    /// two answers from one person about one file, with no way to tell which is
    /// current, is the state the unique index exists to prevent.
    /// </summary>
    private async Task RecordAsync(
        int fileId,
        Action<FileResolution> decide,
        CancellationToken cancellationToken)
    {
        var resolution = await context.FileResolutions
            .FirstOrDefaultAsync(row => row.DiscoveredFileId == fileId, cancellationToken);

        if (resolution is null)
        {
            resolution = new FileResolution { DiscoveredFileId = fileId };
            context.FileResolutions.Add(resolution);
        }

        resolution.DecidedAt = time.GetUtcNow();
        decide(resolution);

        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();
    }

    /// <summary>
    /// The row as it now is, with the counts. Read back rather than assembled
    /// from what was just written, so that what the screen shows is what the
    /// database holds.
    /// </summary>
    private async Task<ReviewDecision> DecidedAsync(int fileId, CancellationToken cancellationToken)
    {
        var summary = await SummariseAsync(cancellationToken);

        var row = await Rows()
            .Where(row => row.File.Id == fileId)
            .Select(row => new
            {
                row.File.SourceDirectoryId,
                row.File.Path,
                row.File.SizeBytes,
                row.File.FirstSeenAt,
                Recognition = new Recognition(
                    row.Identification.Confidence,
                    row.Identification.MatchedBy,
                    row.Identification.VideoId,
                    row.Identification.Title,
                    row.Identification.ReleaseDate,
                    row.Identification.SiteTitle,
                    row.Identification.Candidates.Count,
                    row.Identification.AskedAt),
                Decision = row.Decision == null
                    ? null
                    : new Resolution(
                        row.Decision.Kind,
                        row.Decision.From,
                        row.Decision.DecidedAt,
                        row.Decision.VideoId,
                        row.Decision.Title,
                        row.Decision.ReleaseDate,
                        row.Decision.SiteTitle),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return new ReviewDecision(true, null, summary);
        }

        var sources = await SourcesAsync(cancellationToken);

        return new ReviewDecision(
            true,
            new ReviewEntry(
                fileId,
                Below(sources.GetValueOrDefault(row.SourceDirectoryId), row.Path),
                row.Path,
                row.SizeBytes,
                row.FirstSeenAt,
                row.Recognition,
                [],
                row.Decision),
            summary);
    }

    /// <summary>
    /// Every file prdb has answered about, with what a person has since said —
    /// which is nothing, for most of them. A left join rather than two queries
    /// because "waiting" is the absence of the second row.
    /// </summary>
    private IQueryable<QueuedRow> Rows() =>
        from file in context.DiscoveredFiles.AsNoTracking()
        join identification in context.FileIdentifications.AsNoTracking()
            on file.Id equals identification.DiscoveredFileId
        join resolution in context.FileResolutions.AsNoTracking()
            on file.Id equals resolution.DiscoveredFileId into decisions
        from decision in decisions.DefaultIfEmpty()
        select new QueuedRow
        {
            File = file,
            Identification = identification,
            Decision = decision,
        };

    private IQueryable<QueuedRow> Filtered(ReviewFilter filter, Guid? site, bool noSite)
    {
        var rows = filter switch
        {
            // Everything prdb answered about but could not name a video for:
            // several matches, the site alone, or nothing at all. ADR 0019 keeps
            // all three out of the library and sends them here.
            ReviewFilter.Waiting => Rows()
                .Where(row => row.Identification.VideoId == null && row.Decision == null),

            ReviewFilter.Assigned => Rows()
                .Where(row => row.Decision != null && row.Decision.Kind == ResolutionKind.Assigned),

            _ => Rows()
                .Where(row => row.Decision != null && row.Decision.Kind == ResolutionKind.Dismissed),
        };

        if (noSite)
        {
            return rows.Where(row => row.Identification.SiteId == null);
        }

        return site is { } wanted
            ? rows.Where(row => row.Identification.SiteId == wanted)
            : rows;
    }

    /// <summary>
    /// The counts, over the whole table rather than over the page. Summed with a
    /// ternary rather than counted with a predicate, for the reason the scan's
    /// counts are: a conditional count inside a grouping is not a shape EF puts
    /// into SQL.
    /// </summary>
    private async Task<ReviewSummary> SummariseAsync(CancellationToken cancellationToken)
    {
        var counts = await Rows()
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Waiting = group.Sum(row =>
                    row.Decision == null && row.Identification.VideoId == null ? 1 : 0),
                Ambiguous = group.Sum(row =>
                    row.Decision == null
                    && row.Identification.VideoId == null
                    && row.Identification.Confidence == MatchConfidence.Ambiguous ? 1 : 0),
                SiteOnly = group.Sum(row =>
                    row.Decision == null
                    && row.Identification.VideoId == null
                    && row.Identification.Confidence != MatchConfidence.Ambiguous
                    && row.Identification.MatchedBy == MatchRung.Site ? 1 : 0),
                Assigned = group.Sum(row =>
                    row.Decision != null && row.Decision.Kind == ResolutionKind.Assigned ? 1 : 0),
                Dismissed = group.Sum(row =>
                    row.Decision != null && row.Decision.Kind == ResolutionKind.Dismissed ? 1 : 0),
            })
            .FirstOrDefaultAsync(cancellationToken);

        return counts is null
            ? ReviewSummary.Nothing
            : new ReviewSummary(
                counts.Ambiguous,
                counts.SiteOnly,
                Unrecognised: counts.Waiting - counts.Ambiguous - counts.SiteOnly,
                counts.Assigned,
                counts.Dismissed);
    }

    /// <summary>
    /// How the waiting files divide up by site, over all of them rather than over
    /// the page: the filter has to know about the site on page eighty.
    /// </summary>
    private async Task<IReadOnlyList<ReviewSite>> SitesAsync(CancellationToken cancellationToken)
    {
        var sites = await Filtered(ReviewFilter.Waiting, null, noSite: false)
            .GroupBy(row => new { row.Identification.SiteId, row.Identification.SiteTitle })
            .Select(group => new
            {
                group.Key.SiteId,
                group.Key.SiteTitle,
                Waiting = group.Count(),
            })
            .ToListAsync(cancellationToken);

        return
        [
            .. sites
                .OrderByDescending(site => site.Waiting)
                .ThenBy(site => site.SiteTitle, StringComparer.OrdinalIgnoreCase)
                .Select(site => new ReviewSite(site.SiteId, site.SiteTitle, site.Waiting)),
        ];
    }

    /// <summary>
    /// The candidates for the rows on this page, with words on them. Ids prdb has
    /// not been asked about yet are asked about now, in batches of fifty, and the
    /// answer is kept — ADR 0017's bargain, applied to the one part of an
    /// identification that arrives without words.
    /// </summary>
    private async Task<DescribedCandidates> CandidatesAsync(
        string? apiKey,
        IReadOnlyList<int> fileIds,
        CancellationToken cancellationToken)
    {
        if (fileIds.Count == 0)
        {
            return new DescribedCandidates([], null);
        }

        var rows = await context.FileIdentifications
            .Where(identification => fileIds.Contains(identification.DiscoveredFileId))
            .SelectMany(
                identification => identification.Candidates,
                (identification, candidate) => new { identification.DiscoveredFileId, Candidate = candidate })
            .OrderBy(row => row.Candidate.Position)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return new DescribedCandidates([], null);
        }

        var problem = apiKey is null
            ? null
            : await DescribeAsync(apiKey, [.. rows.Select(row => row.Candidate)], cancellationToken);

        var described = rows
            .GroupBy(row => row.DiscoveredFileId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ReviewCandidate>)
                [
                    .. group.Select(row => new ReviewCandidate(
                        row.Candidate.VideoId,
                        Summary(row.Candidate))),
                ]);

        context.ChangeTracker.Clear();

        return new DescribedCandidates(described, problem);
    }

    /// <summary>
    /// Asks prdb what the candidates nobody has looked up yet are, and writes the
    /// answer next to them.
    /// </summary>
    /// <returns>
    /// What stopped it, or <c>null</c>. It is a line on the screen rather than a
    /// failure: a queue whose buttons say less than they could is still a queue,
    /// and the ids are what a decision is made of.
    /// </returns>
    private async Task<string?> DescribeAsync(
        string apiKey,
        IReadOnlyList<IdentificationCandidate> candidates,
        CancellationToken cancellationToken)
    {
        var wanted = candidates
            .Where(candidate => candidate.DescribedAt is null)
            .Select(candidate => candidate.VideoId)
            .Distinct()
            .ToList();

        if (wanted.Count == 0)
        {
            return null;
        }

        var known = new Dictionary<Guid, VideoSummary>();

        foreach (var batch in wanted.Chunk(IVideoLookup.MaxBatch))
        {
            var answer = await videos.DescribeAsync(apiKey, batch, cancellationToken);

            if (!answer.Answered)
            {
                logger.LogWarning(
                    "The queue could not be told what its candidates are: {Problem}",
                    answer.Message);

                return answer.Message;
            }

            foreach (var video in answer.Videos)
            {
                known[video.VideoId] = video;
            }
        }

        var at = time.GetUtcNow();

        foreach (var candidate in candidates.Where(candidate => candidate.DescribedAt is null))
        {
            // Stamped whether or not prdb knew the id. Without that, a candidate
            // prdb has since merged away would be asked about on every page view
            // for as long as the file sits in the queue.
            candidate.DescribedAt = at;

            if (!known.TryGetValue(candidate.VideoId, out var video))
            {
                continue;
            }

            candidate.Title = video.Title;
            candidate.ReleaseDate = video.ReleaseDate;
            candidate.SiteTitle = video.SiteTitle;
            candidate.Performers = video.Performers.Count == 0 ? null : string.Join(", ", video.Performers);
        }

        await context.SaveChangesAsync(cancellationToken);

        return null;
    }

    /// <summary>
    /// What is stored about one candidate, or <c>null</c> when there is nothing
    /// to say — either prdb has not been asked yet, or it did not know the id.
    /// </summary>
    /// <remarks>
    /// The performers come back as one line rather than as a list. That is how
    /// the column stores them, and it is all a button needs: nothing here
    /// searches or counts by performer, and a table of prdb's people is a corpus
    /// this store does not keep (ADR 0001).
    /// </remarks>
    private static VideoSummary? Summary(IdentificationCandidate candidate) =>
        candidate.DescribedAt is null || candidate.Title is null
            ? null
            : new VideoSummary(
                candidate.VideoId,
                candidate.Title,
                candidate.ReleaseDate,
                SiteId: null,
                candidate.SiteTitle,
                candidate.Performers is null ? [] : [candidate.Performers]);

    private async Task<string?> ApiKeyAsync(CancellationToken cancellationToken)
    {
        var configuration = await context.Configuration.AsNoTracking().SingleAsync(cancellationToken);

        return string.IsNullOrWhiteSpace(configuration.PrdbApiKey) ? null : configuration.PrdbApiKey;
    }

    private async Task<Dictionary<int, string>> SourcesAsync(CancellationToken cancellationToken) =>
        await context.SourceDirectories
            .AsNoTracking()
            .ToDictionaryAsync(source => source.Id, source => source.Path, cancellationToken);

    /// <summary>The path below its download directory, which is what a person recognises.</summary>
    private static string Below(string? source, string path) =>
        source is not null && path.StartsWith(source, StringComparison.Ordinal)
            ? path[source.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : Path.GetFileName(path);

    /// <summary>
    /// One file, what prdb said about it, and what a person has since said.
    /// </summary>
    /// <remarks>
    /// Assembled member by member rather than through a constructor, because that
    /// is the shape the query translator can see into: a filter on
    /// <see cref="Decision"/> has to reach the join it came from, and a
    /// constructor argument is opaque to it.
    /// </remarks>
    private sealed class QueuedRow
    {
        public required DiscoveredFile File { get; init; }

        public required FileIdentification Identification { get; init; }

        /// <summary>Null for a file nobody has decided about, which is what "waiting" means.</summary>
        public FileResolution? Decision { get; init; }
    }

    private sealed record DescribedCandidates(
        Dictionary<int, IReadOnlyList<ReviewCandidate>> Described,
        string? Problem);
}

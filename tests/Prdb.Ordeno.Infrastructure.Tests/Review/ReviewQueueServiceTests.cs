using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Prdb.Ordeno.Core.Configuration;
using Prdb.Ordeno.Core.Identification;
using Prdb.Ordeno.Core.Review;
using Prdb.Ordeno.Core.Scanning;
using Prdb.Ordeno.Infrastructure.Identification;
using Prdb.Ordeno.Infrastructure.Persistence;
using Prdb.Ordeno.Infrastructure.Review;
using Prdb.Ordeno.Infrastructure.Scanning;
using Prdb.Ordeno.Infrastructure.Tests.Identification;
using Prdb.Ordeno.Infrastructure.Tests.Scanning;

using Xunit;

namespace Prdb.Ordeno.Infrastructure.Tests.Review;

/// <summary>
/// Issue #16 against a real filesystem and a real SQLite file: what waits for a
/// person, what happens when they answer, and what the answer survives.
/// </summary>
/// <remarks>
/// Nothing in this milestone moves a file either. It ends with every file having
/// an answer — from prdb or from a person — and none of them moved.
/// </remarks>
public sealed class ReviewQueueServiceTests : IAsyncLifetime
{
    private readonly TempDirectory directory = new();
    private readonly TestTime time = new();
    private readonly FakePrdbIdentification prdb = new();
    private readonly FakeVideoLookup lookup = new();

    private ServiceProvider services = null!;
    private string downloads = null!;

    public async Task InitializeAsync()
    {
        downloads = Directory.CreateDirectory(directory.Combine("downloads")).FullName;

        var collection = new ServiceCollection();
        collection.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        collection.AddSingleton<TimeProvider>(time);

        // Registered before the slices, whose registrations are TryAdd. What a
        // test decides is what prdb says; everything between here and the
        // database is the real thing.
        collection.AddSingleton<IVideoIdentification>(prdb);
        collection.AddSingleton<IVideoLookup>(lookup);

        collection.AddOrdenoPersistence(directory.Combine("data"));
        collection.AddOrdenoScanning();
        collection.AddOrdenoIdentification();
        collection.AddOrdenoReview();

        services = collection.BuildServiceProvider();

        await services.PrepareOrdenoDatabaseAsync();
        await ConfigureAsync();
    }

    public async Task DisposeAsync()
    {
        await services.DisposeAsync();
        directory.Dispose();
    }

    /// <summary>
    /// The three answers ADR 0019 keeps out of the library all arrive here, and a
    /// recognised file does not. The queue is what prdb could not settle.
    /// </summary>
    [Fact]
    public async Task What_prdb_could_not_settle_is_what_waits()
    {
        var candidates = new[] { Guid.NewGuid(), Guid.NewGuid() };

        await ArrivedAsync("ambiguous.mkv");
        await ArrivedAsync("site-only.mkv");
        await ArrivedAsync("nothing.mkv");
        await ArrivedAsync("recognised.mkv");

        await IdentifiedAsync(file => Path.GetFileName(file.FileName) switch
        {
            "ambiguous.mkv" => Answer(file.Ref, MatchConfidence.Ambiguous, MatchRung.ReleaseName, candidates: candidates),
            "site-only.mkv" => Answer(file.Ref, MatchConfidence.Partial, MatchRung.Site, site: "A Site"),
            "recognised.mkv" => Answer(file.Ref, MatchConfidence.Exact, MatchRung.OsHash, videoId: Guid.NewGuid()),
            _ => Answer(file.Ref, MatchConfidence.None, null),
        });

        var queue = await ReadAsync();

        Assert.Equal(3, queue.Total);
        Assert.Equal(3, queue.Summary.Waiting);
        Assert.Equal(1, queue.Summary.Ambiguous);
        Assert.Equal(1, queue.Summary.SiteOnly);
        Assert.Equal(1, queue.Summary.Unrecognised);
        Assert.DoesNotContain(queue.Entries, entry => entry.Name == "recognised.mkv");
    }

    /// <summary>
    /// The queue's fastest case: three candidates are three buttons. prdb names
    /// them as ids, so the words have to come from somewhere — once, and then
    /// from the database.
    /// </summary>
    [Fact]
    public async Task Candidates_are_described_once_and_then_read_from_the_database()
    {
        var first = lookup.Knows("The First One", performers: "Anna Example");
        var second = lookup.Knows("The Second One", performers: "Bea Example");

        await ArrivedAsync("ambiguous.mkv");
        await IdentifiedAsync(file => Answer(
            file.Ref,
            MatchConfidence.Ambiguous,
            MatchRung.ReleaseName,
            candidates: [first.VideoId, second.VideoId]));

        var entry = Assert.Single((await ReadAsync()).Entries);

        Assert.Equal([first.VideoId, second.VideoId], entry.Candidates.Select(candidate => candidate.VideoId));
        Assert.Contains("The First One", entry.Candidates[0].InWords, StringComparison.Ordinal);
        Assert.Contains("Anna Example", entry.Candidates[0].InWords, StringComparison.Ordinal);

        // And reading it again asks prdb nothing at all.
        var described = lookup.Described.Count;

        Assert.Contains("The Second One", (await ReadAsync()).Entries[0].Candidates[1].InWords, StringComparison.Ordinal);
        Assert.Equal(described, lookup.Described.Count);
    }

    /// <summary>
    /// prdb being unreachable is a line on the screen, not an empty queue. The
    /// ids are what a decision is made of, and the words only make it quick.
    /// </summary>
    [Fact]
    public async Task A_queue_whose_candidates_cannot_be_described_still_works()
    {
        var candidate = Guid.NewGuid();

        await ArrivedAsync("ambiguous.mkv");
        await IdentifiedAsync(file => Answer(
            file.Ref,
            MatchConfidence.Ambiguous,
            MatchRung.ReleaseName,
            candidates: [candidate]));

        lookup.Stopped = "prdb could not be reached.";

        var queue = await ReadAsync();
        var entry = Assert.Single(queue.Entries);

        Assert.Equal("prdb could not be reached.", queue.Problem);
        Assert.Equal(candidate, Assert.Single(entry.Candidates).VideoId);
        Assert.Null(entry.Candidates[0].Video);
    }

    /// <summary>
    /// ADR 0023: the browser names a video and the tool asks prdb what it is. The
    /// title becomes a directory name, and a path built from what a page posted
    /// is a path built from unvalidated input.
    /// </summary>
    [Fact]
    public async Task Assigning_stores_what_prdb_says_the_video_is()
    {
        var video = lookup.Knows("A Scene Somebody Found", "Their Site");

        await ArrivedAsync("mystery.mkv");
        await IdentifiedAsync(file => Answer(file.Ref, MatchConfidence.None, null));

        var fileId = (await ReadAsync()).Entries[0].FileId;
        var decision = await AssignAsync(fileId, video.VideoId);

        Assert.True(decision.Made);
        Assert.NotNull(decision.Entry?.Decision);
        Assert.Equal(ResolutionKind.Assigned, decision.Entry.Decision.Kind);
        Assert.Equal("A Scene Somebody Found", decision.Entry.Decision.Title);
        Assert.Equal("Their Site", decision.Entry.Decision.SiteTitle);
        Assert.Equal(time.GetUtcNow(), decision.Entry.Decision.DecidedAt);

        // It came from a search rather than from a candidate, and the row says so
        // — worked out here rather than taken from the request.
        Assert.Equal(ResolvedFrom.Search, decision.Entry.Decision.From);

        var queue = await ReadAsync();

        Assert.Empty(queue.Entries);
        Assert.Equal(1, queue.Summary.Assigned);
    }

    /// <summary>
    /// The same decision reached the other way. Confirming one of the candidates
    /// prdb offered is different evidence from finding a video it did not, and
    /// that difference is what an assignment contributed back would rest on.
    /// </summary>
    [Fact]
    public async Task Accepting_a_candidate_is_recorded_as_a_candidate()
    {
        var video = lookup.Knows("One Of Two");

        await ArrivedAsync("ambiguous.mkv");
        await IdentifiedAsync(file => Answer(
            file.Ref,
            MatchConfidence.Ambiguous,
            MatchRung.ReleaseName,
            candidates: [video.VideoId, Guid.NewGuid()]));

        var fileId = (await ReadAsync()).Entries[0].FileId;
        var decision = await AssignAsync(fileId, video.VideoId);

        Assert.Equal(ResolvedFrom.Candidate, decision.Entry?.Decision?.From);
    }

    /// <summary>
    /// prdb not knowing the id is a refusal rather than a row with no title on
    /// it. A resolution that names nothing would be a scene the layout cannot
    /// name, arrived at deliberately.
    /// </summary>
    [Fact]
    public async Task A_video_prdb_does_not_know_is_not_written_down()
    {
        await ArrivedAsync("mystery.mkv");
        await IdentifiedAsync(file => Answer(file.Ref, MatchConfidence.None, null));

        var fileId = (await ReadAsync()).Entries[0].FileId;
        var decision = await AssignAsync(fileId, Guid.NewGuid());

        Assert.False(decision.Made);
        Assert.Contains("does not know", decision.Problem!, StringComparison.Ordinal);
        Assert.Equal(1, (await ReadAsync()).Total);
    }

    /// <summary>
    /// Saying no is an answer. The file stays where it is, stays in the
    /// inventory, and stops being offered.
    /// </summary>
    [Fact]
    public async Task A_dismissed_file_stays_on_disk_and_stops_being_offered()
    {
        await ArrivedAsync("sample.mkv");
        await IdentifiedAsync(file => Answer(file.Ref, MatchConfidence.None, null));

        var fileId = (await ReadAsync()).Entries[0].FileId;

        Assert.True((await DismissAsync(fileId)).Made);

        var queue = await ReadAsync();

        Assert.Empty(queue.Entries);
        Assert.Equal(1, queue.Summary.Dismissed);
        Assert.True(File.Exists(Path.Combine(downloads, "sample.mkv")));

        // Still known, still counted: dismissing is not deleting or hiding.
        await using var scope = services.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<OrdenoDbContext>()
            .DiscoveredFiles
            .CountAsync());

        var dismissed = await ReadAsync(ReviewFilter.Dismissed);

        Assert.Equal("sample.mkv", Assert.Single(dismissed.Entries).Name);
    }

    /// <summary>
    /// Across restarts and rescans, which is the same thing from here: the row
    /// is what remembers, and a scan that finds the file unchanged leaves it
    /// alone.
    /// </summary>
    [Fact]
    public async Task A_dismissal_survives_the_next_scan_and_the_next_identification()
    {
        await ArrivedAsync("sample.mkv");
        await IdentifiedAsync(file => Answer(file.Ref, MatchConfidence.None, null));

        await DismissAsync((await ReadAsync()).Entries[0].FileId);

        time.Advance(Settling.QuietPeriod);
        await ScanAsync();
        await IdentifyAsync();

        var queue = await ReadAsync();

        Assert.Empty(queue.Entries);
        Assert.Equal(1, queue.Summary.Dismissed);
    }

    /// <summary>
    /// ADR 0023's rule about who wins. prdb learning the answer later is a good
    /// day; it is not a reason to undo what somebody decided.
    /// </summary>
    [Fact]
    public async Task A_persons_answer_survives_prdb_changing_its_mind()
    {
        var chosen = lookup.Knows("What A Person Said It Is");

        await ArrivedAsync("mystery.mkv");
        await IdentifiedAsync(file => Answer(file.Ref, MatchConfidence.None, null));

        var fileId = (await ReadAsync()).Entries[0].FileId;
        await AssignAsync(fileId, chosen.VideoId);

        // The perceptual hash arrives, so the file is asked about again — and
        // this time prdb names something else.
        await HashedAsync();
        await IdentifiedAsync(file => Answer(
            file.Ref,
            MatchConfidence.Probable,
            MatchRung.PerceptualHash,
            videoId: Guid.NewGuid()));

        var assigned = await ReadAsync(ReviewFilter.Assigned);
        var entry = Assert.Single(assigned.Entries);

        Assert.Equal(chosen.VideoId, entry.Decision?.VideoId);
        Assert.Equal("What A Person Said It Is", entry.Decision?.Title);
    }

    /// <summary>
    /// The way back from a wrong button. It is also why a dismissal never needed
    /// to delete anything.
    /// </summary>
    [Fact]
    public async Task A_decision_can_be_undone()
    {
        await ArrivedAsync("sample.mkv");
        await IdentifiedAsync(file => Answer(file.Ref, MatchConfidence.None, null));

        var fileId = (await ReadAsync()).Entries[0].FileId;
        await DismissAsync(fileId);

        await using (var scope = services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ReviewQueueService>().ForgetAsync(fileId);
        }

        var queue = await ReadAsync();

        Assert.Equal("sample.mkv", Assert.Single(queue.Entries).Name);
        Assert.Equal(0, queue.Summary.Dismissed);
    }

    /// <summary>
    /// The first day: thousands of files at once, worked through by the page and
    /// dismissed by the handful. A queue that can only be worked one row at a
    /// time is a queue nobody empties.
    /// </summary>
    [Fact]
    public async Task A_queue_of_thousands_is_paged_and_can_be_dismissed_in_bulk()
    {
        const int count = (ReviewQueue.PageSize * 3) + 7;

        for (var index = 0; index < count; index++)
        {
            await ArrivedAsync($"video-{index:0000}.mkv");
        }

        await IdentifiedAsync(file => Answer(file.Ref, MatchConfidence.None, null));

        var first = await ReadAsync();

        Assert.Equal(count, first.Total);
        Assert.Equal(ReviewQueue.PageSize, first.Entries.Count);
        Assert.Equal(4, first.Pages);

        var last = await ReadAsync(page: 4);

        Assert.Equal(7, last.Entries.Count);
        Assert.Equal(4, last.Page);

        // Past the end is the last page there is, not an empty screen with a
        // "next" button on it.
        Assert.Equal(4, (await ReadAsync(page: 99)).Page);

        await using (var scope = services.CreateAsyncScope())
        {
            var decision = await scope.ServiceProvider
                .GetRequiredService<ReviewQueueService>()
                .DismissAsync([.. first.Entries.Select(entry => entry.FileId)]);

            Assert.True(decision.Made);
            Assert.Null(decision.Problem);
        }

        var after = await ReadAsync();

        Assert.Equal(count - ReviewQueue.PageSize, after.Total);
        Assert.Equal(ReviewQueue.PageSize, after.Summary.Dismissed);
    }

    /// <summary>
    /// One site's downloads are one naming convention and one evening. Working
    /// through them together is what makes the site rung worth having.
    /// </summary>
    [Fact]
    public async Task The_queue_can_be_narrowed_to_one_site()
    {
        var site = Guid.NewGuid();

        await ArrivedAsync("theirs-one.mkv");
        await ArrivedAsync("theirs-two.mkv");
        await ArrivedAsync("nobodys.mkv");

        await IdentifiedAsync(file => file.FileName.StartsWith("theirs", StringComparison.Ordinal)
            ? Answer(file.Ref, MatchConfidence.Partial, MatchRung.Site, siteId: site, site: "Their Site")
            : Answer(file.Ref, MatchConfidence.None, null));

        var queue = await ReadAsync();

        Assert.Equal(
            [new ReviewSite(site, "Their Site", 2), new ReviewSite(null, null, 1)],
            queue.Sites);

        Assert.Equal(2, (await ReadAsync(site: site)).Total);

        var none = await ReadAsync(noSite: true);

        Assert.Equal("nobodys.mkv", Assert.Single(none.Entries).Name);
    }

    /// <summary>
    /// Searching is the other half of settling something by hand, and it is a
    /// request somebody deliberately spent rather than one the tool made.
    /// </summary>
    [Fact]
    public async Task Searching_asks_prdb_and_answers_with_what_it_found()
    {
        lookup.Knows("A Scene Worth Finding");
        lookup.Knows("Something Else Entirely");

        await using var scope = services.CreateAsyncScope();

        var answer = await scope.ServiceProvider
            .GetRequiredService<ReviewQueueService>()
            .SearchAsync("worth finding");

        Assert.True(answer.Answered);
        Assert.Equal("A Scene Worth Finding", Assert.Single(answer.Videos).Title);
        Assert.Equal("not-a-real-key", Assert.Single(lookup.ApiKeys));
    }

    /// <summary>
    /// The claim every milestone before this one made, and this one too: a queue
    /// that settles four thousand files has still not touched one.
    /// </summary>
    [Fact]
    public async Task Settling_files_leaves_the_download_directory_exactly_as_it_was()
    {
        var video = lookup.Knows("A Scene");

        await ArrivedAsync("assigned.mkv");
        await ArrivedAsync("dismissed.mkv");
        await IdentifiedAsync(file => Answer(file.Ref, MatchConfidence.None, null));

        var before = Snapshot();
        var entries = (await ReadAsync()).Entries;

        await AssignAsync(entries.Single(entry => entry.Name == "assigned.mkv").FileId, video.VideoId);
        await DismissAsync(entries.Single(entry => entry.Name == "dismissed.mkv").FileId);

        Assert.Equal(before, Snapshot());
    }

    private List<string> Snapshot() =>
    [
        .. new DirectoryInfo(downloads)
            .EnumerateFileSystemInfos("*", SearchOption.AllDirectories)
            .Select(entry => entry is FileInfo file
                ? $"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc:O}"
                : entry.FullName)
            .Order(StringComparer.Ordinal),
    ];

    private static RecognisedFile Answer(
        string reference,
        MatchConfidence confidence,
        MatchRung? matchedBy,
        Guid? videoId = null,
        Guid? siteId = null,
        string? site = null,
        IReadOnlyList<Guid>? candidates = null) =>
        new(
            reference,
            confidence,
            matchedBy,
            videoId,
            videoId is null ? null : "What prdb called it",
            null,
            site is null ? null : siteId ?? Guid.NewGuid(),
            site,
            candidates ?? []);

    private async Task ArrivedAsync(string relative)
    {
        var path = Path.Combine(downloads, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var content = new byte[256 * 1024];
        Random.Shared.NextBytes(content);

        await File.WriteAllBytesAsync(path, content);
    }

    /// <summary>Two scans a quiet period apart, and then prdb's answer to all of it.</summary>
    private async Task IdentifiedAsync(Func<FileToIdentify, RecognisedFile> answer)
    {
        prdb.Answers(files => IdentificationAnswer.From([.. files.Select(answer)]));

        await ScanAsync();
        time.Advance(Settling.QuietPeriod);
        await ScanAsync();
        await IdentifyAsync();
    }

    /// <summary>
    /// A perceptual hash arriving, which is the one thing that makes the tool ask
    /// prdb about a settled file a second time.
    /// </summary>
    private async Task HashedAsync()
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdenoDbContext>();

        foreach (var file in await context.DiscoveredFiles.ToListAsync())
        {
            file.PerceptualHash = "0123456789abcdef";
        }

        await context.SaveChangesAsync();
    }

    private async Task ScanAsync()
    {
        await using var scope = services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<ScanService>().ScanAsync();
    }

    private async Task IdentifyAsync()
    {
        await using var scope = services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<IdentificationService>().IdentifyAsync();
    }

    private async Task<ReviewQueue> ReadAsync(
        ReviewFilter filter = ReviewFilter.Waiting,
        Guid? site = null,
        bool noSite = false,
        int page = 1)
    {
        await using var scope = services.CreateAsyncScope();

        return await scope.ServiceProvider
            .GetRequiredService<ReviewQueueService>()
            .ReadAsync(filter, site, noSite, page);
    }

    private async Task<ReviewDecision> AssignAsync(int fileId, Guid videoId)
    {
        await using var scope = services.CreateAsyncScope();

        return await scope.ServiceProvider
            .GetRequiredService<ReviewQueueService>()
            .AssignAsync(fileId, videoId);
    }

    private async Task<ReviewDecision> DismissAsync(int fileId)
    {
        await using var scope = services.CreateAsyncScope();

        return await scope.ServiceProvider
            .GetRequiredService<ReviewQueueService>()
            .DismissAsync(fileId);
    }

    /// <summary>A finished onboarding, written straight to the database.</summary>
    private async Task ConfigureAsync()
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdenoDbContext>();

        var configuration = await context.Configuration.SingleAsync();
        configuration.PrdbApiKey = "not-a-real-key";
        configuration.TargetDirectory = Directory.CreateDirectory(directory.Combine("library")).FullName;
        configuration.Layout = LibraryLayouts.NameOf(LibraryLayout.Jellyfin);
        configuration.OnboardingCompletedAt = time.GetUtcNow();

        context.SourceDirectories.Add(new SourceDirectory { Path = downloads, AddedAt = time.GetUtcNow() });

        await context.SaveChangesAsync();
    }
}

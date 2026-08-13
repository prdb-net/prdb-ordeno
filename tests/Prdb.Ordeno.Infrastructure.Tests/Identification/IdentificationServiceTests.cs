using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Prdb.Ordeno.Core.Configuration;
using Prdb.Ordeno.Core.Identification;
using Prdb.Ordeno.Core.Scanning;
using Prdb.Ordeno.Infrastructure.Identification;
using Prdb.Ordeno.Infrastructure.Persistence;
using Prdb.Ordeno.Infrastructure.Scanning;
using Prdb.Ordeno.Infrastructure.Tests.Scanning;

using Xunit;

namespace Prdb.Ordeno.Infrastructure.Tests.Identification;

/// <summary>
/// Issue #15 against a real filesystem and a real SQLite file: what is asked,
/// what is asked twice, and what happens when prdb says no.
/// </summary>
/// <remarks>
/// Nothing here is allowed to move, rename or delete a file either. This
/// milestone ends with a tool that knows what its files are and has still not
/// written one.
/// </remarks>
public sealed class IdentificationServiceTests : IAsyncLifetime
{
    /// <summary>Large enough to have an <c>osHash</c>; see <see cref="OsHashesTests"/>.</summary>
    private const int Hashable = 256 * 1024;

    private readonly TempDirectory directory = new();
    private readonly TestTime time = new();
    private readonly FakePrdbIdentification prdb = new();
    private readonly TestFileHashes hashes = new();

    private ServiceProvider services = null!;
    private string downloads = null!;

    public async Task InitializeAsync()
    {
        downloads = Directory.CreateDirectory(directory.Combine("downloads")).FullName;

        var collection = new ServiceCollection();
        collection.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        collection.AddSingleton<TimeProvider>(time);

        // Registered before the slice, whose registrations are TryAdd: the
        // endpoint and the moment the filesystem says no are the two things a
        // test has to be able to decide. Everything between them is the real
        // thing.
        collection.AddSingleton<IVideoIdentification>(prdb);
        collection.AddSingleton<IFileHashes>(hashes);

        collection.AddOrdenoPersistence(directory.Combine("data"));
        collection.AddOrdenoScanning();
        collection.AddOrdenoIdentification();

        services = collection.BuildServiceProvider();

        await services.PrepareOrdenoDatabaseAsync();
        await ConfigureAsync();
    }

    public async Task DisposeAsync()
    {
        await services.DisposeAsync();
        directory.Dispose();
    }

    [Fact]
    public async Task A_file_that_has_finished_downloading_is_asked_about_with_its_hash()
    {
        var videoId = Guid.NewGuid();
        prdb.Recognises(videoId, "A Scene", "A Site");

        await ArrivedAsync("release/A.Site.24.05.01.A.Scene.mkv", Hashable);
        await SettledAsync();

        var outcome = await IdentifyAsync();

        Assert.Equal(1, outcome.Asked);
        Assert.Null(outcome.Problem);

        var asked = Assert.Single(prdb.Asked);

        // The name, not the path: prdb reads a release name out of it, and the
        // directories on somebody's NAS are not part of the question.
        Assert.Equal("A.Site.24.05.01.A.Scene.mkv", asked.FileName);
        Assert.Equal(Hashable, asked.SizeBytes);
        Assert.NotNull(asked.OsHash);
        Assert.Null(asked.PerceptualHash);
        Assert.Equal("not-a-real-key", Assert.Single(prdb.ApiKeys));

        var recognition = await RecognitionOfAsync("A.Site.24.05.01.A.Scene.mkv");

        Assert.Equal(RecognitionState.Recognised, recognition.State);
        Assert.Equal(videoId, recognition.VideoId);
        Assert.Equal(MatchRung.OsHash, recognition.MatchedBy);
        Assert.Equal("A Scene", recognition.Title);
    }

    /// <summary>
    /// The tool waits for a file to stop changing before it counts on it, and
    /// that includes counting on its hash.
    /// </summary>
    [Fact]
    public async Task A_file_that_is_still_arriving_is_not_asked_about()
    {
        await ArrivedAsync("arriving.mkv", Hashable);
        await ScanAsync();

        Assert.Equal(0, (await IdentifyAsync()).Asked);
        Assert.Empty(prdb.Asked);
    }

    /// <summary>
    /// The rule that keeps a library-sized first run affordable. Four thousand
    /// files nobody could identify must not be four thousand questions every
    /// five minutes.
    /// </summary>
    [Fact]
    public async Task A_file_is_asked_about_once_and_not_again()
    {
        await ArrivedAsync("video.mkv", Hashable);
        await SettledAsync();

        Assert.Equal(1, (await IdentifyAsync()).Asked);
        Assert.Equal(0, (await IdentifyAsync()).Asked);
        Assert.Single(prdb.Batches);
    }

    /// <summary>
    /// Different bytes, different file. What prdb said about the old ones is not
    /// an answer about these.
    /// </summary>
    [Fact]
    public async Task A_file_whose_bytes_changed_is_asked_about_again()
    {
        prdb.Recognises(Guid.NewGuid(), "A Scene", "A Site");

        await ArrivedAsync("video.mkv", Hashable);
        await SettledAsync();
        await IdentifyAsync();

        Assert.NotNull(await RecognitionOfAsync("video.mkv"));

        // The download turned out not to have finished after all.
        await ArrivedAsync("video.mkv", Hashable * 2);
        time.Advance(Settling.QuietPeriod);
        await ScanAsync();

        Assert.Null(await RecognitionOrNullAsync("video.mkv"));
        Assert.Null(await HashOfAsync("video.mkv"));

        time.Advance(Settling.QuietPeriod);
        await ScanAsync();

        Assert.Equal(1, (await IdentifyAsync()).Asked);
        Assert.Equal(2, prdb.Batches.Count);
    }

    /// <summary>
    /// ADR 0001: several videos fitting equally well is an answer, and it is
    /// stored as one. Nothing here narrows it to a single video.
    /// </summary>
    [Fact]
    public async Task An_ambiguous_answer_is_stored_as_ambiguous()
    {
        var candidates = new[] { Guid.NewGuid(), Guid.NewGuid() };

        prdb.Answers(files => IdentificationAnswer.From(
        [
            .. files.Select(file => new RecognisedFile(
                file.Ref,
                MatchConfidence.Ambiguous,
                MatchRung.ReleaseName,
                VideoId: null,
                Title: null,
                ReleaseDate: null,
                SiteId: null,
                SiteTitle: null,
                candidates)),
        ]));

        await ArrivedAsync("video.mkv", Hashable);
        await SettledAsync();
        await IdentifyAsync();

        var recognition = await RecognitionOfAsync("video.mkv");

        Assert.Equal(RecognitionState.Ambiguous, recognition.State);
        Assert.Null(recognition.VideoId);
        Assert.Equal(2, recognition.Candidates);

        await using var scope = services.CreateAsyncScope();
        var stored = await scope.ServiceProvider
            .GetRequiredService<OrdenoDbContext>()
            .FileIdentifications
            .Include(identification => identification.Candidates)
            .SingleAsync();

        Assert.Equal(candidates, stored.Candidates.OrderBy(row => row.Position).Select(row => row.VideoId));
    }

    /// <summary>
    /// The site rung. It is a result, and the tool has to be able to say so
    /// rather than filing it with the files it knows nothing about.
    /// </summary>
    [Fact]
    public async Task A_file_whose_site_is_all_prdb_could_read_says_exactly_that()
    {
        prdb.Answers(files => IdentificationAnswer.From(
        [
            .. files.Select(file => new RecognisedFile(
                file.Ref,
                MatchConfidence.Partial,
                MatchRung.Site,
                VideoId: null,
                Title: null,
                ReleaseDate: null,
                Guid.NewGuid(),
                "A Site",
                [])),
        ]));

        await ArrivedAsync("video.mkv", Hashable);
        await SettledAsync();
        await IdentifyAsync();

        var summary = (await ReadAsync()).Recognition;

        Assert.Equal(1, summary.SiteOnly);
        Assert.Equal(0, summary.Unrecognised);
        Assert.Equal(0, summary.Waiting);
    }

    /// <summary>
    /// AGENTS.md's one hard rule, at the only place this milestone can break it:
    /// an hour of prdb being down costs an hour, not a wrong answer.
    /// </summary>
    [Fact]
    public async Task prdb_being_down_leaves_every_file_exactly_as_it_was()
    {
        prdb.Answers(_ => IdentificationAnswer.Stopped(
            IdentificationStatus.Unreachable,
            "prdb could not be reached."));

        await ArrivedAsync("video.mkv", Hashable);
        await SettledAsync();

        var outcome = await IdentifyAsync();

        Assert.Equal(0, outcome.Asked);
        Assert.Equal("prdb could not be reached.", outcome.Problem);
        Assert.Null(await RecognitionOrNullAsync("video.mkv"));

        // And the next run asks again, because nothing was recorded.
        prdb.Recognises(Guid.NewGuid(), "A Scene", "A Site");

        Assert.Equal(1, (await IdentifyAsync()).Asked);
        Assert.NotNull(await RecognitionOrNullAsync("video.mkv"));
    }

    /// <summary>
    /// A refusal that carries a wait is honoured. Spending a request every five
    /// minutes to be refused again is how a rate limit becomes permanent.
    /// </summary>
    [Fact]
    public async Task A_refusal_that_asks_for_a_wait_gets_one()
    {
        prdb.Answers(_ => IdentificationAnswer.Stopped(
            IdentificationStatus.RateLimited,
            "The quota is spent.",
            TimeSpan.FromMinutes(20)));

        await ArrivedAsync("video.mkv", Hashable);
        await SettledAsync();

        var outcome = await IdentifyAsync();

        Assert.Equal(time.GetUtcNow() + TimeSpan.FromMinutes(20), outcome.NotBefore);
    }

    /// <summary>
    /// A file locked at the wrong moment must not be asked about without its
    /// hash: it would get an answer off a lower rung, that answer would be
    /// stored, and it would never be asked about again.
    /// </summary>
    [Fact]
    public async Task A_file_that_could_not_be_read_is_left_for_the_next_run()
    {
        hashes.Unreadable.Add("busy.mkv");

        await ArrivedAsync("busy.mkv", Hashable);
        await SettledAsync();

        Assert.Equal(0, (await IdentifyAsync()).Asked);
        Assert.Empty(prdb.Asked);

        hashes.Unreadable.Clear();

        Assert.Equal(1, (await IdentifyAsync()).Asked);
        Assert.NotNull(Assert.Single(prdb.Asked).OsHash);
    }

    /// <summary>
    /// Under 128 KiB there is no hash to be had, and that is a state rather than
    /// a failure — the file is asked about by name.
    /// </summary>
    [Fact]
    public async Task A_file_too_small_to_hash_is_still_asked_about()
    {
        await ArrivedAsync("tiny.mkv", 1024);
        await SettledAsync();

        Assert.Equal(1, (await IdentifyAsync()).Asked);

        var asked = Assert.Single(prdb.Asked);
        Assert.Null(asked.OsHash);
        Assert.Equal("tiny.mkv", asked.FileName);
    }

    /// <summary>
    /// The perceptual rung arrives late by design, and a file that was asked
    /// about without one is worth asking about again once it has one.
    /// </summary>
    [Fact]
    public async Task A_perceptual_hash_arriving_is_worth_another_question()
    {
        await ArrivedAsync("video.mkv", Hashable);
        await SettledAsync();
        await IdentifyAsync();

        Assert.Equal(0, (await IdentifyAsync()).Asked);

        await using (var scope = services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<OrdenoDbContext>();
            var file = await context.DiscoveredFiles.SingleAsync();
            file.PerceptualHash = "0123456789abcdef";

            await context.SaveChangesAsync();
        }

        Assert.Equal(1, (await IdentifyAsync()).Asked);
        Assert.Equal("0123456789abcdef", prdb.Batches[^1].Single().PerceptualHash);

        // And now it has been asked with one, so it is finished with.
        Assert.Equal(0, (await IdentifyAsync()).Asked);
    }

    /// <summary>
    /// ADR 0001's whole point: a library is a handful of requests rather than one
    /// per file.
    /// </summary>
    [Fact]
    public async Task A_library_sized_first_run_is_a_handful_of_requests()
    {
        var count = (IdentificationSchedule.MaxBatch * 2) + 1;

        for (var index = 0; index < count; index++)
        {
            await ArrivedAsync($"video-{index:0000}.mkv", 64);
        }

        await SettledAsync();

        Assert.Equal(count, (await IdentifyAsync()).Asked);
        Assert.Equal(3, prdb.Batches.Count);
        Assert.All(prdb.Batches, batch =>
            Assert.True(batch.Count <= IdentificationSchedule.MaxBatch));

        // Every file, and every one of them once.
        Assert.Equal(count, prdb.Asked.Select(file => file.Ref).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// prdb reports what is left of the quota on every answer. A run that would
    /// spend the last of it stops instead, and says so without calling it an
    /// error.
    /// </summary>
    [Fact]
    public async Task A_run_stops_short_of_the_last_of_the_quota()
    {
        for (var index = 0; index <= IdentificationSchedule.MaxBatch; index++)
        {
            await ArrivedAsync($"video-{index:0000}.mkv", 64);
        }

        await SettledAsync();

        prdb.Answers(files => IdentificationAnswer.From(
            [.. files.Select(file => new RecognisedFile(
                file.Ref, MatchConfidence.None, null, null, null, null, null, null, []))],
            new RateLimitReading(Remaining: 1, ResetIn: TimeSpan.FromMinutes(30))));

        var outcome = await IdentifyAsync();

        Assert.Equal(IdentificationSchedule.MaxBatch, outcome.Asked);
        Assert.Single(prdb.Batches);
        Assert.Contains("quota", outcome.Problem!, StringComparison.Ordinal);
        Assert.Equal(time.GetUtcNow() + TimeSpan.FromMinutes(30), outcome.NotBefore);
    }

    /// <summary>
    /// The same reading, when there was nothing left to ask about anyway. A run
    /// that finished everything has not stopped short of anything, and telling
    /// the user it did would send them looking for files that are not waiting.
    /// </summary>
    [Fact]
    public async Task A_run_that_got_through_everything_reports_no_problem()
    {
        await ArrivedAsync("video.mkv", 64);
        await SettledAsync();

        prdb.Answers(files => IdentificationAnswer.From(
            [.. files.Select(file => new RecognisedFile(
                file.Ref, MatchConfidence.None, null, null, null, null, null, null, []))],
            new RateLimitReading(Remaining: 1, ResetIn: TimeSpan.FromMinutes(30))));

        var outcome = await IdentifyAsync();

        Assert.Equal(1, outcome.Asked);
        Assert.Null(outcome.Problem);
        Assert.Null(outcome.NotBefore);
    }

    /// <summary>
    /// The claim this milestone rests on, the same one the scan makes: the
    /// download directory is read and nothing else.
    /// </summary>
    [Fact]
    public async Task Identifying_leaves_the_download_directory_exactly_as_it_was()
    {
        prdb.Recognises(Guid.NewGuid(), "A Scene", "A Site");

        await ArrivedAsync("video.mkv", Hashable);
        await ArrivedAsync("release/second.mp4", Hashable);
        await SettledAsync();

        var before = Snapshot();

        await IdentifyAsync();

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

    private async Task ArrivedAsync(string relative, int bytes)
    {
        var path = Path.Combine(downloads, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Random rather than zeroes: two files of the same length full of the
        // same byte have the same hash, and a test that cannot tell two files
        // apart proves less than it looks.
        var content = new byte[bytes];
        Random.Shared.NextBytes(content);

        await File.WriteAllBytesAsync(path, content);
    }

    /// <summary>Two scans a quiet period apart, which is what makes a file ready.</summary>
    private async Task SettledAsync()
    {
        await ScanAsync();
        time.Advance(Settling.QuietPeriod);
        await ScanAsync();
    }

    private async Task ScanAsync()
    {
        await using var scope = services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<ScanService>().ScanAsync();
    }

    private async Task<IdentificationOutcome> IdentifyAsync()
    {
        await using var scope = services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<IdentificationService>().IdentifyAsync();
    }

    private async Task<Inventory> ReadAsync()
    {
        await using var scope = services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<ScanService>().ReadAsync();
    }

    private async Task<Recognition> RecognitionOfAsync(string name) =>
        await RecognitionOrNullAsync(name)
        ?? throw new InvalidOperationException($"{name} has not been identified.");

    private async Task<Recognition?> RecognitionOrNullAsync(string name)
    {
        var inventory = await ReadAsync();

        return inventory.Files.Single(file => Path.GetFileName(file.Path) == name).Recognised;
    }

    private async Task<string?> HashOfAsync(string name)
    {
        await using var scope = services.CreateAsyncScope();

        var files = await scope.ServiceProvider
            .GetRequiredService<OrdenoDbContext>()
            .DiscoveredFiles
            .AsNoTracking()
            .ToListAsync();

        return files.Single(file => Path.GetFileName(file.Path) == name).OsHash;
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

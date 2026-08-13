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
/// The perceptual hash backlog: which files it takes, what it records, and what
/// it refuses to keep trying.
/// </summary>
/// <remarks>
/// Hashing is the slowest thing this tool does and the only part of it somebody
/// would notice on a shared machine, so what it does <em>not</em> do is as much
/// of the subject here as what it does.
/// </remarks>
public sealed class PerceptualHashServiceTests : IAsyncLifetime
{
    private readonly TempDirectory directory = new();
    private readonly TestTime time = new();
    private readonly FakePrdbIdentification prdb = new();
    private readonly FakePerceptualHashes ffmpeg = new();

    private ServiceProvider services = null!;
    private string downloads = null!;

    public async Task InitializeAsync()
    {
        downloads = Directory.CreateDirectory(directory.Combine("downloads")).FullName;

        var collection = new ServiceCollection();
        collection.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        collection.AddSingleton<TimeProvider>(time);
        collection.AddSingleton<IVideoIdentification>(prdb);
        collection.AddSingleton<IPerceptualHashes>(ffmpeg);
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

    /// <summary>
    /// prdb compares perceptual hashes for equality, so hashing a file it has
    /// already recognised by its exact hash spends minutes of somebody's evening
    /// to learn what is already known.
    /// </summary>
    [Fact]
    public async Task A_file_prdb_recognised_by_its_exact_hash_is_not_hashed()
    {
        prdb.Recognises(Guid.NewGuid(), "A Scene", "A Site");

        await AskedAboutAsync("video.mkv");

        Assert.False(await HashNextAsync());
        Assert.Empty(ffmpeg.Hashed);
        Assert.Equal(0, await BacklogAsync());
    }

    [Fact]
    public async Task A_file_nothing_matched_is_hashed_and_asked_about_again()
    {
        await AskedAboutAsync("video.mkv");

        Assert.Equal(1, await BacklogAsync());
        Assert.True(await HashNextAsync());
        Assert.Equal("video.mkv", Assert.Single(ffmpeg.Hashed));
        Assert.Equal("0123456789abcdef", await StoredHashAsync());

        // Nothing else has to happen for the file to be worth another question:
        // the last one was asked without a perceptual hash.
        Assert.Equal(1, (await IdentifyAsync()).Asked);
        Assert.Equal("0123456789abcdef", prdb.Batches[^1].Single().PerceptualHash);

        // And now the backlog is empty and stays empty.
        Assert.Equal(0, await BacklogAsync());
        Assert.False(await HashNextAsync());
    }

    /// <summary>
    /// A file nobody has asked prdb about yet may be one that needs no hashing
    /// at all. Hashing it first would be paying for the expensive rung before
    /// finding out whether the cheap one answers.
    /// </summary>
    [Fact]
    public async Task A_file_prdb_has_not_been_asked_about_is_left_alone()
    {
        await ArrivedAsync("video.mkv");
        await SettledAsync();

        Assert.Equal(0, await BacklogAsync());
        Assert.False(await HashNextAsync());
    }

    [Fact]
    public async Task A_file_that_has_not_settled_is_not_hashed()
    {
        await AskedAboutAsync("video.mkv");

        // It started growing again after it was asked about.
        await File.WriteAllBytesAsync(Path.Combine(downloads, "video.mkv"), new byte[8192]);
        time.Advance(Settling.QuietPeriod);
        await ScanAsync();

        Assert.Equal(0, await BacklogAsync());
    }

    /// <summary>
    /// A truncated download or a container ffmpeg cannot seek is a property of
    /// the file. Trying it again every few minutes costs twenty-five frame
    /// decodes to learn the same thing.
    /// </summary>
    [Fact]
    public async Task A_file_ffmpeg_cannot_read_is_not_tried_again()
    {
        ffmpeg.Fails(PerceptualHashState.ProbeFailed);

        await AskedAboutAsync("broken.mkv");

        Assert.True(await HashNextAsync());
        Assert.False(await HashNextAsync());
        Assert.Single(ffmpeg.Hashed);
        Assert.Null(await StoredHashAsync());
    }

    /// <summary>
    /// A timeout is the one failure that says as much about how busy the disk
    /// was as about the file — and it still does not go on forever.
    /// </summary>
    [Fact]
    public async Task A_timeout_is_worth_another_go_but_not_an_endless_one()
    {
        ffmpeg.Fails(PerceptualHashState.TimedOut);

        await AskedAboutAsync("slow.mkv");

        for (var attempt = 0; attempt < PerceptualHashBacklog.MaxAttempts; attempt++)
        {
            Assert.True(await HashNextAsync());
        }

        Assert.False(await HashNextAsync());
        Assert.Equal(PerceptualHashBacklog.MaxAttempts, ffmpeg.Hashed.Count);
    }

    /// <summary>
    /// One file per turn, each of them once. The order is the order they were
    /// recorded in, which is not the order a directory listing comes back in —
    /// what matters is that the backlog empties and nothing is hashed twice.
    /// </summary>
    [Fact]
    public async Task The_backlog_is_worked_through_one_file_at_a_time()
    {
        await AskedAboutAsync("first.mkv", "second.mkv", "third.mkv");

        Assert.Equal(3, await BacklogAsync());

        Assert.True(await HashNextAsync());
        Assert.Equal(2, await BacklogAsync());

        Assert.True(await HashNextAsync());
        Assert.True(await HashNextAsync());
        Assert.False(await HashNextAsync());

        Assert.Equal(["first.mkv", "second.mkv", "third.mkv"], ffmpeg.Hashed.Order(StringComparer.Ordinal));
    }

    private async Task<bool> HashNextAsync()
    {
        await using var scope = services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<PerceptualHashService>().HashNextAsync();
    }

    private async Task<int> BacklogAsync()
    {
        await using var scope = services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<PerceptualHashService>().BacklogAsync();
    }

    private async Task<string?> StoredHashAsync()
    {
        await using var scope = services.CreateAsyncScope();

        return await scope.ServiceProvider
            .GetRequiredService<OrdenoDbContext>()
            .DiscoveredFiles
            .AsNoTracking()
            .Select(file => file.PerceptualHash)
            .FirstAsync();
    }

    /// <summary>Files that have arrived, settled, and been asked about once.</summary>
    private async Task AskedAboutAsync(params string[] names)
    {
        foreach (var name in names)
        {
            await ArrivedAsync(name);
        }

        await SettledAsync();
        await IdentifyAsync();
    }

    private async Task ArrivedAsync(string name)
    {
        var content = new byte[4096];
        Random.Shared.NextBytes(content);

        await File.WriteAllBytesAsync(Path.Combine(downloads, name), content);
    }

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

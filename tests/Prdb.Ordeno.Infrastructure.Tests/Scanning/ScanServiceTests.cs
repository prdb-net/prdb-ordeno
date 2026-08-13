using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Prdb.Ordeno.Core.Configuration;
using Prdb.Ordeno.Core.Scanning;
using Prdb.Ordeno.Infrastructure.Configuration;
using Prdb.Ordeno.Infrastructure.Persistence;
using Prdb.Ordeno.Infrastructure.Scanning;

using Xunit;

namespace Prdb.Ordeno.Infrastructure.Tests.Scanning;

/// <summary>
/// The scan, against a real filesystem and a real SQLite file. Nothing in here
/// is allowed to write, rename or delete a file in the directories it walks —
/// that is the whole reason this milestone comes before the one that files
/// anything, and the last test says so out loud.
/// </summary>
public sealed class ScanServiceTests : IAsyncLifetime
{
    private readonly TempDirectory directory = new();
    private readonly TestTime time = new();

    private ServiceProvider services = null!;
    private string downloads = null!;
    private string library = null!;

    public async Task InitializeAsync()
    {
        downloads = Directory.CreateDirectory(directory.Combine("downloads")).FullName;
        library = Directory.CreateDirectory(directory.Combine("library")).FullName;

        var collection = new ServiceCollection();
        collection.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        collection.AddSingleton<TimeProvider>(time);
        collection.AddOrdenoPersistence(directory.Combine("data"));
        collection.AddOrdenoScanning();

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
    /// A file that is still growing is not a candidate, however long it has been
    /// there. VISION.md would rather wait another cycle than act on one.
    /// </summary>
    [Fact]
    public async Task A_file_that_is_still_being_written_is_not_ready()
    {
        var path = Path.Combine(downloads, "arriving.mkv");
        await File.WriteAllBytesAsync(path, new byte[1024]);

        await ScanAsync();
        Assert.Equal((0, 1), await CountsAsync());

        // Another scan later, but the file grew in between: back to the start.
        time.Advance(Settling.QuietPeriod * 2);
        await File.WriteAllBytesAsync(path, new byte[4096]);

        await ScanAsync();
        Assert.Equal((0, 1), await CountsAsync());
    }

    [Fact]
    public async Task A_file_that_has_stopped_changing_becomes_ready_without_being_asked_again()
    {
        await File.WriteAllBytesAsync(Path.Combine(downloads, "finished.mkv"), new byte[1024]);

        await ScanAsync();
        Assert.Equal((0, 1), await CountsAsync());

        time.Advance(Settling.QuietPeriod);
        await ScanAsync();

        Assert.Equal((1, 0), await CountsAsync());
    }

    /// <summary>
    /// The observation is what survives a restart, not just the file. Everything
    /// readiness is decided from is in the row, so a container that comes back up
    /// does not put a settled library through its quiet period again.
    /// </summary>
    [Fact]
    public async Task A_file_that_was_ready_before_a_restart_is_still_ready_after_one()
    {
        await File.WriteAllBytesAsync(Path.Combine(downloads, "finished.mkv"), new byte[1024]);

        await ScanAsync();
        time.Advance(Settling.QuietPeriod);
        await ScanAsync();

        Assert.Equal((1, 0), await CountsAsync());

        // A restart is a new set of services over the same database file, and a
        // clock that has moved on while the container was down.
        await services.DisposeAsync();
        time.Advance(TimeSpan.FromHours(6));

        var collection = new ServiceCollection();
        collection.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        collection.AddSingleton<TimeProvider>(time);
        collection.AddOrdenoPersistence(directory.Combine("data"));
        collection.AddOrdenoScanning();
        services = collection.BuildServiceProvider();

        Assert.Equal((1, 0), await CountsAsync());
    }

    [Fact]
    public async Task A_file_that_is_gone_leaves_the_inventory()
    {
        var path = Path.Combine(downloads, "here-for-now.mkv");
        await File.WriteAllBytesAsync(path, new byte[1024]);

        await ScanAsync();
        Assert.Equal(1, await RowsAsync());

        File.Delete(path);
        time.Advance(Settling.QuietPeriod);
        await ScanAsync();

        Assert.Equal(0, await RowsAsync());
    }

    /// <summary>
    /// The Monday-morning case: the share did not come back over the weekend.
    /// Reporting it as an empty directory would throw away everything the tool
    /// knew, and the scan after the share returns would then treat a settled
    /// library as thousands of brand new arrivals.
    /// </summary>
    [Fact]
    public async Task A_directory_that_cannot_be_read_keeps_what_the_tool_knew_about_it()
    {
        await File.WriteAllBytesAsync(Path.Combine(downloads, "finished.mkv"), new byte[1024]);

        await ScanAsync();
        time.Advance(Settling.QuietPeriod);
        await ScanAsync();

        Assert.Equal((1, 0), await CountsAsync());

        Directory.Move(downloads, directory.Combine("unmounted"));
        try
        {
            await ScanAsync();

            Assert.Equal(1, await RowsAsync());

            var inventory = await ReadAsync();
            var source = Assert.Single(inventory.Sources);
            Assert.False(source.Reachable);
            Assert.Contains("cannot be read", inventory.WhatItFound, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Move(directory.Combine("unmounted"), downloads);
        }
    }

    /// <summary>
    /// ADR 0009: a fresh installation scans nothing until the guided path has
    /// been walked to its end.
    /// </summary>
    [Fact]
    public async Task Nothing_is_scanned_before_onboarding_has_been_finished()
    {
        await File.WriteAllBytesAsync(Path.Combine(downloads, "video.mkv"), new byte[1024]);

        await using (var scope = services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<OrdenoDbContext>();
            var configuration = await context.Configuration.SingleAsync();
            configuration.OnboardingCompletedAt = null;

            await context.SaveChangesAsync();
        }

        await ScanAsync();

        Assert.Equal(0, await RowsAsync());
        Assert.Contains("Finish the setup", (await ReadAsync()).WhatItFound, StringComparison.Ordinal);
    }

    /// <summary>
    /// A file dragged from one watched directory into another arrives at a new
    /// path, so the tool treats it as new — which is right, because a file that
    /// has just appeared somewhere may still be arriving there. What must not
    /// happen is the file being counted in both places: the directories are
    /// walked first and the sweep for what is gone runs after all of them, so
    /// there is never a moment where it exists twice or not at all.
    /// </summary>
    [Fact]
    public async Task A_file_moved_between_two_watched_directories_is_counted_once()
    {
        var second = Directory.CreateDirectory(directory.Combine("more-downloads")).FullName;
        await AddSourceAsync(second);

        var path = Path.Combine(downloads, "video.mkv");
        await File.WriteAllBytesAsync(path, new byte[1024]);

        await ScanAsync();
        time.Advance(Settling.QuietPeriod);
        await ScanAsync();
        Assert.Equal((1, 0), await CountsAsync());

        File.Move(path, Path.Combine(second, "video.mkv"));
        time.Advance(Settling.QuietPeriod);
        await ScanAsync();

        // One file, not two and not none — and now under the other directory,
        // where it has to settle again before anything would act on it.
        Assert.Equal(1, await RowsAsync());
        Assert.Equal((0, 1), await CountsAsync());

        var inventory = await ReadAsync();
        Assert.Equal(0, inventory.Sources.Single(source => source.Path == downloads).Total);
        Assert.Equal(1, inventory.Sources.Single(source => source.Path == second).Settling);
    }

    [Fact]
    public async Task Unwatching_a_directory_takes_its_files_with_it()
    {
        await File.WriteAllBytesAsync(Path.Combine(downloads, "video.mkv"), new byte[1024]);
        await ScanAsync();
        Assert.Equal(1, await RowsAsync());

        await using (var scope = services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<OrdenoDbContext>();

            // The way the configuration does it: one statement, no rows loaded.
            await context.SourceDirectories.ExecuteDeleteAsync();
        }

        Assert.Equal(0, await RowsAsync());
    }

    /// <summary>
    /// A first pass over an existing library is thousands of files. The screen
    /// gets a capped list and the real number next to it, because a thousand rows
    /// in a browser help nobody.
    /// </summary>
    [Fact]
    public async Task A_large_directory_is_counted_in_full_and_listed_in_part()
    {
        var count = Inventory.Limit + 37;

        for (var index = 0; index < count; index++)
        {
            await File.WriteAllBytesAsync(
                Path.Combine(downloads, $"video-{index:0000}.mkv"),
                new byte[16]);
        }

        await ScanAsync();

        var inventory = await ReadAsync();

        Assert.Equal(count, inventory.Total);
        Assert.Equal(Inventory.Limit, inventory.Files.Count);
    }

    /// <summary>
    /// The claim this whole milestone rests on. Reading a download directory must
    /// leave it exactly as it was found — no probe files, no renames, nothing
    /// deleted — because everything after this is built on top of it.
    /// </summary>
    [Fact]
    public async Task Scanning_leaves_the_download_directory_exactly_as_it_was()
    {
        var paths = new[] { "video.mkv", "release/second.mp4", "release/notes.txt" };

        foreach (var relative in paths)
        {
            var path = Path.Combine(downloads, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, new byte[64]);
        }

        var before = Snapshot(downloads);

        await ScanAsync();
        time.Advance(Settling.QuietPeriod);
        await ScanAsync();

        Assert.Equal(before, Snapshot(downloads));
    }

    /// <summary>Path, size and modification time of everything under a directory.</summary>
    private static List<string> Snapshot(string root) =>
    [
        .. new DirectoryInfo(root)
            .EnumerateFileSystemInfos("*", SearchOption.AllDirectories)
            .Select(entry => entry is FileInfo file
                ? $"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc:O}"
                : entry.FullName)
            .Order(StringComparer.Ordinal),
    ];

    private async Task ScanAsync()
    {
        await using var scope = services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<ScanService>().ScanAsync();
    }

    private async Task<Inventory> ReadAsync()
    {
        await using var scope = services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<ScanService>().ReadAsync();
    }

    private async Task<int> RowsAsync()
    {
        await using var scope = services.CreateAsyncScope();

        return await scope.ServiceProvider
            .GetRequiredService<OrdenoDbContext>()
            .DiscoveredFiles
            .CountAsync();
    }

    private async Task<(int Ready, int Settling)> CountsAsync()
    {
        var inventory = await ReadAsync();

        return (inventory.Ready, inventory.Settling);
    }

    /// <summary>A finished onboarding, written straight to the database.</summary>
    private async Task ConfigureAsync()
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdenoDbContext>();

        var configuration = await context.Configuration.SingleAsync();
        configuration.PrdbApiKey = "not-a-real-key";
        configuration.TargetDirectory = library;
        configuration.Layout = LibraryLayouts.NameOf(LibraryLayout.Jellyfin);
        configuration.OnboardingCompletedAt = time.GetUtcNow();

        context.SourceDirectories.Add(new SourceDirectory { Path = downloads, AddedAt = time.GetUtcNow() });

        await context.SaveChangesAsync();
    }

    private async Task AddSourceAsync(string path)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdenoDbContext>();

        context.SourceDirectories.Add(new SourceDirectory { Path = path, AddedAt = time.GetUtcNow() });

        await context.SaveChangesAsync();
    }
}

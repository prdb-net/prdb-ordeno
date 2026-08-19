using Prdb.Ordeno.Core.Configuration;
using Prdb.Ordeno.Core.History;
using Prdb.Ordeno.Core.Identification;
using Prdb.Ordeno.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Ordeno.Infrastructure.Tests.History;

/// <summary>
/// ADR 0028: what filing writes down as it moves files, and how the log stays
/// small enough to live in the same SQLite file as everything else after three
/// years.
/// </summary>
public sealed class OperationLogTests : IAsyncLifetime
{
    private HistoryWorkspace workspace = null!;

    public async Task InitializeAsync() => workspace = await HistoryWorkspace.StartAsync();

    public async Task DisposeAsync() => await workspace.DisposeAsync();

    /// <summary>
    /// One entry per file moved, with where it came from, where it went, and why
    /// the tool believed that was right. The last part is what turns a bug report
    /// into something answerable, and it has to be written now: the
    /// identification row it comes from is deleted with the file it described.
    /// </summary>
    [Fact]
    public async Task Filing_records_what_it_moved_and_why()
    {
        workspace.Recognised();
        var source = workspace.Arrived("Example.Studio.24.05.01.Scene.Title.1080p.mkv");
        await workspace.ReadyAsync();

        await workspace.FileAsync();

        var run = Assert.Single(await workspace.RunsAsync());
        Assert.Equal(RunKind.Filing, run.Kind);
        Assert.NotNull(run.FinishedAt);
        Assert.Equal("1 video was filed.", run.Account);

        var entry = Assert.Single(await workspace.OperationsAsync());

        Assert.Equal(OperationKind.Filed, entry.Kind);
        Assert.Equal(run.Id, entry.RunId);
        Assert.Equal(source, entry.FromPath);
        Assert.Equal(
            Path.Combine(workspace.Scene, "Example Studio - 2024-05-01 - Scene Title.mkv"),
            entry.ToPath);
        Assert.Equal("1080p", entry.QualityLabel);
        Assert.Equal(FileMovement.Rename, entry.Movement);
        Assert.True(entry.CreatedDirectory);
        Assert.Equal(new FileInfo(entry.ToPath).Length, entry.SizeBytes);

        // Why, in the three columns a bug report is answered from.
        Assert.Equal(DecidedBy.Prdb, entry.DecidedBy);
        Assert.Equal(MatchConfidence.Exact, entry.Confidence);
        Assert.Equal(MatchRung.OsHash, entry.MatchedBy);

        // And what it was filed as, as it was named at the time.
        Assert.Equal(HistoryWorkspace.Title, entry.SceneTitle);
        Assert.Equal(HistoryWorkspace.Site, entry.SceneSite);
        Assert.Equal(new DateOnly(2024, 5, 1), entry.SceneReleaseDate);
    }

    /// <summary>
    /// What went in next to the video is on the entry too, because it is what an
    /// undo takes away with it — the sidecar by its path, and the image by its
    /// path, its length and a fingerprint, since ADR 0027 left the file itself
    /// unmarked.
    /// </summary>
    [Fact]
    public async Task What_was_written_next_to_the_video_is_on_the_entry()
    {
        await workspace.ArtworkOnAsync();
        workspace.Recognised();
        workspace.Arrived("scene.1080p.mkv");
        await workspace.ReadyAsync();

        await workspace.FileAsync();

        var entry = Assert.Single(await workspace.OperationsAsync());

        Assert.Equal(Path.Combine(workspace.Scene, "movie.nfo"), entry.SidecarPath);
        Assert.Equal(Path.Combine(workspace.Scene, "fanart.jpg"), entry.ArtworkPath);
        Assert.Equal(new FileInfo(entry.ArtworkPath!).Length, entry.ArtworkBytes);
        Assert.NotNull(entry.ArtworkFingerprint);
    }

    /// <summary>
    /// A file nobody switched artwork on for gets no image and says nothing about
    /// one — the ordinary installation, and the one where an undo must not go
    /// looking for a file that was never written.
    /// </summary>
    [Fact]
    public async Task An_image_nobody_asked_for_is_not_on_the_entry()
    {
        workspace.Recognised();
        workspace.Arrived("scene.1080p.mkv");
        await workspace.ReadyAsync();

        await workspace.FileAsync();

        var entry = Assert.Single(await workspace.OperationsAsync());

        Assert.Null(entry.ArtworkPath);
        Assert.Null(entry.ArtworkFingerprint);
    }

    /// <summary>
    /// ADR 0023, in the log: a person's answer is recorded as a person's, with no
    /// rung and no grade. Recording prdb's would say the file was filed for a
    /// reason it was not.
    /// </summary>
    [Fact]
    public async Task A_persons_answer_is_recorded_as_a_persons()
    {
        workspace.Arrived("mystery.1080p.mkv");
        await workspace.ReadyAsync();
        await workspace.DecidedAsync("mystery.1080p.mkv", Guid.NewGuid());

        await workspace.FileAsync();

        var entry = Assert.Single(await workspace.OperationsAsync());

        Assert.Equal(DecidedBy.Person, entry.DecidedBy);
        Assert.Null(entry.Confidence);
        Assert.Null(entry.MatchedBy);
        Assert.Equal(workspace.Time.GetUtcNow(), entry.DecidedAt);
    }

    /// <summary>
    /// ADR 0020 asked for this: the rename that makes room for a second quality
    /// is its own entry, or an undo returns the newcomer and leaves the file it
    /// renamed under a name nobody chose.
    /// </summary>
    [Fact]
    public async Task A_relabel_is_an_entry_of_its_own()
    {
        var video = workspace.Recognised();
        workspace.Arrived("first.1080p.mkv", 1920, 1080);
        await workspace.ReadyAsync();
        await workspace.FileAsync();

        workspace.Recognised(videoId: video);
        workspace.Arrived("second.2160p.mkv", 3840, 2160);
        await workspace.ReadyAsync();
        await workspace.FileAsync();

        var entries = await workspace.OperationsAsync();

        Assert.Equal(3, entries.Count);

        var relabel = Assert.Single(entries, entry => entry.Kind is OperationKind.Relabelled);

        Assert.EndsWith("Scene Title.mkv", relabel.FromPath, StringComparison.Ordinal);
        Assert.EndsWith("Scene Title - [1080p].mkv", relabel.ToPath, StringComparison.Ordinal);

        // It happens before the file that caused it, which is what makes reading
        // a run backwards put the two right.
        Assert.True(relabel.Id < entries[^1].Id);
        Assert.False(relabel.CreatedDirectory);
    }

    /// <summary>
    /// A tool that says nothing about the night it found nothing to do is a tool
    /// whose silence has two meanings — for somebody who asked, which is what the
    /// row is owed to.
    /// </summary>
    [Fact]
    public async Task A_run_somebody_asked_for_still_leaves_its_row_when_it_moved_nothing()
    {
        await workspace.FileAsync();

        var run = Assert.Single(await workspace.RunsAsync());

        Assert.Empty(await workspace.OperationsAsync());
        Assert.Equal(AskedBy.Person, run.AskedBy);
        Assert.NotNull(run.FinishedAt);
        Assert.Equal("0 videos were filed.", run.Account);
    }

    /// <summary>
    /// And a run nobody asked for leaves nothing at all — ADR 0031, amending
    /// ADR 0028. A row every quarter of an hour would push three years of nights
    /// out of a thousand-run log inside a fortnight, and the trim would be
    /// dropping real history to keep a record of the tool doing nothing.
    /// </summary>
    [Fact]
    public async Task An_unattended_run_that_moved_nothing_leaves_no_row()
    {
        await workspace.FileAsync(AskedBy.Timer);

        Assert.Empty(await workspace.RunsAsync());
    }

    /// <summary>
    /// One that did move something is in the log exactly as an asked-for run is,
    /// and it says who asked: "you filed these" and "the tool filed these while
    /// nobody was watching" are different sentences.
    /// </summary>
    [Fact]
    public async Task An_unattended_run_that_moved_something_is_logged_as_the_timers()
    {
        workspace.Recognised();
        workspace.Arrived("scene.1080p.mkv");
        await workspace.ReadyAsync();

        await workspace.FileAsync(AskedBy.Timer);

        var run = Assert.Single(await workspace.RunsAsync());

        Assert.Equal(AskedBy.Timer, run.AskedBy);
        Assert.Equal("1 video was filed.", run.Account);
        Assert.Single(await workspace.OperationsAsync());
    }

    /// <summary>
    /// The trim, and the property the whole way back depends on: a run in the log
    /// can be undone as a run, or it is not in the log at all. Half a batch is
    /// the one state worth ruling out, because it is the state that looks
    /// complete and is not.
    /// </summary>
    [Fact]
    public async Task The_log_is_trimmed_by_whole_runs()
    {
        const int extra = 5;

        await workspace.WithContextAsync(async context =>
        {
            for (var number = 0; number < HistoryLimits.Runs + extra; number++)
            {
                var run = new OperationRun
                {
                    Kind = RunKind.Filing,
                    StartedAt = workspace.Time.GetUtcNow().AddMinutes(number),
                    FinishedAt = workspace.Time.GetUtcNow().AddMinutes(number),
                    Account = $"run {number}",
                };

                context.OperationRuns.Add(run);
                await context.SaveChangesAsync();

                context.Operations.AddRange(
                    Entry(run.Id, number, "a"),
                    Entry(run.Id, number, "b"));

                await context.SaveChangesAsync();
            }
        });

        await workspace.TrimAsync();

        var runs = await workspace.RunsAsync();
        var operations = await workspace.OperationsAsync();

        Assert.Equal(HistoryLimits.Runs, runs.Count);

        // The oldest went, and they went whole: what is left is exactly two
        // entries per surviving run, and none of the dropped runs left an entry
        // behind.
        Assert.Equal("run 5", runs[0].Account);
        Assert.Equal(HistoryLimits.Runs * 2, operations.Count);
        Assert.All(
            runs,
            run => Assert.Equal(2, operations.Count(entry => entry.RunId == run.Id)));
    }

    private static OperationEntry Entry(int runId, int number, string which) => new()
    {
        RunId = runId,
        Kind = OperationKind.Filed,
        FromPath = $"/downloads/{number}-{which}.mkv",
        ToPath = $"/library/scene {number} {which}/scene.mkv",
        Movement = FileMovement.Rename,
        DecidedBy = DecidedBy.Prdb,
        At = DateTimeOffset.UnixEpoch.AddMinutes(number),
    };
}

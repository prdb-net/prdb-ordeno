using Prdb.Ordeno.Core.Library;
using Prdb.Ordeno.Infrastructure.Tests.Library;

using Xunit;

namespace Prdb.Ordeno.Infrastructure.Tests.History;

/// <summary>
/// ADR 0030 against the whole loop: a file that was put back is found again,
/// asked about again, and still not filed.
/// </summary>
/// <remarks>
/// The point of running this through the real scan and the real identification
/// is that the hold has to survive them. An undone file is a file the tool has
/// forgotten — its row, prdb's answer and any decision went when it was filed
/// (ADR 0029) — so what stops the tool filing it again cannot be any of those,
/// and a test that wrote the rows by hand would not be testing that.
/// </remarks>
public sealed class FilingHoldTests : IAsyncLifetime
{
    private HistoryWorkspace workspace = null!;

    public async Task InitializeAsync() => workspace = await HistoryWorkspace.StartAsync();

    public async Task DisposeAsync() => await workspace.DisposeAsync();

    /// <summary>
    /// The sentence the whole decision is for: an unattended run that refiles
    /// overnight what somebody undid that evening is the way back cancelled by
    /// the feature it unblocked.
    /// </summary>
    [Fact]
    public async Task A_file_that_was_put_back_is_not_filed_again_by_a_run_nobody_watched()
    {
        var source = await FiledAsync("scene.1080p.mkv");
        await workspace.UndoAsync(runId: 1);

        // The ordinary loop takes it from there, exactly as ADR 0029 says: a
        // new row, a fresh question to prdb, and the same answer as yesterday.
        await workspace.ReadyAsync();

        var plan = Assert.Single((await workspace.PlanAsync()).Plans);
        Assert.Equal(FilingOutcome.Held, plan.Outcome);
        Assert.Contains("you put this file back", plan.Message, StringComparison.OrdinalIgnoreCase);

        var report = await workspace.FileAsync(Core.History.AskedBy.Timer);

        Assert.Equal(FilingResultState.Skipped, Assert.Single(report.Results).State);
        Assert.True(File.Exists(source));
        Assert.Empty(await workspace.FiledAsync());
    }

    /// <summary>
    /// And not by the button either — the load-bearing half of ADR 0030. A hold
    /// the button ignored would make the plan depend on who was asking, and
    /// somebody filing this morning's downloads would refile last night's two
    /// hundred without meaning to.
    /// </summary>
    [Fact]
    public async Task A_run_somebody_asked_for_does_not_file_it_either()
    {
        var source = await FiledAsync("scene.1080p.mkv");
        await workspace.UndoAsync(runId: 1);
        await workspace.ReadyAsync();

        var report = await workspace.FileAsync();

        Assert.Equal(FilingResultState.Skipped, Assert.Single(report.Results).State);
        Assert.True(File.Exists(source));
    }

    /// <summary>
    /// Releasing is the "until somebody says otherwise", and it moves nothing by
    /// itself: the plan and the button still stand between it and the library.
    /// </summary>
    [Fact]
    public async Task Releasing_it_makes_it_an_ordinary_file_again()
    {
        var source = await FiledAsync("scene.1080p.mkv");
        await workspace.UndoAsync(runId: 1);
        await workspace.ReadyAsync();

        Assert.Equal(1, await workspace.ReleaseAsync());

        // Nothing moved because of the release.
        Assert.True(File.Exists(source));
        Assert.Empty(await workspace.FiledAsync());

        var plan = Assert.Single((await workspace.PlanAsync()).Plans);
        Assert.Equal(FilingOutcome.Filed, plan.Outcome);

        Assert.True(Assert.Single((await workspace.FileAsync()).Results).Filed);
        Assert.False(File.Exists(source));
    }

    /// <summary>
    /// One file out of a hold, which is the case somebody who undid one file
    /// has. The other stays held.
    /// </summary>
    [Fact]
    public async Task One_file_can_be_released_on_its_own()
    {
        workspace.RecognisedSeparately("first.1080p.mkv", "second.1080p.mkv");
        workspace.Arrived("first.1080p.mkv");
        workspace.Arrived("second.1080p.mkv");
        await workspace.ReadyAsync();
        await workspace.FileAsync();

        await workspace.UndoAsync(runId: 1);
        await workspace.ReadyAsync();

        var first = (await workspace.PlanAsync()).Plans
            .Single(plan => plan.SourceName == "first.1080p.mkv");

        Assert.Equal(1, await workspace.ReleaseAsync(first.FileId));

        var plans = (await workspace.PlanAsync()).Plans.OrderBy(plan => plan.SourceName).ToList();

        Assert.Equal(FilingOutcome.Filed, plans[0].Outcome);
        Assert.Equal(FilingOutcome.Held, plans[1].Outcome);
    }

    /// <summary>
    /// The timer does not start a run to find out that everything waiting is
    /// held — ADR 0031. Working out what would happen reads the header of every
    /// settled video, and doing that four times an hour to arrive at "nothing"
    /// is work on somebody's NAS for nothing.
    /// </summary>
    [Fact]
    public async Task Nothing_is_waiting_while_the_only_candidate_is_held()
    {
        await FiledAsync("scene.1080p.mkv");
        await workspace.UndoAsync(runId: 1);
        await workspace.ReadyAsync();

        Assert.False(await workspace.AnythingWaitingAsync());

        await workspace.ReleaseAsync();

        Assert.True(await workspace.AnythingWaitingAsync());
    }

    /// <summary>
    /// A relabel moved nothing into a download directory, so it holds nothing.
    /// The file it renamed is still in the library under the name it had before
    /// the second quality arrived — ADR 0020, read backwards.
    /// </summary>
    [Fact]
    public async Task A_relabel_leaves_no_hold()
    {
        var video = workspace.Recognised();
        workspace.Arrived("first.1080p.mkv", 1920, 1080);
        await workspace.ReadyAsync();
        await workspace.FileAsync();

        workspace.Recognised(videoId: video);
        var second = workspace.Arrived("second.2160p.mkv", 3840, 2160);
        await workspace.ReadyAsync();
        await workspace.FileAsync();

        await workspace.UndoAsync(runId: 2);

        var hold = Assert.Single(await workspace.HoldsAsync());
        Assert.Equal(second, hold.Path);
    }

    /// <summary>
    /// The bytes at that path changed, so the hold is about a file nobody has
    /// seen yet — the same rule, in the same statement, that forgets prdb's
    /// answer and a person's decision (ADR 0023).
    /// </summary>
    [Fact]
    public async Task A_hold_goes_when_the_bytes_at_that_path_change()
    {
        var source = await FiledAsync("scene.1080p.mkv");
        await workspace.UndoAsync(runId: 1);
        await workspace.ScanAsync();

        Assert.Single(await workspace.HoldsAsync());

        // A different download, arriving under a name somebody reuses.
        TestVideos.Write(source, 1280, 720);
        workspace.Time.Advance(TimeSpan.FromMinutes(1));
        await workspace.ScanAsync();

        Assert.Empty(await workspace.HoldsAsync());
    }

    /// <summary>
    /// And it goes when the file does. A hold for a file that is not there
    /// decides nothing, and one that outlived its file would hold a download
    /// somebody puts at that name months later.
    /// </summary>
    [Fact]
    public async Task A_hold_goes_when_the_file_leaves_the_download_directory()
    {
        var source = await FiledAsync("scene.1080p.mkv");
        await workspace.UndoAsync(runId: 1);
        await workspace.ScanAsync();

        Assert.Single(await workspace.HoldsAsync());

        File.Delete(source);
        workspace.Time.Advance(TimeSpan.FromMinutes(1));
        await workspace.ScanAsync();

        Assert.Empty(await workspace.HoldsAsync());
    }

    /// <summary>
    /// A hold written after this scan started looking is not swept away by it.
    /// The walk had already passed that directory, so "there is no row for this
    /// path" means "I have not looked since", and sweeping it would leave the
    /// file unheld — the one outcome the hold exists to prevent.
    /// </summary>
    [Fact]
    public async Task A_hold_written_while_a_scan_was_under_way_survives_it()
    {
        await FiledAsync("scene.1080p.mkv");
        await workspace.UndoAsync(runId: 1);

        // The undo has put the file back and no scan has seen it yet, which is
        // exactly the state a scan that started a moment earlier would be in.
        Assert.Single(await workspace.HoldsAsync());

        await workspace.ScanAsync();

        Assert.Single(await workspace.HoldsAsync());
    }

    /// <summary>
    /// What the row says. It is the difference between a tool that is
    /// remembering and a tool that is failing, and it carries what the file had
    /// been filed as — which is what somebody deciding whether to release it
    /// actually wants.
    /// </summary>
    [Fact]
    public async Task The_hold_says_what_had_happened_to_the_file()
    {
        await FiledAsync("scene.1080p.mkv");
        var filed = Assert.Single(await workspace.OperationsAsync());

        await workspace.UndoAsync(runId: 1);

        var hold = Assert.Single(await workspace.HoldsAsync());

        Assert.Equal(filed.ToPath, hold.FiledTo);
        Assert.Equal(filed.At, hold.FiledAt);

        var words = new FilingHold(hold.HeldAt, hold.FiledAt, hold.FiledTo).InWords;

        Assert.Contains("Example Studio - 2024-05-01 - Scene Title.mkv", words, StringComparison.Ordinal);
        Assert.Contains("until you release it", words, StringComparison.Ordinal);
    }

    private async Task<string> FiledAsync(string relative)
    {
        workspace.Recognised();
        var source = workspace.Arrived(relative);
        await workspace.ReadyAsync();

        Assert.Single((await workspace.FileAsync()).Results);

        return source;
    }
}

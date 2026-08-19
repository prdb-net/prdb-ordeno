using Prdb.Ordeno.Core.History;
using Prdb.Ordeno.Infrastructure.Tests.Library;

using Xunit;

namespace Prdb.Ordeno.Infrastructure.Tests.History;

/// <summary>
/// ADR 0029 against a real filesystem: what the tool filed goes back where it
/// came from, what it wrote next to it goes with it, and everything it cannot be
/// sure about is left exactly as it is.
/// </summary>
/// <remarks>
/// The interesting failures here are the ones a mocked file layer cannot have —
/// a file somebody replaced, a name that is taken again, a share that is not
/// mounted — so all of it runs against real files in a temporary directory.
/// </remarks>
public sealed class UndoServiceTests : IAsyncLifetime
{
    private HistoryWorkspace workspace = null!;

    public async Task InitializeAsync() => workspace = await HistoryWorkspace.StartAsync();

    public async Task DisposeAsync() => await workspace.DisposeAsync();

    /// <summary>
    /// The line the whole issue is about, read backwards: the video is in the
    /// download directory again, the library does not hold it, and the row that
    /// said it did is gone.
    /// </summary>
    [Fact]
    public async Task A_filed_video_goes_back_where_it_came_from()
    {
        var source = await FiledAsync("Example.Studio.24.05.01.Scene.Title.1080p.mkv");
        var filed = Assert.Single(await workspace.OperationsAsync());

        var report = await workspace.UndoAsync(runId: filed.RunId);

        var result = Assert.Single(report.Results);
        Assert.Equal(UndoResultState.Returned, result.State);
        Assert.True(File.Exists(source));
        Assert.False(File.Exists(filed.ToPath));
        Assert.Empty(await workspace.FiledAsync());

        // And the directory the run made goes with it, because nothing is left
        // in it — a library with an empty scene directory in it is not the
        // library the run found.
        Assert.False(Directory.Exists(workspace.Scene));
    }

    /// <summary>
    /// The sidecar and the image were written by this run, so they go with the
    /// video. Neither is written over anything, and neither is removed on the
    /// strength of the log alone: both are asked about at the moment of the
    /// removal.
    /// </summary>
    [Fact]
    public async Task What_the_run_wrote_next_to_it_goes_with_it()
    {
        await workspace.ArtworkOnAsync();

        await FiledAsync("scene.1080p.mkv");

        Assert.True(File.Exists(Path.Combine(workspace.Scene, "movie.nfo")));
        Assert.True(File.Exists(Path.Combine(workspace.Scene, "fanart.jpg")));

        var result = Assert.Single((await workspace.UndoAsync(runId: 1)).Results);

        Assert.Equal(UndoResultState.Returned, result.State);
        Assert.Null(result.Leftovers);
        Assert.False(Directory.Exists(workspace.Scene));
    }

    /// <summary>
    /// ADR 0024's marker decides this, not the log: a user who wrote their own
    /// <c>movie.nfo</c> over the tool's has taken the file back, and an undo is
    /// exactly as bound by that as a rewrite is.
    /// </summary>
    [Fact]
    public async Task A_sidecar_somebody_else_wrote_is_left_where_it_is()
    {
        await FiledAsync("scene.1080p.mkv");

        var sidecar = Path.Combine(workspace.Scene, "movie.nfo");
        await File.WriteAllTextAsync(sidecar, "<movie><title>Mine</title></movie>");

        var result = Assert.Single((await workspace.UndoAsync(runId: 1)).Results);

        Assert.Equal(UndoResultState.Returned, result.State);
        Assert.True(File.Exists(sidecar));
        Assert.Contains("was not written by this tool", result.Leftovers);

        // And the directory stays, because there is something in it that is not
        // the tool's to remove.
        Assert.True(Directory.Exists(workspace.Scene));
    }

    /// <summary>
    /// The half ADR 0027 left no other way to answer. An image carries no
    /// marker, so "is this still the file this run downloaded" is a question
    /// only the fingerprint in the log can answer — and when the answer is no,
    /// the file stays.
    /// </summary>
    [Fact]
    public async Task An_image_that_is_not_the_one_it_downloaded_is_left_where_it_is()
    {
        await workspace.ArtworkOnAsync();

        await FiledAsync("scene.1080p.mkv");

        var image = Path.Combine(workspace.Scene, "fanart.jpg");
        await File.WriteAllBytesAsync(image, FakeCdn.Jpeg(filling: 128));

        var result = Assert.Single((await workspace.UndoAsync(runId: 1)).Results);

        Assert.Equal(UndoResultState.Returned, result.State);
        Assert.True(File.Exists(image));
        Assert.Contains("not the image this run downloaded", result.Leftovers);
    }

    /// <summary>
    /// A file that has changed since it was filed is somebody's work, whatever
    /// it is called. Nothing is moved, and the reason is on the row.
    /// </summary>
    [Fact]
    public async Task A_file_that_has_changed_since_is_not_touched()
    {
        var source = await FiledAsync("scene.1080p.mkv");
        var filed = Assert.Single(await workspace.OperationsAsync());

        await File.AppendAllTextAsync(filed.ToPath, "not what was filed");

        var result = Assert.Single((await workspace.UndoAsync(runId: 1)).Results);

        Assert.Equal(UndoResultState.Refused, result.State);
        Assert.Equal(UndoRefusal.Changed, result.Plan.Refusal);
        Assert.True(File.Exists(filed.ToPath));
        Assert.False(File.Exists(source));
        Assert.Single(await workspace.FiledAsync());
    }

    /// <summary>
    /// The right length and the wrong file: only the exact hash catches this
    /// one, and the tool records one for every file large enough to have one.
    /// </summary>
    [Fact]
    public async Task A_file_of_the_right_length_and_the_wrong_bytes_is_not_touched()
    {
        workspace.Recognised();
        workspace.Arrived("scene.1080p.mkv", lossless: true);
        await workspace.ReadyAsync();
        await workspace.FileAsync();

        var filed = Assert.Single(await workspace.OperationsAsync());

        // Over the 128 KiB the exact hash needs, which is the whole point of
        // this fixture: below it there is no hash and the length is the only
        // check there can be.
        Assert.NotNull(filed.OsHash);

        var bytes = await File.ReadAllBytesAsync(filed.ToPath);
        bytes[1024] ^= 0xFF;
        await File.WriteAllBytesAsync(filed.ToPath, bytes);

        var result = Assert.Single((await workspace.UndoAsync(runId: 1)).Results);

        Assert.Equal(UndoRefusal.Changed, result.Plan.Refusal);
        Assert.True(File.Exists(filed.ToPath));
    }

    /// <summary>A reversal that overwrites is not a reversal.</summary>
    [Fact]
    public async Task Something_already_at_the_way_back_stops_it()
    {
        var source = await FiledAsync("scene.1080p.mkv");
        await File.WriteAllTextAsync(source, "something else entirely");

        var result = Assert.Single((await workspace.UndoAsync(runId: 1)).Results);

        Assert.Equal(UndoRefusal.Occupied, result.Plan.Refusal);
        Assert.Equal("something else entirely", await File.ReadAllTextAsync(source));
        Assert.Single(await workspace.FiledAsync());
    }

    /// <summary>
    /// "The original location is gone", which on this audience's storage is
    /// usually a share that is not mounted — and putting two hundred files into
    /// what is really a mountpoint is how a NAS fills its system disk.
    /// </summary>
    [Fact]
    public async Task A_download_directory_that_is_gone_stops_it()
    {
        await FiledAsync("below/scene.1080p.mkv");

        Directory.Delete(Path.Combine(workspace.Downloads, "below"), recursive: true);

        var result = Assert.Single((await workspace.UndoAsync(runId: 1)).Results);

        Assert.Equal(UndoRefusal.NoWayBack, result.Plan.Refusal);
    }

    /// <summary>
    /// ADR 0020 read backwards, which is what reverse order buys: the newcomer
    /// leaves first, and only then is the file it relabelled renamed back.
    /// </summary>
    [Fact]
    public async Task A_second_quality_and_the_rename_it_caused_come_back_together()
    {
        var video = workspace.Recognised();
        workspace.Arrived("first.1080p.mkv", 1920, 1080);
        await workspace.ReadyAsync();
        await workspace.FileAsync();

        workspace.Recognised(videoId: video);
        var second = workspace.Arrived("second.2160p.mkv", 3840, 2160);
        await workspace.ReadyAsync();
        await workspace.FileAsync();

        Assert.Equal(
            [
                "Example Studio - 2024-05-01 - Scene Title - [1080p].mkv",
                "Example Studio - 2024-05-01 - Scene Title - [2160p].mkv",
            ],
            HistoryWorkspace.VideosIn(workspace.Scene));

        // The second run is the one that both relabelled and filed.
        var report = await workspace.UndoAsync(runId: 2);

        Assert.Equal(2, report.Results.Count);
        Assert.All(report.Results, result => Assert.Equal(UndoResultState.Returned, result.State));

        // The newcomer is back in the download directory, and the file that was
        // there first carries the name it had before it arrived.
        Assert.True(File.Exists(second));
        Assert.Equal(
            ["Example Studio - 2024-05-01 - Scene Title.mkv"],
            HistoryWorkspace.VideosIn(workspace.Scene));

        var filed = Assert.Single(await workspace.FiledAsync());
        Assert.Equal("Example Studio - 2024-05-01 - Scene Title.mkv", filed.FileName);
    }

    /// <summary>
    /// Reverse chronological order between runs, not only inside one: the run
    /// that filed the first copy cannot go back while a later run's rename is
    /// still in place, and the refusal names the run to undo first.
    /// </summary>
    [Fact]
    public async Task A_run_a_later_one_renamed_out_of_is_refused_until_that_one_goes_back()
    {
        var video = workspace.Recognised();
        workspace.Arrived("first.1080p.mkv", 1920, 1080);
        await workspace.ReadyAsync();
        await workspace.FileAsync();

        workspace.Recognised(videoId: video);
        workspace.Arrived("second.2160p.mkv", 3840, 2160);
        await workspace.ReadyAsync();
        await workspace.FileAsync();

        var refused = Assert.Single((await workspace.UndoAsync(runId: 1)).Results);

        Assert.Equal(UndoRefusal.RenamedLater, refused.Plan.Refusal);
        Assert.Contains("Undo that run first", refused.Message);

        // And once the later run is back where it started, the earlier one can
        // go too.
        await workspace.UndoAsync(runId: 2);

        var returned = Assert.Single((await workspace.UndoAsync(runId: 1)).Results);

        Assert.Equal(UndoResultState.Returned, returned.State);
        Assert.False(Directory.Exists(workspace.Scene));
        Assert.Empty(await workspace.FiledAsync());
    }

    /// <summary>
    /// One operation rather than a run — the file somebody is looking at. The
    /// rest of the run is untouched, and the run still shows as partly undone.
    /// </summary>
    [Fact]
    public async Task One_operation_can_be_put_back_on_its_own()
    {
        workspace.RecognisedSeparately("first.1080p.mkv", "second.1080p.mkv");
        workspace.Arrived("first.1080p.mkv", 1920, 1080);
        workspace.Arrived("second.1080p.mkv", 1280, 720);
        await workspace.ReadyAsync();
        await workspace.FileAsync();

        var operations = await workspace.OperationsAsync();
        Assert.Equal(2, operations.Count);

        var result = Assert.Single((await workspace.UndoAsync(operationId: operations[0].Id)).Results);

        Assert.Equal(UndoResultState.Returned, result.State);
        Assert.True(File.Exists(operations[0].FromPath));
        Assert.True(File.Exists(operations[1].ToPath));

        var run = Assert.Single((await workspace.HistoryAsync()).Runs, one => one.Kind is RunKind.Filing);
        Assert.Equal(2, run.Operations);
        Assert.Equal(1, run.Undone);
        Assert.True(run.CanBeUndone);
    }

    /// <summary>
    /// Partial is reported, never hidden: one refusal does not stop the ones
    /// after it, and the report says which did not go back.
    /// </summary>
    [Fact]
    public async Task A_partial_undo_says_which_did_not_go_back()
    {
        workspace.RecognisedSeparately("first.1080p.mkv", "second.1080p.mkv");
        workspace.Arrived("first.1080p.mkv", 1920, 1080);
        var second = workspace.Arrived("second.1080p.mkv", 1280, 720);
        await workspace.ReadyAsync();
        await workspace.FileAsync();

        // Somebody has put a file back at one of the two names by hand.
        await File.WriteAllTextAsync(second, "in the way");

        var report = await workspace.UndoAsync(runId: 1);

        Assert.Equal(2, report.Results.Count);
        Assert.Single(report.Results, result => result.Returned);
        Assert.Single(report.Results, result => result.Plan.Refusal is UndoRefusal.Occupied);
    }

    /// <summary>
    /// The record answers a second attempt rather than the filesystem, and the
    /// undo itself is a run in the log that nothing offers to undo.
    /// </summary>
    [Fact]
    public async Task An_undo_is_recorded_and_is_not_itself_undoable()
    {
        await FiledAsync("scene.1080p.mkv");
        await workspace.UndoAsync(runId: 1);

        var again = Assert.Single((await workspace.UndoAsync(runId: 1)).Results);
        Assert.Equal(UndoRefusal.AlreadyUndone, again.Plan.Refusal);

        var history = await workspace.HistoryAsync();

        // Two of them: the second attempt moved nothing and still left its row,
        // for the reason a filing run that moves nothing leaves one.
        var undos = history.Runs.Where(run => run.Kind is RunKind.Undo).ToList();

        Assert.Equal(2, undos.Count);
        Assert.All(undos, run => Assert.False(run.CanBeUndone));
        Assert.Contains("1 file went back", undos[^1].InWords);

        var filing = Assert.Single(history.Runs, run => run.Kind is RunKind.Filing);
        Assert.False(filing.CanBeUndone);
        Assert.Equal(filing.Operations, filing.Undone);
    }

    /// <summary>
    /// A check moves nothing. It is the half of ADR 0029 that would be easy to
    /// get wrong once the two share a code path — and the same rule the filing
    /// preview lives under.
    /// </summary>
    [Fact]
    public async Task Checking_what_would_happen_moves_nothing()
    {
        var source = await FiledAsync("scene.1080p.mkv");
        var filed = Assert.Single(await workspace.OperationsAsync());

        var preview = await workspace.CheckAsync(runId: 1);

        Assert.Single(preview.Plans);
        Assert.True(preview.Plans[0].Returns);
        Assert.True(File.Exists(filed.ToPath));
        Assert.False(File.Exists(source));
        Assert.Single(await workspace.FiledAsync());
    }

    /// <summary>
    /// A run that has been trimmed out of the log is not a run that half exists.
    /// The answer is a sentence rather than an empty list, because an empty list
    /// reads as "nothing to do here" — which is exactly what it is not.
    /// </summary>
    [Fact]
    public async Task A_run_that_is_not_in_the_log_says_so()
    {
        var report = await workspace.UndoAsync(runId: 404);

        Assert.Empty(report.Results);
        Assert.Contains("not in the log", report.Problem);
    }

    /// <summary>One video, filed, with its download path handed back.</summary>
    private async Task<string> FiledAsync(string relative)
    {
        workspace.Recognised();
        var source = workspace.Arrived(relative);
        await workspace.ReadyAsync();

        var report = await workspace.FileAsync();
        Assert.Single(report.Results);

        return source;
    }

}

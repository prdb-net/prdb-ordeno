using Prdb.Ordeno.Core.Configuration;
using Prdb.Ordeno.Core.History;
using Prdb.Ordeno.Core.Identification;
using Prdb.Ordeno.Core.Library;

using Xunit;

namespace Prdb.Ordeno.Core.Tests.History;

/// <summary>
/// ADR 0029's refusals, one test each. Every one of them is a file somebody
/// cannot get back, so the question this file asks over and over is the same:
/// when the tool is not sure, does it do nothing?
/// </summary>
public sealed class UndoPlannerTests
{
    private const string Source = "/downloads/Example.Studio.24.05.01.Scene.Title.1080p.mkv";

    private const string Target =
        "/library/Example Studio/Example Studio - 2024-05-01 - Scene Title/"
        + "Example Studio - 2024-05-01 - Scene Title.mkv";

    [Fact]
    public void A_file_that_is_where_it_was_filed_goes_back()
    {
        var plan = UndoPlanner.Plan(Filed(), Present());

        Assert.Equal(UndoOutcome.Returns, plan.Outcome);
        Assert.Equal(UndoRefusal.None, plan.Refusal);
        Assert.Contains(Source, plan.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The record answers this rather than the filesystem, which is cheaper and
    /// more honest: what is at the path may be a file with the same name that
    /// somebody put back by hand.
    /// </summary>
    [Fact]
    public void One_that_has_been_put_back_already_is_refused()
    {
        var plan = UndoPlanner.Plan(
            Filed() with { UndoneAt = DateTimeOffset.UnixEpoch },
            Present());

        Assert.Equal(UndoRefusal.AlreadyUndone, plan.Refusal);
    }

    [Fact]
    public void One_that_is_no_longer_where_it_was_filed_is_refused()
    {
        var plan = UndoPlanner.Plan(Filed(), new UndoObservation(FiledFileState.Missing));

        Assert.Equal(UndoRefusal.Missing, plan.Refusal);
        Assert.Contains("does not go looking", plan.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The cheap half of "is this the file that was filed". A file of a different
    /// length is somebody's work, whatever it is called.
    /// </summary>
    [Fact]
    public void One_of_a_different_length_is_refused()
    {
        var plan = UndoPlanner.Plan(Filed(), Present() with { SizeBytes = 4_000 });

        Assert.Equal(UndoRefusal.Changed, plan.Refusal);
    }

    /// <summary>The exact half: the right length and the wrong file.</summary>
    [Fact]
    public void One_that_hashes_differently_is_refused()
    {
        var plan = UndoPlanner.Plan(Filed(), Present() with { OsHash = "ffffffffffffffff" });

        Assert.Equal(UndoRefusal.Changed, plan.Refusal);
        Assert.Contains("right length", plan.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A hash that could not be read is not a hash that matches. Treating an
    /// unreadable file as unchanged is how an undo moves something it never
    /// looked at.
    /// </summary>
    [Fact]
    public void One_whose_hash_cannot_be_read_is_refused()
    {
        var plan = UndoPlanner.Plan(Filed(), Present() with { OsHash = null });

        Assert.Equal(UndoRefusal.Unreadable, plan.Refusal);
    }

    /// <summary>
    /// A file under 128 KiB never had an <c>osHash</c>, so the length is the
    /// whole check — and it is enough to act on.
    /// </summary>
    [Fact]
    public void One_that_never_had_a_hash_is_judged_on_its_length_alone()
    {
        var plan = UndoPlanner.Plan(
            Filed() with { OsHash = null },
            Present() with { OsHash = null });

        Assert.Equal(UndoOutcome.Returns, plan.Outcome);
    }

    /// <summary>
    /// "The original location is gone" — and on this audience's storage it is
    /// usually a share that is not mounted, which looks exactly like an empty
    /// path to write two hundred files into.
    /// </summary>
    [Fact]
    public void One_whose_download_directory_is_gone_is_refused()
    {
        var plan = UndoPlanner.Plan(Filed(), Present() with { SourceDirectoryExists = false });

        Assert.Equal(UndoRefusal.NoWayBack, plan.Refusal);
    }

    [Fact]
    public void One_whose_way_back_is_occupied_is_refused()
    {
        var plan = UndoPlanner.Plan(Filed(), Present() with { SourceOccupied = true });

        Assert.Equal(UndoRefusal.Occupied, plan.Refusal);
        Assert.Contains("not a reversal", plan.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reverse chronological order between runs as well as inside one: whatever
    /// is at that name now belongs to the later run, and undoing that one first
    /// is what makes this one possible.
    /// </summary>
    [Fact]
    public void One_a_later_run_renamed_is_refused_and_names_that_run()
    {
        var plan = UndoPlanner.Plan(
            Filed(),
            Present() with { RenamedBy = "the run of 2026-08-19 16:44 UTC" });

        Assert.Equal(UndoRefusal.RenamedLater, plan.Refusal);
        Assert.Contains("2026-08-19 16:44", plan.Message, StringComparison.Ordinal);
        Assert.Contains("Undo that run first", plan.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The refusal is decided before anything about the file itself, because
    /// what is at that path is not the file the entry is about at all.
    /// </summary>
    [Fact]
    public void A_later_rename_outranks_the_state_of_the_file()
    {
        var plan = UndoPlanner.Plan(
            Filed(),
            new UndoObservation(FiledFileState.Missing, RenamedBy: "a later run"));

        Assert.Equal(UndoRefusal.RenamedLater, plan.Refusal);
    }

    /// <summary>
    /// What the row says before somebody presses the button. The sidecar and the
    /// image are named because they go with the video, and the qualification is
    /// not decoration: whether they are still the tool's own is asked at the
    /// moment of the removal.
    /// </summary>
    [Fact]
    public void What_it_would_do_names_what_goes_with_the_video()
    {
        var plan = UndoPlanner.Plan(
            Filed() with
            {
                Sidecar = new WrittenSidecar("/library/scene/movie.nfo"),
                Artwork = new WrittenArtwork("/library/scene/fanart.jpg", 1024, "abc"),
                CreatedDirectory = true,
            },
            Present());

        Assert.Contains("movie.nfo", plan.Message, StringComparison.Ordinal);
        Assert.Contains("fanart.jpg", plan.Message, StringComparison.Ordinal);
        Assert.Contains("if they are still the files it wrote", plan.Message, StringComparison.Ordinal);
        Assert.Contains("scene directory is removed", plan.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A relabel is the same shape and a different sentence: nothing leaves the
    /// library, the file goes back to the name it had before a second quality
    /// arrived.
    /// </summary>
    [Fact]
    public void A_relabel_says_it_is_renamed_back()
    {
        var plan = UndoPlanner.Plan(
            Filed() with
            {
                Kind = OperationKind.Relabelled,
                From = "/library/scene/Scene.mkv",
                To = "/library/scene/Scene - [1080p].mkv",
                SizeBytes = null,
                OsHash = null,
            },
            new UndoObservation(FiledFileState.Present, 2_000));

        Assert.Equal(UndoOutcome.Returns, plan.Outcome);
        Assert.Contains("renamed back to 'Scene.mkv'", plan.Message, StringComparison.Ordinal);
    }

    /// <summary>The reason is on every entry, and it says who decided — ADR 0023.</summary>
    [Fact]
    public void The_reason_says_who_decided()
    {
        Assert.Equal(
            "prdb matched it by its file hash, an exact match.",
            new OperationReason(DecidedBy.Prdb, MatchConfidence.Exact, MatchRung.OsHash).InWords);

        Assert.Equal(
            "You named this video in the review queue.",
            new OperationReason(DecidedBy.Person).InWords);
    }

    /// <summary>
    /// A person's answer carries no rung and no grade, because they did not come
    /// from a ladder. Recording prdb's next to it would say the file was filed
    /// for a reason it was not.
    /// </summary>
    [Fact]
    public void A_persons_answer_carries_no_rung()
    {
        var reason = OperationReason.From(
            new Recognition(
                MatchConfidence.Probable,
                MatchRung.FileName,
                Guid.NewGuid(),
                "Something else",
                null,
                "Another site",
                0,
                DateTimeOffset.UnixEpoch),
            new Core.Review.Resolution(
                Core.Review.ResolutionKind.Assigned,
                Core.Review.ResolvedFrom.Search,
                DateTimeOffset.UnixEpoch.AddDays(1),
                Guid.NewGuid()));

        Assert.Equal(DecidedBy.Person, reason.DecidedBy);
        Assert.Null(reason.MatchedBy);
        Assert.Null(reason.Confidence);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddDays(1), reason.DecidedAt);
    }

    private static LoggedOperation Filed() => new(
        Id: 7,
        RunId: 3,
        OperationKind.Filed,
        new Scene(Guid.NewGuid(), "Example Studio", "Scene Title", new DateOnly(2024, 5, 1)),
        Source,
        Target,
        "1080p",
        FileMovement.Rename,
        SizeBytes: 3_000,
        OsHash: "aabbccddeeff0011",
        CreatedDirectory: false,
        Sidecar: null,
        Artwork: null,
        new OperationReason(DecidedBy.Prdb, MatchConfidence.Exact, MatchRung.OsHash),
        DateTimeOffset.UnixEpoch);

    /// <summary>The file, where it was filed, unchanged, with a clear way back.</summary>
    private static UndoObservation Present() =>
        new(FiledFileState.Present, 3_000, "aabbccddeeff0011");
}

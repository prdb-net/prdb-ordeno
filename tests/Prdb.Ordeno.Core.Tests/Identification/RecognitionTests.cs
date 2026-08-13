using Prdb.Ordeno.Core.Identification;

using Xunit;

namespace Prdb.Ordeno.Core.Tests.Identification;

/// <summary>
/// What a row on the downloads screen says once prdb has answered. It is tested
/// because three of the four answers are results and one is a dead end, and a
/// screen that presents them as the same thing turns a working tool into one
/// that looks broken.
/// </summary>
public sealed class RecognitionTests
{
    [Fact]
    public void A_named_video_reads_as_the_video_it_is()
    {
        var recognition = Answer(
            MatchConfidence.Exact,
            MatchRung.OsHash,
            videoId: Guid.NewGuid(),
            title: "A Scene",
            releaseDate: new DateOnly(2024, 5, 1),
            siteTitle: "A Site");

        Assert.Equal(RecognitionState.Recognised, recognition.State);
        Assert.Equal("A Scene — A Site, 2024-05-01", recognition.InWords);
        Assert.Equal("matched by file hash", recognition.Because);
    }

    /// <summary>
    /// ADR 0001: prdb names the candidates rather than choosing, and the screen
    /// has to say that rather than picking the first one to have something to
    /// show.
    /// </summary>
    [Fact]
    public void An_ambiguous_answer_says_prdb_did_not_choose()
    {
        var recognition = Answer(
            MatchConfidence.Ambiguous,
            MatchRung.ReleaseName,
            candidates: 3);

        Assert.Equal(RecognitionState.Ambiguous, recognition.State);
        Assert.Contains("3 videos fit equally well", recognition.InWords, StringComparison.Ordinal);
        Assert.Contains("did not choose", recognition.InWords, StringComparison.Ordinal);
    }

    /// <summary>
    /// The one that must not read as a failure. A file filed under the right
    /// site is further along than one still in the download directory.
    /// </summary>
    [Fact]
    public void A_known_site_is_a_result_and_not_a_failure()
    {
        var recognition = Answer(MatchConfidence.Partial, MatchRung.Site, siteTitle: "A Site");

        Assert.Equal(RecognitionState.SiteOnly, recognition.State);
        Assert.Contains("A Site", recognition.InWords, StringComparison.Ordinal);
        Assert.DoesNotContain("Not recognised", recognition.InWords, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_matching_is_said_plainly()
    {
        var recognition = Answer(MatchConfidence.None, matchedBy: null);

        Assert.Equal(RecognitionState.Unrecognised, recognition.State);
        Assert.Equal("Not recognised.", recognition.InWords);
        Assert.Null(recognition.Because);
    }

    /// <summary>
    /// A rung this build has no name for is a newer prdb. The answer is still
    /// the answer; only the explanation is missing.
    /// </summary>
    [Fact]
    public void A_video_with_no_title_still_reads_as_recognised()
    {
        var recognition = Answer(MatchConfidence.Strong, MatchRung.FileName, videoId: Guid.NewGuid());

        Assert.Equal(RecognitionState.Recognised, recognition.State);
        Assert.Equal("A video prdb knows", recognition.InWords);
    }

    [Fact]
    public void Nothing_asked_about_yet_is_reported_as_waiting_rather_than_as_zeroes()
    {
        var summary = new RecognitionSummary(0, 0, 0, 0, Waiting: 12);

        Assert.Contains("12 videos are waiting", summary.WhatItRecognised!, StringComparison.Ordinal);
    }

    /// <summary>
    /// A tool that has found nothing has nothing to say about recognition, and
    /// saying it in four zeroes is worse than silence.
    /// </summary>
    [Fact]
    public void An_empty_installation_says_nothing_at_all()
    {
        Assert.Null(RecognitionSummary.Nothing.WhatItRecognised);
    }

    [Fact]
    public void The_counts_read_as_a_sentence()
    {
        var summary = new RecognitionSummary(
            Recognised: 1_204,
            Ambiguous: 3,
            SiteOnly: 11,
            Unrecognised: 42,
            Waiting: 8);

        var sentence = summary.WhatItRecognised!;

        Assert.Equal(1_260, summary.Answered);
        Assert.Contains("Of 1,260 asked about:", sentence, StringComparison.Ordinal);
        Assert.Contains("1,204 recognised", sentence, StringComparison.Ordinal);
        Assert.Contains("11 known by site only", sentence, StringComparison.Ordinal);
        Assert.Contains("and 42 not recognised", sentence, StringComparison.Ordinal);
        Assert.Contains("8 still to ask about", sentence, StringComparison.Ordinal);
    }

    private static Recognition Answer(
        MatchConfidence confidence,
        MatchRung? matchedBy,
        Guid? videoId = null,
        string? title = null,
        DateOnly? releaseDate = null,
        string? siteTitle = null,
        int candidates = 0) =>
        new(
            confidence,
            matchedBy,
            videoId,
            title,
            releaseDate,
            siteTitle,
            candidates,
            AskedAt: new DateTimeOffset(2026, 8, 13, 10, 0, 0, TimeSpan.Zero));
}

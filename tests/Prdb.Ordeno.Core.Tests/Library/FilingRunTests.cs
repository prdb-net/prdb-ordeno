using Prdb.Ordeno.Core.Configuration;
using Prdb.Ordeno.Core.Library;

using Xunit;

namespace Prdb.Ordeno.Core.Tests.Library;

/// <summary>
/// The one line somebody reads after a run. A screen shows two hundred rows of a
/// run that may have been thousands of files, so what is true of the run has to
/// be in the sentence rather than only in the rows.
/// </summary>
public sealed class FilingRunTests
{
    private static readonly Scene Scene =
        new(Guid.NewGuid(), "Example Studio", "Scene Title", new DateOnly(2025, 11, 3));

    [Fact]
    public void A_run_that_filed_everything_says_only_that() =>
        Assert.Equal("2 videos were filed.", Did([Filed(), Filed()]));

    /// <summary>
    /// prdb being unreachable is a thing about the run rather than about one
    /// file: every video is in the library and none of them carries what the
    /// media server reads.
    /// </summary>
    [Fact]
    public void A_run_that_could_not_ask_prdb_says_so_in_the_sentence() =>
        Assert.Equal(
            "2 videos were filed and none of them could be given the metadata file the media "
            + "server reads, and the rows say why.",
            Did([Filed("prdb could not be reached."), Filed("prdb could not be reached.")]));

    [Fact]
    public void One_sidecar_that_could_not_be_written_is_counted_rather_than_generalised() =>
        Assert.Contains(
            "1 of them could not be given the metadata file",
            Did([Filed(), Filed("The share went away.")]));

    /// <summary>
    /// A sidecar somebody else wrote is not a failure, and a run that left one
    /// alone did nothing that needs explaining at the top of the screen.
    /// </summary>
    [Fact]
    public void A_sidecar_that_was_deliberately_left_alone_is_not_counted() =>
        Assert.Equal(
            "1 video was filed.",
            Did([Filed("It was left exactly as it is.", SidecarAction.Keep)]));

    /// <summary>
    /// One CDN having a bad afternoon is a fact about the run rather than about
    /// each file, and a user should not have to read two hundred rows to learn
    /// it — ADR 0027's images get the same line the sidecar does.
    /// </summary>
    [Fact]
    public void Images_that_did_not_arrive_are_counted_at_the_top_of_the_screen() =>
        Assert.Contains(
            "1 did not get the image that was to go next to it",
            Did(
            [
                Filed(images: ArtworkAction.Write),
                Filed(artwork: "The CDN could not be reached.", images: ArtworkAction.Write),
            ]));

    /// <summary>
    /// A scene prdb has no image for says nothing and is counted as nothing. It
    /// is the ordinary outcome, and a run that called it a shortfall would be
    /// reporting the absence of something nobody was promised.
    /// </summary>
    [Fact]
    public void A_scene_with_no_image_to_download_is_not_a_shortfall() =>
        Assert.Equal(
            "1 video was filed.",
            Did([Filed(images: ArtworkAction.Write)]));

    private static string? Did(IReadOnlyList<FilingResult> results) =>
        FilingRun.Never.Filed(DateTimeOffset.UnixEpoch, results).WhatItDid;

    private static FilingResult Filed(
        string? sidecar = null,
        SidecarAction action = SidecarAction.Write,
        string? artwork = null,
        ArtworkAction images = ArtworkAction.None) =>
        new(
            FilingResultState.Filed,
            new FilingPlan(
                FilingOutcome.Filed,
                FileId: 1,
                "/downloads/scene.mkv",
                "scene.mkv",
                Scene,
                "1080p",
                "/library/scene",
                "/library/scene/scene.mkv",
                Relabel: null,
                FileMovement.Rename,
                Message: null,
                new SidecarPlan(action, "/library/scene/movie.nfo"),
                new ArtworkPlan(images, "/library/scene/fanart.jpg")),
            Message: null,
            sidecar,
            artwork);
}

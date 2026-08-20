using Prdb.Ordeno.Core.Library;

using Xunit;

namespace Prdb.Ordeno.Core.Tests.Library;

/// <summary>
/// What a refresh is allowed to do to one scene — ADR 0033, apart from any
/// filesystem.
/// </summary>
/// <remarks>
/// The rule under nearly every test here is that the tool writes over its own
/// document and into an empty name, and does nothing else ever. The one that is
/// not about that is the comparison: a document identical to what would be
/// written is left alone, which is what stops a nightly run rewriting every
/// scene in the library and telling a media server about all of them.
/// </remarks>
public sealed class SceneRefreshTests
{
    private static readonly SceneMetadata Scene = new(
        Guid.NewGuid(),
        "Scene Title",
        new DateOnly(2024, 5, 1),
        "Example Studio",
        ["A Performer"]);

    private static readonly SceneMetadata Corrected = Scene with { Title = "Corrected Title" };

    [Fact]
    public void A_document_that_already_says_what_prdb_says_is_left_alone()
    {
        var plan = SceneRefresh.Decide(
            SidecarState.Ours,
            MovieNfo.For(Scene),
            Scene,
            ArtworkState.Present,
            downloadArtwork: true);

        Assert.False(plan.Writes);
        Assert.Null(plan.Sidecar);
    }

    /// <summary>
    /// The case `VISION.md` names, and the reason the whole feature exists: the
    /// file written last spring still says the old thing.
    /// </summary>
    [Fact]
    public void A_corrected_title_is_written_over_the_tools_own_document()
    {
        var plan = SceneRefresh.Decide(
            SidecarState.Ours,
            MovieNfo.For(Scene),
            Corrected,
            ArtworkState.Present,
            downloadArtwork: false);

        Assert.Equal(MovieNfo.For(Corrected), plan.Sidecar);
    }

    /// <summary>
    /// ADR 0024 left a scene whose sidecar has gone missing to the next filing.
    /// This is what covers it instead, and writing into an empty name destroys
    /// nothing.
    /// </summary>
    [Fact]
    public void A_missing_document_is_written()
    {
        var plan = SceneRefresh.Decide(
            SidecarState.Missing,
            null,
            Scene,
            ArtworkState.Present,
            downloadArtwork: false);

        Assert.Equal(MovieNfo.For(Scene), plan.Sidecar);
    }

    [Theory]
    [InlineData(SidecarState.Foreign)]
    [InlineData(SidecarState.Unknown)]
    public void Somebody_elses_document_is_never_written_over(SidecarState state)
    {
        var plan = SceneRefresh.Decide(state, null, Corrected, ArtworkState.Present, false);

        Assert.Null(plan.Sidecar);
        Assert.NotNull(plan.SidecarNote);
    }

    /// <summary>
    /// Nothing is written on the strength of half an answer. The document that
    /// is there came from an answer prdb did give, which makes it better than
    /// anything this run could put in its place.
    /// </summary>
    [Fact]
    public void A_scene_prdb_no_longer_knows_keeps_what_it_has()
    {
        var plan = SceneRefresh.Decide(
            SidecarState.Ours,
            MovieNfo.For(Scene),
            null,
            ArtworkState.Missing,
            downloadArtwork: true);

        Assert.False(plan.Writes);
        Assert.Contains("no longer describes", plan.SidecarNote, StringComparison.Ordinal);
    }

    /// <summary>
    /// ADR 0027 unchanged, which is the point: the refresh was offered the
    /// amendment and declined it.
    /// </summary>
    [Fact]
    public void An_image_is_written_where_there_is_none_and_never_over_one()
    {
        var withImage = Scene with { ImageUrl = "https://cdn.example/scene.jpg" };

        Assert.True(SceneRefresh
            .Decide(SidecarState.Ours, MovieNfo.For(withImage), withImage, ArtworkState.Missing, true)
            .Artwork);

        Assert.False(SceneRefresh
            .Decide(SidecarState.Ours, MovieNfo.For(withImage), withImage, ArtworkState.Present, true)
            .Artwork);

        // Treated as present, because the alternative is writing into a
        // directory on the strength of not having been able to look at it.
        Assert.False(SceneRefresh
            .Decide(SidecarState.Ours, MovieNfo.For(withImage), withImage, ArtworkState.Unknown, true)
            .Artwork);
    }

    [Fact]
    public void No_image_is_written_while_the_switch_is_off()
    {
        var withImage = Scene with { ImageUrl = "https://cdn.example/scene.jpg" };

        var plan = SceneRefresh.Decide(
            SidecarState.Ours,
            MovieNfo.For(withImage),
            withImage,
            ArtworkState.Missing,
            downloadArtwork: false);

        Assert.False(plan.Artwork);
    }

    [Fact]
    public void A_scene_prdb_has_no_image_for_writes_no_image()
    {
        var plan = SceneRefresh.Decide(
            SidecarState.Ours,
            MovieNfo.For(Scene),
            Scene,
            ArtworkState.Missing,
            downloadArtwork: true);

        Assert.False(plan.Artwork);
    }

    /// <summary>
    /// The request is the scarce thing (ADR 0032), so a scene nothing could be
    /// written to never takes a place in a batch to prdb.
    /// </summary>
    [Fact]
    public void A_scene_nothing_can_be_written_to_is_not_worth_asking_about()
    {
        Assert.False(SceneRefresh.WorthAsking(SidecarState.Foreign, ArtworkState.Present, true));
        Assert.False(SceneRefresh.WorthAsking(SidecarState.Unknown, ArtworkState.Missing, false));

        Assert.True(SceneRefresh.WorthAsking(SidecarState.Ours, ArtworkState.Present, true));
        Assert.True(SceneRefresh.WorthAsking(SidecarState.Missing, ArtworkState.Present, false));

        // The one case where an untouchable sidecar is still worth a question:
        // the image next to it is missing and images are wanted.
        Assert.True(SceneRefresh.WorthAsking(SidecarState.Foreign, ArtworkState.Missing, true));
    }
}

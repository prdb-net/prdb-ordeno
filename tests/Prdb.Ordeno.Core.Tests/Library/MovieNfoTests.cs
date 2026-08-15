using System.Xml.Linq;

using Prdb.Ordeno.Core.Library;
using Prdb.Ordeno.Core.Review;

using Xunit;

namespace Prdb.Ordeno.Core.Tests.Library;

/// <summary>
/// The sidecar Jellyfin reads, in the shapes section 4 of the layout document
/// measured against a real server.
/// </summary>
/// <remarks>
/// Three of these are tests rather than comments for one reason: every one of
/// them fails silently. Jellyfin reports no error for a date it cannot parse, an
/// actor it drops or a document it cannot read — it simply shows the file name,
/// which looks exactly like a metadata lookup that returned nothing. A writer
/// that regresses here produces a library that looks broken rather than one that
/// fails.
/// </remarks>
public sealed class MovieNfoTests
{
    private static readonly Guid VideoId = Guid.Parse("0f3a1c2b-4d5e-6f70-8192-a3b4c5d6e7f8");

    private static SceneMetadata Metadata(
        string title = "Scene Title",
        DateOnly? releaseDate = null,
        string? studio = "Example Studio",
        params string[] performers) =>
        new(VideoId, title, releaseDate ?? new DateOnly(2025, 11, 7), studio, performers);

    private static XElement Movie(SceneMetadata metadata) => XDocument.Parse(MovieNfo.For(metadata)).Root!;

    [Fact]
    public void The_root_element_is_movie() => Assert.Equal("movie", Movie(Metadata()).Name.LocalName);

    /// <summary>
    /// One format, and no other. `07/11/2025`, `07.11.2025`, `07 November 2025`
    /// and `2025-11-07T00:00:00` were all discarded without a word, taking the
    /// production year with them — and the last of those is what most date
    /// libraries hand you by default.
    /// </summary>
    [Fact]
    public void A_release_date_is_written_bare_and_never_as_a_timestamp()
    {
        var premiered = Movie(Metadata(releaseDate: new DateOnly(2025, 11, 7))).Element("premiered");

        Assert.Equal("2025-11-07", premiered?.Value);
    }

    /// <summary>
    /// ADR 0019: where prdb knows no date the element is left out rather than
    /// filled. An empty one is a date Jellyfin cannot parse, and a guessed year
    /// is a wrong answer the server has no way to doubt.
    /// </summary>
    [Fact]
    public void A_scene_with_no_release_date_carries_no_premiered_element()
    {
        var movie = Movie(new SceneMetadata(VideoId, "Scene Title", null, "Example Studio", []));

        Assert.Null(movie.Element("premiered"));
        Assert.Null(movie.Element("year"));
    }

    /// <summary>
    /// A performer becomes a person only as an element with a name child. Text
    /// directly inside `actor` is dropped, which is the shape a naive writer
    /// produces, and the failure mode is a cast that is simply not there.
    /// </summary>
    [Fact]
    public void A_performer_is_an_actor_element_with_a_name_child()
    {
        var actor = Assert.Single(Movie(Metadata(performers: "Someone Real")).Elements("actor"));

        Assert.Equal("Someone Real", actor.Element("name")?.Value);
        Assert.Empty(actor.Nodes().OfType<XText>());
    }

    /// <summary>
    /// `Performer` is not a type Jellyfin knows, and an unknown one produces a
    /// person of kind `Unknown` rather than defaulting to an actor — a person
    /// who exists and is filed under nothing.
    /// </summary>
    [Fact]
    public void A_performer_is_written_as_the_one_type_jellyfin_knows() =>
        Assert.Equal(
            "Actor",
            Movie(Metadata(performers: "Someone Real")).Element("actor")?.Element("type")?.Value);

    [Fact]
    public void Performers_keep_the_order_prdb_gave_them()
    {
        var actors = Movie(Metadata(performers: ["First Name", "Second Name"])).Elements("actor").ToList();

        Assert.Equal(["First Name", "Second Name"], actors.Select(actor => actor.Element("name")?.Value));
        Assert.Equal(["0", "1"], actors.Select(actor => actor.Element("order")?.Value));
    }

    /// <summary>
    /// This was found by getting it wrong: an unescaped ampersand makes the whole
    /// document unparseable, and Jellyfin then uses none of it. A scene title
    /// containing one is not an edge case.
    /// </summary>
    [Theory]
    [InlineData("Rock & Roll")]
    [InlineData("Less < Than")]
    [InlineData("More > Than")]
    [InlineData("All Three & < >")]
    public void A_title_that_is_xml_is_escaped_rather_than_written(string title) =>
        Assert.Equal(title, Movie(Metadata(title)).Element("title")?.Value);

    /// <summary>
    /// The other half of the same failure: a control character no amount of
    /// escaping makes valid. It becomes a space, so that two words stay two
    /// words rather than becoming one.
    /// </summary>
    [Fact]
    public void A_title_carrying_a_control_character_still_parses() =>
        Assert.Equal("Two Words", Movie(Metadata("Two\u0001Words")).Element("title")?.Value);

    [Fact]
    public void The_scene_id_is_written_as_a_prdb_provider_id()
    {
        var id = Assert.Single(Movie(Metadata()).Elements("uniqueid"));

        Assert.Equal("prdb", id.Attribute("type")?.Value);
        Assert.Equal(VideoId.ToString("d"), id.Value);
    }

    [Fact]
    public void The_site_is_the_studio() =>
        Assert.Equal("Example Studio", Movie(Metadata()).Element("studio")?.Value);

    /// <summary>
    /// An empty studio is a browsable entry in the library with no name in it,
    /// which is worse than one fewer field.
    /// </summary>
    [Fact]
    public void A_scene_with_no_site_carries_no_studio_element() =>
        Assert.Null(Movie(Metadata(studio: null)).Element("studio"));

    /// <summary>
    /// The document says what it is written in. A `StringWriter` reports UTF-16
    /// and `XmlWriter` believes it, so this is the declaration being wrong by
    /// default rather than a formality.
    /// </summary>
    [Fact]
    public void The_document_declares_the_encoding_it_is_written_in() =>
        Assert.Equal("utf-8", XDocument.Parse(MovieNfo.For(Metadata())).Declaration?.Encoding);

    /// <summary>
    /// The whole of how the tool tells its own sidecar from somebody else's,
    /// which is what stands between a rewrite and destroying somebody's work.
    /// </summary>
    [Fact]
    public void A_sidecar_the_tool_wrote_says_so() => Assert.True(MovieNfo.IsOurs(MovieNfo.For(Metadata())));

    [Theory]
    [InlineData("<movie><title>Mine, thank you</title></movie>")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_else_is_somebody_elses(string? document) => Assert.False(MovieNfo.IsOurs(document));

    /// <summary>
    /// The marker is a comment, and stays one. An element would be a field the
    /// server tries to read; a comment is skipped by every XML parser there is.
    /// </summary>
    [Fact]
    public void The_marker_is_a_comment_rather_than_a_field()
    {
        var document = XDocument.Parse(MovieNfo.For(Metadata()));

        Assert.Contains(document.Nodes().OfType<XComment>(), comment =>
            comment.Value.Contains(MovieNfo.Marker, StringComparison.Ordinal));

        Assert.DoesNotContain(document.Root!.Elements(), element =>
            element.Value.Contains(MovieNfo.Marker, StringComparison.Ordinal));
    }

    /// <summary>
    /// prdb requires a title on the video document it answers with, so a video
    /// without one is a malformed answer rather than a scene shaped differently
    /// — and nothing is written from a partial answer.
    /// </summary>
    [Fact]
    public void A_video_with_no_title_is_not_something_to_write_a_sidecar_from() =>
        Assert.Null(SceneMetadata.From(new VideoSummary(VideoId, "  ", null, null, "Example Studio", [])));

    [Fact]
    public void What_prdb_answered_becomes_what_the_sidecar_says()
    {
        var metadata = SceneMetadata.From(new VideoSummary(
            VideoId,
            "Scene Title",
            new DateOnly(2025, 11, 7),
            Guid.NewGuid(),
            "Example Studio",
            ["Someone Real"]));

        Assert.NotNull(metadata);
        Assert.Equal("Scene Title", metadata.Title);
        Assert.Equal(new DateOnly(2025, 11, 7), metadata.ReleaseDate);
        Assert.Equal("Example Studio", metadata.Studio);
        Assert.Equal(["Someone Real"], metadata.Performers);
    }
}

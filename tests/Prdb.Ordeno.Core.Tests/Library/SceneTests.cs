using Prdb.Ordeno.Core.Identification;
using Prdb.Ordeno.Core.Library;

using Xunit;

namespace Prdb.Ordeno.Core.Tests.Library;

/// <summary>
/// Which answers from prdb become a filing at all. ADR 0019 draws the line at
/// "can the tool name it": everything else waits in the review queue, where a
/// person settles it, rather than going into the library under a name nobody
/// asked for.
/// </summary>
public sealed class SceneTests
{
    private static Recognition Answer(
        RecognitionState state = RecognitionState.Recognised,
        string? title = "Scene Title",
        string? site = "Example Studio",
        DateOnly? date = null) => new(
            state is RecognitionState.Ambiguous ? MatchConfidence.Ambiguous : MatchConfidence.Exact,
            state is RecognitionState.SiteOnly ? MatchRung.Site : MatchRung.OsHash,
            state is RecognitionState.Recognised ? Guid.NewGuid() : null,
            title,
            date,
            site,
            state is RecognitionState.Ambiguous ? 2 : 0,
            DateTimeOffset.UnixEpoch);

    [Fact]
    public void A_recognised_video_becomes_a_scene()
    {
        var answer = Answer(date: new DateOnly(2025, 11, 3));

        var scene = Scene.From(answer);

        Assert.NotNull(scene);
        Assert.Equal(answer.VideoId, scene.VideoId);
        Assert.Equal("Example Studio", scene.Site);
        Assert.Equal("Scene Title", scene.Title);
        Assert.Equal(new DateOnly(2025, 11, 3), scene.ReleaseDate);
    }

    /// <summary>
    /// The date is the one thing prdb leaves open — it is nullable in its schema
    /// where the site and the title are not — and it is the case ADR 0019 exists
    /// for. A scene without one is filed, under a shorter name.
    /// </summary>
    [Fact]
    public void A_recognised_video_with_no_date_still_becomes_one() =>
        Assert.NotNull(Scene.From(Answer()));

    /// <summary>
    /// The site rung is a result rather than a failure, and it is still not a
    /// filing: there is no title to name a directory after.
    /// </summary>
    [Theory]
    [InlineData(RecognitionState.SiteOnly)]
    [InlineData(RecognitionState.Ambiguous)]
    [InlineData(RecognitionState.Unrecognised)]
    public void Anything_short_of_a_named_video_is_not_filed(RecognitionState state) =>
        Assert.Null(Scene.From(Answer(state, title: null)));

    /// <summary>
    /// prdb requires both of these — Video.SiteId is not nullable, and site and
    /// title are required members of the video document this tool always asks
    /// for — so an answer missing one is malformed rather than shaped
    /// differently. It waits for a person instead of being given a layout of its
    /// own.
    /// </summary>
    [Theory]
    [InlineData(null, "Example Studio")]
    [InlineData("  ", "Example Studio")]
    [InlineData("Scene Title", null)]
    [InlineData("Scene Title", " ")]
    public void A_recognised_video_missing_a_title_or_a_site_is_not_filed(string? title, string? site) =>
        Assert.Null(Scene.From(Answer(title: title, site: site)));
}

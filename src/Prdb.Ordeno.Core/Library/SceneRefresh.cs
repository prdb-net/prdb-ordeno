namespace Prdb.Ordeno.Core.Library;

/// <summary>
/// What a refresh would do to one scene directory, worked out from what is in it
/// and what prdb says now — ADR 0033.
/// </summary>
/// <param name="Sidecar">
/// The document to put at <c>movie.nfo</c>, or <c>null</c> when nothing is
/// written there.
/// </param>
/// <param name="Artwork">
/// Whether an image is to be fetched into a directory that has none.
/// </param>
/// <param name="SidecarNote">
/// What to say about the sidecar, when there is something worth saying. Silent
/// in the ordinary case, which is a document that already says what prdb says.
/// </param>
public sealed record SceneRefreshPlan(
    string? Sidecar,
    bool Artwork,
    string? SidecarNote = null)
{
    public static readonly SceneRefreshPlan Nothing = new(null, false);

    public bool Writes => Sidecar is not null || Artwork;
}

/// <summary>
/// The refresh's decisions, apart from any filesystem — ADR 0012, and the reason
/// this is testable without one.
/// </summary>
/// <remarks>
/// <para>
/// Two questions, asked in this order and never the other way round. What is in
/// the directory decides whether prdb is worth asking about the scene at all
/// (<see cref="WorthAsking"/>); what prdb answered decides whether anything is
/// written (<see cref="Decide"/>). That order is ADR 0033's, and it is the
/// opposite of filing's on purpose: filing asks about everything because working
/// out which files move would mean reading the header of every video twice,
/// while here the look is a directory read the run has to make anyway and the
/// request is the scarce thing.
/// </para>
/// <para>
/// Nothing here removes anything, and there is no argument that would make it.
/// A refresh writes over its own document or into an empty name, and those are
/// the only two things it does to a filesystem.
/// </para>
/// </remarks>
public static class SceneRefresh
{
    /// <summary>
    /// Whether this scene is worth a place in a batch to prdb: is there anything
    /// the run could write here?
    /// </summary>
    /// <remarks>
    /// A scene whose sidecar is somebody else's and whose image is already there
    /// is a scene nothing can be done about, and asking about it would spend a
    /// fiftieth of a request to be told something that changes nothing.
    /// </remarks>
    /// <param name="sidecar">What is at the <c>movie.nfo</c>.</param>
    /// <param name="artwork">What is at the <c>fanart.jpg</c>.</param>
    /// <param name="downloadArtwork">Whether the artwork switch is on (ADR 0027).</param>
    public static bool WorthAsking(
        SidecarState sidecar,
        ArtworkState artwork,
        bool downloadArtwork) =>
        sidecar is SidecarState.Missing or SidecarState.Ours
        || (downloadArtwork && artwork is ArtworkState.Missing);

    /// <summary>
    /// What to write, given what is in the directory and what prdb answered.
    /// </summary>
    /// <param name="sidecar">What is at the <c>movie.nfo</c>.</param>
    /// <param name="document">
    /// What is in it, when it is the tool's own and could be read. The comparison
    /// this is for is the whole trigger of a refresh (ADR 0033):
    /// <see cref="MovieNfo"/> is deterministic, so a document identical to the
    /// one prdb's answer produces now is a document there is no reason to write.
    /// </param>
    /// <param name="metadata">
    /// What prdb says about the scene, or <c>null</c> when it no longer knows it
    /// or answered without a title. Nothing is written on the strength of half an
    /// answer.
    /// </param>
    /// <param name="artwork">What is at the <c>fanart.jpg</c>.</param>
    /// <param name="downloadArtwork">Whether the artwork switch is on.</param>
    public static SceneRefreshPlan Decide(
        SidecarState sidecar,
        string? document,
        SceneMetadata? metadata,
        ArtworkState artwork,
        bool downloadArtwork)
    {
        if (metadata is null)
        {
            // prdb has forgotten the video, or answered without a title. The
            // sidecar that is there was written from an answer prdb did give,
            // which makes it better than anything this run could put in its
            // place.
            return new SceneRefreshPlan(
                null,
                false,
                sidecar is SidecarState.Missing
                    ? null
                    : $"prdb no longer describes this scene, so '{ScenePath.SidecarFileName}' was "
                        + "left exactly as it is.");
        }

        var wanted = MovieNfo.For(metadata);

        var write = sidecar switch
        {
            // Nothing there. ADR 0024 left a scene whose sidecar has gone missing
            // to the next filing; this is what covers it instead, and writing
            // into an empty name destroys nothing.
            SidecarState.Missing => wanted,

            // The tool's own, and it no longer says what prdb says. A document
            // that is byte for byte what would be written is left alone — not
            // rewritten to the same bytes, which would be a write on every scene
            // in the library every night and a media server told about all of
            // them.
            SidecarState.Ours when document != wanted => wanted,

            _ => null,
        };

        var note = sidecar switch
        {
            SidecarState.Foreign =>
                $"'{ScenePath.SidecarFileName}' in that directory was not written by this tool. It "
                + "is left exactly as it is; deleting the marker comment is how a file is handed "
                + "back, and deleting the file is how a fresh one is asked for.",
            SidecarState.Unknown =>
                $"'{ScenePath.SidecarFileName}' in that directory could not be read, so it was left "
                + "exactly as it is.",
            _ => null,
        };

        return new SceneRefreshPlan(
            write,

            // ADR 0027 unchanged, and this is not an amendment to it: an image
            // goes where there is none and nowhere else, whether a filing or a
            // refresh is what put it there.
            downloadArtwork && artwork is ArtworkState.Missing && metadata.ImageUrl is not null,
            note);
    }
}

namespace Prdb.Ordeno.Core.MediaServer;

/// <summary>How an exchange with the media server ended.</summary>
public enum MediaServerReach
{
    /// <summary>It answered, and the answer is in hand.</summary>
    Answered,

    /// <summary>It answered, and would not let this key in.</summary>
    Refused,

    /// <summary>
    /// Nothing answered, or what answered was not a media server. The distinction
    /// from <see cref="Refused"/> is the same one the prdb key check makes: only
    /// a refusal is the user's to fix by typing something else.
    /// </summary>
    Unreachable,
}

/// <param name="Problem">What to tell the user. <c>null</c> when there is nothing to say.</param>
public sealed record MediaServerReply(MediaServerReach Reach, string? Problem = null)
{
    public bool Answered => Reach is MediaServerReach.Answered;
}

/// <summary>An answer that carries something, when it is an answer at all.</summary>
public sealed record MediaServerReply<T>(MediaServerReach Reach, T? Value = null, string? Problem = null)
    where T : class
{
    public bool Answered => Reach is MediaServerReach.Answered && Value is not null;

    public static MediaServerReply<T> Of(T value) => new(MediaServerReach.Answered, value);

    public static MediaServerReply<T> Refused(string problem) =>
        new(MediaServerReach.Refused, null, problem);

    public static MediaServerReply<T> Unreachable(string problem) =>
        new(MediaServerReach.Unreachable, null, problem);
}

/// <summary>
/// One thing the server holds, and the path it holds it at.
/// </summary>
/// <remarks>
/// The path is the video file rather than the directory it sits in — section 9
/// of the layout document — and it is the path as the <em>server</em> sees it,
/// which is not the path this tool wrote to.
/// </remarks>
public sealed record MediaServerItem(string Id, string Path);

/// <summary>
/// What the server says about itself, and the one setting of its own that
/// decides whether the sidecars this tool writes are read or discarded.
/// </summary>
/// <param name="ReleaseDateFormat">
/// What it parses a release date against. <c>null</c> when it could not be read
/// — the tool then says it did not check rather than claiming either answer.
/// </param>
public sealed record MediaServerFacts(string Name, string Version, string? ReleaseDateFormat);

/// <summary>
/// The half of a media server implementation that talks to the server, which
/// ADR 0018 makes optional: a sidecar writer exists for every layout, a client
/// only where somebody has measured one.
/// </summary>
/// <remarks>
/// Nothing here throws for a server that is down, moved or answering with a
/// stale key. Every call answers with a <see cref="MediaServerReach"/>, because
/// every caller is on a path that has to carry on regardless — the filing path
/// most of all.
/// </remarks>
public interface IMediaServerClient
{
    /// <summary>
    /// What the server is, and how it will read the dates the tool writes. This
    /// is the call that proves a URL and a key belong together.
    /// </summary>
    Task<MediaServerReply<MediaServerFacts>> ExamineAsync(
        MediaServerConnection connection,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Everything in the library, with the path each item is held at.
    /// </summary>
    /// <remarks>
    /// The whole library, because the server cannot be asked about one path:
    /// <c>GET /Items</c> accepts a <c>path=</c> parameter, ignores it and answers
    /// with everything, so a caller that trusts it refreshes an item at random.
    /// Measured in section 9, and priced there too — 58 movies in 43 KB, so ten
    /// thousand is a few megabytes, once per batch rather than once per item.
    /// </remarks>
    Task<MediaServerReply<IReadOnlyList<MediaServerItem>>> ItemsAsync(
        MediaServerConnection connection,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads one item again, sidecar and all, so that a rewrite appears without
    /// waiting for a scan.
    /// </summary>
    /// <remarks>
    /// By item id and not by reporting the changed path: a path report obeys the
    /// same one-minute tolerance a scan does, which is the one case a targeted
    /// refresh exists for, and a path the server does not recognise is answered
    /// with a 204 exactly like one it does.
    /// </remarks>
    Task<MediaServerReply> RefreshAsync(
        MediaServerConnection connection,
        string itemId,
        CancellationToken cancellationToken = default);
}

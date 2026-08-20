namespace Prdb.Ordeno.Core.Library;

/// <summary>What is at the path a sidecar goes.</summary>
public enum SidecarState
{
    /// <summary>Nothing. The ordinary case, and the one that is simply written.</summary>
    Missing,

    /// <summary>
    /// A sidecar this tool wrote, recognised by <see cref="MovieNfo.Marker"/>. It
    /// is the tool's own output and may be replaced by it.
    /// </summary>
    Ours,

    /// <summary>
    /// A sidecar somebody else wrote. It is left exactly where it is: overwriting
    /// it is destroying work nobody asked the tool to touch, and a hand-written
    /// one is most likely to be at this very name rather than one that can be
    /// stepped around.
    /// </summary>
    Foreign,

    /// <summary>
    /// It could not be read — a share that went away, a file this user may not
    /// open. Treated as somebody else's, because the alternative is writing over
    /// a file on the strength of not having been able to look at it.
    /// </summary>
    Unknown,
}

/// <summary>
/// What is at the path, and what it says when the tool may read it as its own.
/// </summary>
/// <param name="Document">
/// The whole document, when it is the tool's own and could be read;
/// <c>null</c> otherwise. A refresh compares it with what
/// <see cref="MovieNfo.For"/> produces now, which is the whole of what makes a
/// refresh write anything (ADR 0033) — and the reason it is the document rather
/// than the head of it.
/// </param>
public sealed record SidecarLook(SidecarState State, string? Document = null);

/// <summary>
/// Looks at the sidecar in one scene directory, and answers whose it is.
/// </summary>
/// <remarks>
/// The question the planner asks before deciding anything, which is why it is an
/// interface: <c>Core</c> touches no filesystem (ADR 0012), and what is at a path
/// is the one thing filing cannot work out for itself.
/// </remarks>
public interface ISidecars
{
    SidecarState StateOf(string absolutePath);

    /// <summary>
    /// The same question, with the document when there is one to read. Filing
    /// asks <see cref="StateOf"/> because whose the file is, is all it needs; a
    /// refresh asks this, because "is what is there already right" cannot be
    /// answered from the first eight kilobytes.
    /// </summary>
    SidecarLook Look(string absolutePath);
}

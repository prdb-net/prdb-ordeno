namespace Prdb.Ordeno.Core.Library;

/// <summary>What is at the path an image goes.</summary>
/// <remarks>
/// Two of these three mean the same thing to the planner, and that is the point
/// of <see href="https://github.com/prdb-net/prdb-ordeno/blob/main/docs/adr/0027-artwork-is-one-image-written-only-where-there-is-none.md">ADR 0027</see>:
/// nothing is ever written over, so whose the file is never has to be decided.
/// They are still told apart, because "there is already an image there" and "the
/// tool could not look" are different sentences to put in front of somebody.
/// </remarks>
public enum ArtworkState
{
    /// <summary>Nothing. The only state an image is written in.</summary>
    Missing,

    /// <summary>
    /// A file is at that name. It stays exactly as it is, whether the tool
    /// downloaded it last month or the user put it there this morning —
    /// re-downloading is waste in the first case and destruction in the second,
    /// and deleting the file is how a fresh one is asked for.
    /// </summary>
    Present,

    /// <summary>
    /// It could not be looked at — a share that went away, a file this user may
    /// not open. Treated as present, because the alternative is writing into a
    /// directory on the strength of not having been able to see it.
    /// </summary>
    Unknown,
}

/// <summary>
/// Looks at the image in one scene directory, and answers whether there is one.
/// </summary>
/// <remarks>
/// The question the planner asks before promising anything, which is why it is an
/// interface: <c>Core</c> touches no filesystem (ADR 0012), and what is at a path
/// is the one thing filing cannot work out for itself.
/// </remarks>
public interface ISceneArtwork
{
    ArtworkState StateOf(string absolutePath);
}

namespace Prdb.Ordeno.Core.Library;

/// <summary>
/// One video the tool has put in the library, as it needs to remember it.
/// </summary>
/// <remarks>
/// <para>
/// This exists because a filesystem cannot say whose a directory is. Filing has
/// to tell a second quality of a scene it filed last year — which goes
/// <em>into</em> that directory — from two different scenes the layout gives one
/// name, which must not. Nothing on disk carries that answer: the names are
/// lossy by the time they are legal, and the sidecar that would carry an id is
/// #18 and would still be missing from every directory somebody else made.
/// </para>
/// <para>
/// It is deliberately the smallest record that answers it, and it is not the
/// operation log
/// (<see href="https://github.com/prdb-net/prdb-ordeno/issues/19">#19</see>).
/// The log records what happened so it can be undone; this records what is
/// currently true of the library so the next filing can be decided. The log will
/// be written from the plan, and will grow next to this rather than out of it.
/// </para>
/// </remarks>
/// <param name="VideoId">
/// prdb's id for the scene. Two copies of one scene share it, which is what
/// makes them findable as a pair.
/// </param>
/// <param name="Directory">The absolute scene directory the copy sits in.</param>
/// <param name="FileName">
/// The name it was filed under — plain while it is the only copy, bracketed once
/// it is one of several (ADR 0020).
/// </param>
/// <param name="QualityLabel">
/// What was read out of the file when it was filed. It is what "the library
/// already holds this quality" is decided by, and what the file is renamed to
/// carry when a second quality arrives.
/// </param>
public sealed record FiledCopy(Guid VideoId, string Directory, string FileName, string QualityLabel)
{
    public string Path => System.IO.Path.Combine(Directory, FileName);

    /// <summary>The names this copy sits under, read back off the directory it is in.</summary>
    public ScenePath Names => ScenePath.At(Directory, System.IO.Path.GetExtension(FileName));

    /// <summary>
    /// Whether the name carries a quality label yet. The first copy of a scene
    /// is filed without one and gets one when a second quality turns up
    /// (ADR 0020), so this is what says whether there is a rename to do.
    /// </summary>
    public bool IsLabelled => !string.Equals(FileName, Names.VideoFileName, StringComparison.Ordinal);
}

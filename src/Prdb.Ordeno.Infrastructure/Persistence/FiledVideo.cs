namespace Prdb.Ordeno.Infrastructure.Persistence;

/// <summary>
/// One video this tool has put in the library, and where it put it.
/// </summary>
/// <remarks>
/// <para>
/// It exists because a filesystem cannot say whose a directory is. Filing has to
/// tell a second quality of a scene it filed last year — which goes
/// <em>into</em> that directory (ADR 0003, ADR 0020) — from two different scenes
/// the layout gives one name, which must not. Nothing on disk carries that
/// answer.
/// </para>
/// <para>
/// It is not the operation log
/// (<see href="https://github.com/prdb-net/prdb-ordeno/issues/19">#19</see>).
/// The log records what happened so that it can be undone; this records what is
/// currently true of the library so the next filing can be decided, and it holds
/// nothing filing does not read. A row that no longer describes a file on disk
/// is deleted rather than kept as history — which is exactly what a log would
/// have to keep, and why it is a table of its own rather than a column here.
/// </para>
/// </remarks>
public sealed class FiledVideo
{
    public int Id { get; set; }

    /// <summary>
    /// prdb's id for the scene. Several rows share it once a scene is held at
    /// more than one quality, which is what makes them findable as a set.
    /// </summary>
    public Guid VideoId { get; set; }

    /// <summary>
    /// The library this was filed into. Kept because the user can point the tool
    /// at a different one: what is filed under the old root says nothing about
    /// what the new one holds, and a scene is filed afresh there rather than
    /// treated as already present somewhere the tool is no longer looking.
    /// </summary>
    public required string LibraryRoot { get; set; }

    /// <summary>The absolute scene directory, as it was named when it was filed.</summary>
    public required string Directory { get; set; }

    /// <summary>
    /// The name it carries. Plain while it is the only copy of its scene,
    /// bracketed from the moment a second quality joins it — ADR 0020, which is
    /// also what rewrites this row.
    /// </summary>
    public required string FileName { get; set; }

    /// <summary>
    /// What was read out of the file when it was filed. It decides whether the
    /// next copy of this scene is a second quality or a second copy, and it is
    /// the label the file is renamed to carry when the first of those happens.
    /// </summary>
    public required string QualityLabel { get; set; }

    public DateTimeOffset FiledAt { get; set; }
}

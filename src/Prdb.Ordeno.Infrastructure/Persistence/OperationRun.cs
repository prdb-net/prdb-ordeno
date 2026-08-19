using Prdb.Ordeno.Core.History;

namespace Prdb.Ordeno.Infrastructure.Persistence;

/// <summary>
/// One run of the tool over somebody's files — ADR 0028.
/// </summary>
/// <remarks>
/// <para>
/// The row is opened when a run starts and closed when it stops, so a container
/// killed halfway leaves a run with no end and the entries it managed to write.
/// That is the honest record of what happened, and the screen says as much.
/// </para>
/// <para>
/// A run that moved nothing keeps its row. It costs one row, and it is the
/// answer to the question somebody who was asleep asks first.
/// </para>
/// </remarks>
public sealed class OperationRun
{
    public int Id { get; set; }

    public RunKind Kind { get; set; }

    /// <summary>
    /// Whether a person asked for this run or the timer did — ADR 0031, in the
    /// column ADR 0028 left for it. It also decides whether an empty run keeps
    /// its row: the row above is owed to whoever asked, and nobody asked for a
    /// tick of the clock.
    /// </summary>
    public AskedBy AskedBy { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    /// <summary>When it stopped, or <c>null</c> for a run that never did.</summary>
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>
    /// What it did, in the one line the screen built while it was happening. It
    /// is stored rather than recomputed because it is what the user read at the
    /// time, and because the counts it is built from include files that left no
    /// entry — the ones nothing happened to.
    /// </summary>
    public string? Account { get; set; }

    /// <summary>What stopped it, when something did.</summary>
    public string? Problem { get; set; }
}

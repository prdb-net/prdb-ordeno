using Prdb.Ordeno.Core.History;
using Prdb.Ordeno.Core.Library;

namespace Prdb.Ordeno.Host.Library;

/// <summary>
/// One scene a refresh had something to say about.
/// </summary>
/// <remarks>
/// Only the scenes that changed and the ones that could not be dealt with are
/// here. A document that already says what prdb says produces no row at all,
/// which is what the steady state of this feature looks like.
/// </remarks>
/// <param name="Scene">The scene directory's own name, which is what somebody recognises.</param>
/// <param name="Title">What prdb calls the scene now, when it still knows it.</param>
public sealed record RefreshedSceneState(
    string Scene,
    string Directory,
    string? Title,
    bool Sidecar,
    bool Artwork,
    string? SidecarMessage,
    string? ArtworkMessage,
    string? Problem);

/// <summary>
/// What the metadata refresh has to show for itself — ADR 0032.
/// </summary>
/// <param name="Running">
/// Whether a run is under way. There is only one kind of run here: no preview,
/// because nothing this does moves a file and working out what it would write
/// costs exactly what doing it costs.
/// </param>
/// <param name="Unattended">Whether the tool checks the library on its own.</param>
/// <param name="AskedByTimer">
/// Whether the last run is one nobody started. A screen showing a run that
/// appeared out of nowhere has to say where it came from.
/// </param>
/// <param name="Scenes">How many scenes of this library the tool filed.</param>
/// <param name="NeverChecked">How many of them nothing has looked at yet.</param>
/// <param name="OldestCheckedAt">
/// When the least recently checked scene was last looked at, or <c>null</c>
/// while any of them has never been.
/// </param>
public sealed record RefreshState(
    bool Running,
    bool Unattended,
    bool AskedByTimer,
    int IntervalHours,
    int Slice,
    int Scenes,
    int NeverChecked,
    DateTimeOffset? OldestCheckedAt,
    DateTimeOffset? FinishedAt,
    string? Problem,
    int Checked,
    int Sidecars,
    int Images,
    int Waiting,
    string? WhatItDid,
    IReadOnlyList<RefreshedSceneState> Changed,
    int ChangedTotal)
{
    /// <summary>
    /// How many rows go to the browser. The same cap the filing screen uses and
    /// for the same reason: a first pass over a library that has just had artwork
    /// switched on writes hundreds, and the sentence carries the scale.
    /// </summary>
    public const int Limit = 200;

    /// <param name="problem">
    /// Said instead of what the last run reported, when a request has just been
    /// refused. Filing, undo and this share one gate.
    /// </param>
    public static RefreshState Of(
        RefreshRun run,
        bool unattended,
        RefreshStanding standing,
        string? problem = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(standing);

        var report = run.Report;

        return new RefreshState(
            Running: run.Running,
            Unattended: unattended,
            AskedByTimer: run.AskedBy is AskedBy.Timer,
            IntervalHours: (int)RefreshSchedule.Interval.TotalHours,
            Slice: RefreshSchedule.Slice,
            Scenes: standing.Scenes,
            NeverChecked: standing.NeverChecked,
            OldestCheckedAt: standing.Oldest,
            FinishedAt: run.FinishedAt,
            Problem: problem ?? run.Problem,
            Checked: report?.Checked ?? 0,
            Sidecars: report?.Sidecars ?? 0,
            Images: report?.Images ?? 0,
            Waiting: report?.Waiting ?? 0,
            WhatItDid: run.WhatItDid,
            Changed: [.. (report?.Notes ?? []).Take(Limit).Select(Row)],
            ChangedTotal: report?.Notes.Count ?? 0);
    }

    private static RefreshedSceneState Row(RefreshResult result) => new(
        result.Scene,
        result.Directory,
        result.Title,
        result.WroteSidecar,
        result.WroteArtwork,
        result.Sidecar,
        result.Artwork,
        result.Problem);
}

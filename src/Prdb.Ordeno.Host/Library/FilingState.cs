using Prdb.Ordeno.Core.Configuration;
using Prdb.Ordeno.Core.History;
using Prdb.Ordeno.Core.Library;

namespace Prdb.Ordeno.Host.Library;

/// <summary>
/// What would happen to one video, as the screen shows it.
/// </summary>
/// <param name="Outcome">
/// <c>filed</c>, <c>collisionBroken</c>, <c>secondQuality</c>,
/// <c>alreadyFiled</c>, <c>held</c> or <c>blocked</c>. The last three move
/// nothing and are three different things: a copy of something the library
/// already has, a file somebody put back and has not released (ADR 0030), and a
/// reason the tool could not act.
/// </param>
/// <param name="Movement">
/// <c>rename</c>, <c>copyThenDelete</c> or <c>unknown</c> — whether this filing
/// is instant or as slow as the file is large.
/// </param>
/// <param name="RelabelTo">
/// What a file already in the library would be renamed to first, or <c>null</c>
/// when nothing is renamed. It is the one part of a plan that touches something
/// the user already considers filed, so it is on the row rather than in a
/// sentence.
/// </param>
/// <param name="Sidecar">
/// What would happen to the <c>movie.nfo</c> in that directory, in words, or
/// <c>null</c> when nothing would. It is the second thing a filing writes, and
/// the one that can land next to a file somebody wrote themselves.
/// </param>
/// <param name="Artwork">
/// What would happen to the <c>fanart.jpg</c>, in words, or <c>null</c> when
/// nothing would — which includes every installation that left artwork off, so
/// this row is silent for most of them.
/// </param>
public sealed record PlannedFileState(
    int FileId,
    string Name,
    string Path,
    string Outcome,
    string? Scene,
    string? Quality,
    string? Directory,
    string? TargetName,
    string? RelabelFrom,
    string? RelabelTo,
    string Movement,
    bool Moves,
    string? Message,
    string? Sidecar,
    string? Artwork);

/// <summary>
/// What happened to one video.
/// </summary>
/// <param name="State"><c>filed</c>, <c>skipped</c>, <c>failed</c> or <c>stopped</c>.</param>
/// <param name="Sidecar">
/// What became of the metadata file next to it, when that is worth saying: it was
/// left alone, or it could not be written. <c>null</c> when it was written, since
/// the video moving is what the row is about.
/// </param>
/// <param name="Artwork">
/// The same for the image, and silent in one more case: a scene prdb has no
/// image for is the ordinary outcome, not a problem to report.
/// </param>
public sealed record FiledFileState(
    int FileId,
    string Name,
    string State,
    string? Scene,
    string? TargetName,
    string? Message,
    string? Sidecar,
    string? Artwork);

/// <summary>
/// Everything the filing part of the downloads screen shows: what would happen,
/// and what happened when it was asked to.
/// </summary>
/// <remarks>
/// Two lists rather than one, and both are kept: somebody who has just filed a
/// batch and wants to know what is left needs the answer to both questions at
/// once. Each is capped — <see cref="PlanTotal"/> and
/// <see cref="ResultTotal"/> carry the scale.
/// </remarks>
/// <param name="Filing">
/// Whether the run under way is moving files or only working out what it would
/// move. It is the difference between a screen that is thinking and one that is
/// touching somebody's library.
/// </param>
/// <param name="Unattended">
/// Whether the tool files on its own (ADR 0031). The screen says which it is,
/// because "prdb-ordeno files nothing on its own" was a promise and is now a
/// setting — and a screen that hedges about that is worse than one that was
/// wrong about it.
/// </param>
/// <param name="Held">
/// How many of the planned files are waiting on a hold an undo left (ADR 0030).
/// It is counted here rather than on the browser side because the button that
/// releases all of them is shown from it.
/// </param>
/// <param name="AskedByTimer">
/// Whether the last run that filed anything is one nobody started. A screen
/// showing a run that appeared out of nowhere has to say where it came from.
/// </param>
public sealed record FilingState(
    bool Running,
    bool Filing,
    bool Unattended,
    bool AskedByTimer,
    DateTimeOffset? PlannedAt,
    DateTimeOffset? FiledAt,
    string? Problem,
    IReadOnlyList<PlannedFileState> Plan,
    int PlanTotal,
    int WouldFile,
    int Held,
    string? WhatItWouldDo,
    IReadOnlyList<FiledFileState> Results,
    int ResultTotal,
    string? WhatItDid)
{
    /// <summary>
    /// How many rows of each list go to the browser. A first pass over an
    /// existing library is thousands of them, and a screen full helps nobody —
    /// the sentences carry the scale, the lists carry the examples.
    /// </summary>
    public const int Limit = 200;

    /// <summary>
    /// What is going on, and — when a request has just been refused — why
    /// nothing started.
    /// </summary>
    /// <param name="problem">
    /// Said instead of what the last run reported. Filing and the way back share
    /// one gate, so a button pressed while the other one is working is a state
    /// the screen would otherwise have to infer from nothing happening.
    /// </param>
    public static FilingState Of(FilingRun run, bool unattended, string? problem = null)
    {
        ArgumentNullException.ThrowIfNull(run);

        return new FilingState(
            Running: run.Running,
            Filing: run.Filing,
            Unattended: unattended,
            AskedByTimer: run.AskedBy is AskedBy.Timer,
            PlannedAt: run.PlannedAt,
            FiledAt: run.FiledAt,
            Problem: problem ?? run.Problem,
            Plan: [.. run.Plan.Take(Limit).Select(Planned)],
            PlanTotal: run.Plan.Count,
            WouldFile: run.WouldFile,
            Held: run.Plan.Count(plan => plan.Outcome is FilingOutcome.Held),
            WhatItWouldDo: run.WhatItWouldDo,
            Results: [.. run.Results.Take(Limit).Select(Filed)],
            ResultTotal: run.Results.Count,
            WhatItDid: run.WhatItDid);
    }

    private static PlannedFileState Planned(FilingPlan plan) => new(
        plan.FileId,
        plan.SourceName,
        plan.SourcePath,
        Name(plan.Outcome),
        plan.Scene?.InWords,
        plan.QualityLabel,
        plan.Directory,
        plan.TargetName,
        plan.Relabel is null ? null : System.IO.Path.GetFileName(plan.Relabel.From),
        plan.Relabel is null ? null : System.IO.Path.GetFileName(plan.Relabel.To),
        Name(plan.Movement),
        plan.Moves,
        plan.Message,
        plan.Sidecar.InWords,
        plan.Artwork.InWords);

    private static FiledFileState Filed(FilingResult result) => new(
        result.Plan.FileId,
        result.Plan.SourceName,
        Name(result.State),
        result.Plan.Scene?.InWords,
        result.Plan.TargetName,
        result.Message,
        result.Sidecar,
        result.Artwork);

    /// <summary>
    /// As a name rather than a number, the way every other state crosses this
    /// boundary. A number in a generated type is a number the browser has to
    /// hold a second copy of the meaning of.
    /// </summary>
    private static string Name(FilingOutcome outcome) => outcome switch
    {
        FilingOutcome.Filed => "filed",
        FilingOutcome.CollisionBroken => "collisionBroken",
        FilingOutcome.SecondQuality => "secondQuality",
        FilingOutcome.AlreadyFiled => "alreadyFiled",
        FilingOutcome.Held => "held",
        _ => "blocked",
    };

    private static string Name(FilingResultState state) => state switch
    {
        FilingResultState.Filed => "filed",
        FilingResultState.Skipped => "skipped",
        FilingResultState.Failed => "failed",
        _ => "stopped",
    };

    private static string Name(FileMovement movement) => movement switch
    {
        FileMovement.Rename => "rename",
        FileMovement.CopyThenDelete => "copyThenDelete",
        _ => "unknown",
    };
}

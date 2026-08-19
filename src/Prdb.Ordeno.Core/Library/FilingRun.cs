using System.Globalization;

namespace Prdb.Ordeno.Core.Library;

/// <summary>
/// What filing would do, and why it might not be able to do any of it.
/// </summary>
/// <param name="Problem">
/// Something that stops the whole run rather than one file — the setup is not
/// finished, the library cannot be written to. <c>null</c> when the plan below
/// is the answer.
/// </param>
public sealed record FilingPreview(IReadOnlyList<FilingPlan> Plans, string? Problem = null)
{
    public static readonly FilingPreview Nothing = new([]);
}

/// <summary>What a run did, file by file.</summary>
public sealed record FilingReport(IReadOnlyList<FilingResult> Results, string? Problem = null)
{
    public static readonly FilingReport Nothing = new([]);

    /// <summary>
    /// The one line the operation log keeps (ADR 0028). It is the sentence the
    /// screen shows, from the same code, because a log that describes a run
    /// differently from the screen that watched it is two accounts of one night.
    /// </summary>
    public string Account => FilingRun.WhatARunDid(Results);
}

/// <summary>
/// The plan the user is shown, and what came of it when they said yes.
/// </summary>
/// <remarks>
/// <para>
/// Held in memory and forgotten on a restart, like the scan's and the
/// identification's: what survives a restart is in the database, and a container
/// that has just come up has filed nothing.
/// </para>
/// <para>
/// The plan is not what gets carried out. ADR 0022 has the run computing it
/// again at the moment it acts, because a directory can be occupied in the
/// seconds between reading a screen and pressing a button — so what is kept here
/// is what was shown, and <see cref="Results"/> is what happened.
/// </para>
/// </remarks>
/// <param name="Filing">
/// Whether the run under way is moving files or only working out what it would
/// move. The difference is the whole of ADR 0022 and it belongs on the screen.
/// </param>
public sealed record FilingRun(
    bool Running,
    bool Filing,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? Problem,
    IReadOnlyList<FilingPlan> Plan,
    DateTimeOffset? PlannedAt,
    IReadOnlyList<FilingResult> Results,
    DateTimeOffset? FiledAt)
{
    /// <summary>Nothing has been planned or filed since the tool started.</summary>
    public static readonly FilingRun Never = new(
        Running: false,
        Filing: false,
        null,
        null,
        null,
        [],
        null,
        [],
        null);

    public FilingRun Started(DateTimeOffset at, bool filing) =>
        this with { Running = true, Filing = filing, StartedAt = at, FinishedAt = null, Problem = null };

    /// <summary>
    /// A planning run that finished. The results of the last filing run are kept
    /// next to it: a user who has just filed something and asks what is left
    /// wants both answers on the screen at once.
    /// </summary>
    public FilingRun Planned(DateTimeOffset at, IReadOnlyList<FilingPlan> plan, string? problem = null) =>
        this with
        {
            Running = false,
            Filing = false,
            FinishedAt = at,
            Problem = problem,
            Plan = plan,
            PlannedAt = at,
        };

    /// <summary>
    /// A filing run that finished. The plan is replaced by what the run itself
    /// worked out, so the screen never shows a preview of something that has
    /// already happened.
    /// </summary>
    public FilingRun Filed(DateTimeOffset at, IReadOnlyList<FilingResult> results, string? problem = null) =>
        this with
        {
            Running = false,
            Filing = false,
            FinishedAt = at,
            Problem = problem,
            Plan = [.. results.Where(result => !result.Filed).Select(result => result.Plan)],
            PlannedAt = at,
            Results = results,
            FiledAt = at,
        };

    public int WouldFile => Plan.Count(plan => plan.Moves);

    public int WasFiled => Results.Count(result => result.Filed);

    /// <summary>
    /// What the plan says, in one line. It is the sentence somebody reads before
    /// pressing a button that moves their files, so it counts what would happen
    /// rather than what was found.
    /// </summary>
    public string? WhatItWouldDo
    {
        get
        {
            if (PlannedAt is null)
            {
                return null;
            }

            if (Plan.Count == 0)
            {
                return "Nothing is waiting to be filed. Videos appear here once they have finished "
                    + "downloading and prdb has said what they are.";
            }

            var moving = Plan.Count(plan => plan.Moves);
            var second = Plan.Count(plan => plan.Outcome is FilingOutcome.SecondQuality);
            var held = Plan.Count(plan => plan.Outcome is FilingOutcome.AlreadyFiled);
            var blocked = Plan.Count(plan => plan.Outcome is FilingOutcome.Blocked);

            var parts = new List<string>();

            if (moving > 0)
            {
                parts.Add(second == 0
                    ? $"{Count(moving, "video")} would be filed"
                    : $"{Count(moving, "video")} would be filed, {Number(second)} of them next to a "
                        + "copy the library already holds");
            }

            if (held > 0)
            {
                parts.Add($"{Number(held)} would be left alone as a copy of something already filed");
            }

            if (blocked > 0)
            {
                parts.Add($"{Number(blocked)} cannot be filed and say why");
            }

            return Join(parts) + ". Nothing has been moved yet.";
        }
    }

    /// <summary>What the last run did, in one line. <c>null</c> until one has happened.</summary>
    public string? WhatItDid => FiledAt is null ? null : WhatARunDid(Results);

    /// <summary>
    /// What a run did, in one line, from its results alone. Static because the
    /// operation log needs the same sentence about a run that is over, and a
    /// second implementation of it would drift.
    /// </summary>
    public static string WhatARunDid(IReadOnlyList<FilingResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var filed = results.Count(result => result.Filed);
        var failed = results.Count(result => result.State is FilingResultState.Failed);
        var stopped = results.Count(result => result.State is FilingResultState.Stopped);

        // Filed, and the sidecar that was to go in next to it did not: prdb
        // could not be asked, or the file could not be written. The rows say
        // which, but a run of thousands shows two hundred of them, and prdb
        // being down is a thing about the run rather than about one file.
        var bare = results.Count(result =>
            result.Filed && result.Plan.Sidecar.Writes && result.Sidecar is not null);

        // And the same for the image, which is worth a line here for the
        // same reason: one CDN having a bad afternoon is a fact about the
        // run, and reading it off two hundred rows is nobody's evening. A
        // scene prdb has no image for says nothing and is not counted —
        // ADR 0027 is plain that this is the ordinary case.
        var unillustrated = results.Count(result =>
            result.Filed && result.Plan.Artwork.Writes && result.Artwork is not null);

        var parts = new List<string>
        {
            filed == 1 ? "1 video was filed" : $"{Number(filed)} videos were filed",
        };

        if (bare > 0)
        {
            parts.Add(bare == filed
                ? "none of them could be given the metadata file the media server reads, and the "
                    + "rows say why"
                : $"{Number(bare)} of them could not be given the metadata file the media server "
                    + "reads");
        }

        if (unillustrated > 0)
        {
            parts.Add(unillustrated == 1
                ? "1 did not get the image that was to go next to it"
                : $"{Number(unillustrated)} did not get the image that was to go next to them");
        }

        if (failed > 0)
        {
            parts.Add($"{Number(failed)} could not be moved and were left exactly as they were");
        }

        if (stopped > 0)
        {
            parts.Add($"{Number(stopped)} were not reached before the tool was asked to stop");
        }

        return Join(parts) + ".";
    }

    private static string Join(IReadOnlyList<string> parts) => parts.Count switch
    {
        0 => "Nothing would happen",
        1 => parts[0],
        _ => string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1],
    };

    private static string Count(int number, string singular) =>
        number == 1 ? $"{Number(number)} {singular}" : $"{Number(number)} {singular}s";

    private static string Number(int number) => number.ToString("N0", CultureInfo.InvariantCulture);
}

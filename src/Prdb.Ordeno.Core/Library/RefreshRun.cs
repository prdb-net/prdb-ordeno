using System.Globalization;

using Prdb.Ordeno.Core.History;

namespace Prdb.Ordeno.Core.Library;

/// <summary>
/// How often the tool checks what it filed against what prdb says now, when
/// nobody asks it to and the switch is on — ADR 0032.
/// </summary>
/// <remarks>
/// Constants rather than settings, exactly as <see cref="FilingSchedule"/>'s
/// are. What makes these numbers rather than preferences is the arithmetic they
/// come out of: a pass over a library costs one prdb request per
/// <see cref="Review.IVideoLookup.MaxBatch"/> scenes, so a slice fixes what a
/// tick costs whatever size the library is.
/// </remarks>
public static class RefreshSchedule
{
    /// <summary>
    /// A day. What this chases — a title, a date or a cast entry corrected in
    /// prdb — arrives on prdb's schedule rather than on anybody's, and nobody is
    /// waiting in front of the screen for it. Somebody who is presses the button.
    /// </summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    /// <summary>
    /// The wait before the first unattended run after a start. Long enough that
    /// a container restarted twice in a morning does not spend a pass over the
    /// library each time.
    /// </summary>
    public static readonly TimeSpan FirstRunDelay = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How many scenes one unattended run looks at. Five hundred is ten prdb
    /// requests, and — on the first pass of an installation that has just turned
    /// artwork on — up to five hundred images off a CDN. Both are what a night
    /// costs, and neither grows with the library: a run takes the scenes least
    /// recently checked, so a bigger library comes round more slowly rather than
    /// costing more per run.
    /// </summary>
    public const int Slice = 500;

    /// <summary>
    /// How much of the hourly quota a run leaves behind before stopping — an
    /// order of magnitude above
    /// <see cref="Identification.IdentificationSchedule.QuotaReserve"/>, and
    /// deliberately. Identification is the loop: a download nobody has
    /// identified cannot be filed at all, while a correction that arrives
    /// tomorrow instead of tonight costs nobody anything.
    /// </summary>
    public const int QuotaReserve = 50;

    /// <summary>
    /// The same reserve against the monthly window. A run that repeats nightly
    /// over a whole library is the first thing in this tool that is a monthly
    /// consumer rather than an hourly one, and a month spent here is a month
    /// identification does not have.
    /// </summary>
    public const int MonthlyQuotaReserve = 500;
}

/// <summary>
/// What a refresh found in one scene directory, when there is anything to say
/// about it.
/// </summary>
/// <remarks>
/// A scene whose sidecar already says what prdb says produces no result at all.
/// The steady state of this feature is a run that reads a few hundred documents
/// and writes none of them, and a list of five hundred rows saying "unchanged"
/// is a list nobody reads — the counts on <see cref="RefreshReport"/> carry the
/// scale.
/// </remarks>
/// <param name="Scene">
/// The scene directory's own name, which is what somebody recognises. The
/// absolute path is on <paramref name="Directory"/> for a bug report.
/// </param>
/// <param name="VideoPath">
/// The video this scene was filed as, which is what the media server is told to
/// read again once a sidecar next to it has changed.
/// </param>
/// <param name="Sidecar">
/// What happened to the <c>movie.nfo</c>, in words, or <c>null</c> when nothing
/// did.
/// </param>
/// <param name="Artwork">The same for the image, and silent far more often.</param>
/// <param name="Problem">
/// Why nothing could be done here, when that is the answer. A scene left alone
/// because its sidecar is somebody else's is not a problem and does not use
/// this.
/// </param>
public sealed record RefreshResult(
    string Directory,
    string VideoPath,
    string Scene,
    Guid VideoId,
    string? Title,
    bool WroteSidecar,
    bool WroteArtwork,
    string? Sidecar = null,
    string? Artwork = null,
    string? Problem = null)
{
    public bool Changed => WroteSidecar || WroteArtwork;
}

/// <summary>
/// What one refresh did — ADR 0032.
/// </summary>
/// <param name="Checked">
/// How many scene directories the run reached. Not how many it wrote to.
/// </param>
/// <param name="Waiting">
/// How many filed scenes it did not reach, because the slice ended or the quota
/// did. They are the ones the next run starts with.
/// </param>
/// <param name="Notes">
/// The scenes worth a row: everything that changed, and everything that could
/// not be done. Capped by the caller that shows it, like every other list here.
/// </param>
/// <param name="Problem">
/// What stopped the run — no library, no API key, prdb unreachable, the quota
/// nearly spent. Everything the run had already written stays written.
/// </param>
public sealed record RefreshReport(
    int Checked,
    int Sidecars,
    int Images,
    int Waiting,
    IReadOnlyList<RefreshResult> Notes,
    string? Problem = null)
{
    public static readonly RefreshReport Nothing = new(0, 0, 0, 0, []);

    /// <summary>Whether this run changed a file, which is what decides its row in the log.</summary>
    public bool ChangedAnything => Sidecars > 0 || Images > 0;

    /// <summary>
    /// The one line the screen shows and the operation log keeps (ADR 0028),
    /// from one place so that the two cannot describe the same run differently.
    /// </summary>
    public string Account
    {
        get
        {
            if (Checked == 0)
            {
                return "Nothing was checked: this library holds no scenes this tool filed.";
            }

            var checkedScenes = Checked == 1
                ? "1 scene was checked against what prdb says now"
                : $"{Count(Checked)} scenes were checked against what prdb says now";

            var wrote = (Sidecars, Images) switch
            {
                (0, 0) => ", and every one of them already said it",
                (_, 0) => $", and {Files(Sidecars)} brought up to date",
                (0, _) => $", and {Pictures(Images)} written where there had been none",
                _ => $", {Files(Sidecars)} brought up to date and {Pictures(Images)} written where "
                    + "there had been none",
            };

            var left = Waiting == 0
                ? "."
                : Waiting == 1
                    ? ". 1 more scene is waiting for the next run."
                    : $". {Count(Waiting)} more scenes are waiting for the next run.";

            return checkedScenes + wrote + left;
        }
    }

    private static string Count(int number) => number.ToString("N0", CultureInfo.InvariantCulture);

    private static string Files(int number) =>
        number == 1 ? "1 metadata file was" : $"{Count(number)} metadata files were";

    private static string Pictures(int number) =>
        number == 1 ? "1 image was" : $"{Count(number)} images were";
}

/// <summary>
/// What there is to check, before anything is checked — one query over the
/// tool's own tables.
/// </summary>
/// <param name="Scenes">How many scenes this library holds that the tool filed.</param>
/// <param name="NeverChecked">
/// How many of them nothing has looked at yet. Every scene of a library filed
/// before this feature existed, exactly once.
/// </param>
/// <param name="Oldest">
/// When the least recently checked scene was last looked at, or <c>null</c>
/// while any of them has never been. It is what the screen turns into "the whole
/// library has been checked since Tuesday".
/// </param>
public sealed record RefreshStanding(int Scenes, int NeverChecked, DateTimeOffset? Oldest)
{
    public static readonly RefreshStanding Nothing = new(0, 0, null);
}

/// <summary>
/// The refresh that is under way, or the last one — ADR 0032.
/// </summary>
/// <remarks>
/// Held in memory and forgotten on a restart, like the filing run's: what
/// survives a restart is in the database and in the operation log, and a
/// container that has just come up has refreshed nothing.
/// </remarks>
/// <param name="AskedBy">
/// Who the last run happened for. A screen showing a run that appeared out of
/// nowhere has to say where it came from, which is the same rule the filing
/// screen follows (ADR 0031).
/// </param>
public sealed record RefreshRun(
    bool Running,
    AskedBy AskedBy,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    RefreshReport? Report)
{
    public static readonly RefreshRun Never =
        new(Running: false, AskedBy.Person, null, null, null);

    public RefreshRun Started(DateTimeOffset at, AskedBy askedBy) =>
        new(Running: true, askedBy, at, null, Report);

    public RefreshRun Finished(DateTimeOffset at, RefreshReport report) =>
        new(Running: false, AskedBy, StartedAt, at, report);

    /// <summary>What the last run did, or <c>null</c> when there has not been one.</summary>
    public string? WhatItDid => Report?.Account;

    public string? Problem => Report?.Problem;
}

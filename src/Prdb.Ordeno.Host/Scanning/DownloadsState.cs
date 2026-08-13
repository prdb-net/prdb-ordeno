using Prdb.Ordeno.Core.Identification;
using Prdb.Ordeno.Core.Scanning;
using Prdb.Ordeno.Infrastructure.Identification;
using Prdb.Ordeno.Infrastructure.Scanning;

namespace Prdb.Ordeno.Host.Scanning;

/// <summary>
/// One watched directory and what is in it.
/// </summary>
public sealed record ScannedSourceState(
    int SourceId,
    string Path,
    bool Reachable,
    string? Problem,
    int Ready,
    int Settling,
    int Total);

/// <summary>
/// What prdb said one file is.
/// </summary>
/// <param name="State">
/// <c>recognised</c>, <c>ambiguous</c>, <c>siteOnly</c> or <c>unrecognised</c>.
/// The last two are not the same thing and must not be shown as though they
/// were: a file whose site is known is further along than one that matched
/// nothing.
/// </param>
/// <param name="Answer">The answer in one line, ready to put on a row.</param>
/// <param name="Because">Which rung got that far, in words.</param>
public sealed record RecognisedState(
    string State,
    string Answer,
    string? Because,
    Guid? VideoId,
    int Candidates,
    DateTimeOffset AskedAt);

/// <summary>
/// One video the tool has found. <paramref name="Ready"/> means it has stopped
/// being written to — not that anything has been done with it.
/// </summary>
/// <param name="Recognised">
/// What prdb said it is, or <c>null</c> if it has not been asked about yet.
/// </param>
public sealed record ScannedFileState(
    int Id,
    int SourceId,
    string Path,
    string Name,
    long SizeBytes,
    bool Ready,
    DateTimeOffset FirstSeenAt,
    RecognisedState? Recognised);

/// <summary>
/// How far the tool has got with prdb.
/// </summary>
/// <param name="NotBefore">
/// When the tool will ask again, when prdb has asked to be left alone until
/// then. Nothing is wrong while this is set.
/// </param>
/// <param name="PerceptualBacklog">
/// How many files are still queued for a perceptual hash. Nothing is waiting on
/// it — it is reported because it is the one part of the tool that costs
/// noticeable CPU.
/// </param>
public sealed record IdentificationState(
    bool Running,
    DateTimeOffset? LastRunFinishedAt,
    int LastRunAsked,
    string? Problem,
    DateTimeOffset? NotBefore,
    int Recognised,
    int Ambiguous,
    int SiteOnly,
    int Unrecognised,
    int Waiting,
    int PerceptualBacklog,
    string? WhatItRecognised);

/// <summary>
/// Everything the downloads screen shows: what is in the directories, and what
/// the tool has made of it. The file list is capped — <paramref name="Total"/>
/// is the real number.
/// </summary>
public sealed record ScanState(
    bool Scanning,
    DateTimeOffset? LastScanStartedAt,
    DateTimeOffset? LastScanFinishedAt,
    string? Problem,
    bool OnboardingComplete,
    IReadOnlyList<ScannedSourceState> Sources,
    IReadOnlyList<ScannedFileState> Files,
    int Ready,
    int Settling,
    int Total,
    string WhatItFound,
    IdentificationState Identification);

/// <summary>
/// The one document the downloads screen reads, built from the two things that
/// happen to those files — looking, and asking prdb what was found.
/// </summary>
/// <remarks>
/// One document rather than two endpoints because it is one screen and one poll:
/// a row carries both what the file is and whether it has been recognised, and
/// splitting them would have the browser stitching two answers from two moments
/// back together.
/// </remarks>
internal static class DownloadsState
{
    public static async Task<ScanState> ReadAsync(
        ScanService scanning,
        PerceptualHashService hashing,
        ScanRun scan,
        IdentificationRun identification,
        CancellationToken cancellationToken)
    {
        var inventory = await scanning.ReadAsync(cancellationToken);
        var backlog = await hashing.BacklogAsync(cancellationToken);

        return Of(inventory, scan, identification, backlog);
    }

    private static ScanState Of(
        Inventory inventory,
        ScanRun scan,
        IdentificationRun identification,
        int backlog) => new(
        Scanning: scan.Running,
        LastScanStartedAt: scan.StartedAt,
        LastScanFinishedAt: scan.FinishedAt,
        Problem: scan.Problem,
        OnboardingComplete: inventory.OnboardingComplete,
        Sources:
        [
            .. inventory.Sources.Select(source => new ScannedSourceState(
                source.SourceId,
                source.Path,
                source.Reachable,
                source.Problem,
                source.Ready,
                source.Settling,
                source.Total)),
        ],
        Files:
        [
            .. inventory.Files.Select(file => new ScannedFileState(
                file.Id,
                file.SourceId,
                file.Path,
                file.Name,
                file.SizeBytes,
                file.Ready,
                file.FirstSeenAt,
                Recognised(file.Recognised))),
        ],
        Ready: inventory.Ready,
        Settling: inventory.Settling,
        Total: inventory.Total,
        WhatItFound: inventory.WhatItFound,
        Identification: new IdentificationState(
            Running: identification.Running,
            LastRunFinishedAt: identification.FinishedAt,
            LastRunAsked: identification.Asked,
            Problem: identification.Problem,
            NotBefore: identification.NotBefore,
            Recognised: inventory.Recognition.Recognised,
            Ambiguous: inventory.Recognition.Ambiguous,
            SiteOnly: inventory.Recognition.SiteOnly,
            Unrecognised: inventory.Recognition.Unrecognised,
            Waiting: inventory.Recognition.Waiting,
            PerceptualBacklog: backlog,
            WhatItRecognised: inventory.Recognition.WhatItRecognised));

    private static RecognisedState? Recognised(Recognition? recognition) =>
        recognition is null
            ? null
            : new RecognisedState(
                Name(recognition.State),
                recognition.InWords,
                recognition.Because,
                recognition.VideoId,
                recognition.Candidates,
                recognition.AskedAt);

    /// <summary>
    /// The state as a name rather than a number, the way the layout crosses this
    /// boundary too. A number in a generated type is a number the browser has to
    /// hold a second copy of the meaning of.
    /// </summary>
    private static string Name(RecognitionState state) => state switch
    {
        RecognitionState.Recognised => "recognised",
        RecognitionState.Ambiguous => "ambiguous",
        RecognitionState.SiteOnly => "siteOnly",
        _ => "unrecognised",
    };
}

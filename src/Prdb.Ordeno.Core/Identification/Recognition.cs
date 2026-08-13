using System.Globalization;

namespace Prdb.Ordeno.Core.Identification;

/// <summary>
/// The four answers a file can be in, once prdb has been asked. Three of them
/// are results and one is a dead end, and the difference matters on the screen:
/// a file whose site is known is further along than an unidentified one and must
/// not be shown as the same kind of thing.
/// </summary>
public enum RecognitionState
{
    /// <summary>One video, named.</summary>
    Recognised,

    /// <summary>Several videos fitted equally well. A question for a person, not a failure.</summary>
    Ambiguous,

    /// <summary>The site is known and the scene is not.</summary>
    SiteOnly,

    /// <summary>The ladder ran out. Nothing matched, not even a site.</summary>
    Unrecognised,
}

/// <summary>
/// What the tool was told about one file, as the screen shows it.
/// </summary>
/// <param name="Candidates">How many videos fitted equally well; zero unless the answer was ambiguous.</param>
/// <param name="AskedAt">When prdb was asked. The answer is from then, not from now.</param>
public sealed record Recognition(
    MatchConfidence Confidence,
    MatchRung? MatchedBy,
    Guid? VideoId,
    string? Title,
    DateOnly? ReleaseDate,
    string? SiteTitle,
    int Candidates,
    DateTimeOffset AskedAt)
{
    public RecognitionState State => this switch
    {
        { Confidence: MatchConfidence.Ambiguous } => RecognitionState.Ambiguous,
        { VideoId: not null } => RecognitionState.Recognised,
        { MatchedBy: MatchRung.Site } => RecognitionState.SiteOnly,
        _ => RecognitionState.Unrecognised,
    };

    /// <summary>
    /// What it was recognised as, in one line. The title is what prdb answered
    /// with; a video without one still gets a line, because "recognised, and the
    /// tool cannot tell you as what" is the more confusing of the two.
    /// </summary>
    public string InWords => State switch
    {
        RecognitionState.Recognised => Describe(),
        RecognitionState.Ambiguous => Candidates == 1
            ? "More than one video fits. prdb did not choose."
            : $"{Candidates} videos fit equally well. prdb did not choose.",
        RecognitionState.SiteOnly => SiteTitle is null
            ? "The site is known, the scene is not."
            : $"{SiteTitle} — the site is known, the scene is not.",
        _ => "Not recognised.",
    };

    /// <summary>
    /// Which rung got that far, in words. It is on the row because it is the
    /// difference between an answer worth acting on and one worth checking, and
    /// because a bug report that quotes it can be answered.
    /// </summary>
    public string? Because => MatchedBy switch
    {
        MatchRung.OsHash => "matched by file hash",
        MatchRung.PerceptualHash => "matched by perceptual hash",
        MatchRung.FileName => "matched by a file name prdb knows",
        MatchRung.ReleaseName => "matched by its release name",
        MatchRung.Site => "the site was read out of the file name",
        _ => null,
    };

    private string Describe()
    {
        var title = string.IsNullOrWhiteSpace(Title) ? "A video prdb knows" : Title;
        var site = string.IsNullOrWhiteSpace(SiteTitle) ? null : SiteTitle;
        var date = ReleaseDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        return (site, date) switch
        {
            (not null, not null) => $"{title} — {site}, {date}",
            (not null, null) => $"{title} — {site}",
            (null, not null) => $"{title} — {date}",
            _ => title,
        };
    }
}

/// <summary>
/// How far the download directories have got, counted.
/// </summary>
/// <param name="Waiting">
/// Files that have finished downloading and have not been asked about yet. The
/// number that goes down while the tool works through a first run.
/// </param>
public sealed record RecognitionSummary(
    int Recognised,
    int Ambiguous,
    int SiteOnly,
    int Unrecognised,
    int Waiting)
{
    public static readonly RecognitionSummary Nothing = new(0, 0, 0, 0, 0);

    public int Answered => Recognised + Ambiguous + SiteOnly + Unrecognised;

    /// <summary>
    /// The line about recognition, under the one about what was found. It says
    /// nothing at all when there is nothing to say, rather than reporting six
    /// zeroes at somebody who has not finished downloading their first file.
    /// </summary>
    public string? WhatItRecognised
    {
        get
        {
            if (Answered == 0)
            {
                return Waiting == 0
                    ? null
                    : $"{Number(Waiting)} {(Waiting == 1 ? "video is" : "videos are")} waiting to be "
                        + "identified. prdb is asked about them in batches, a few minutes apart.";
            }

            var parts = new List<string>();

            if (Recognised > 0)
            {
                parts.Add($"{Number(Recognised)} recognised");
            }

            if (Ambiguous > 0)
            {
                parts.Add($"{Number(Ambiguous)} with more than one match");
            }

            if (SiteOnly > 0)
            {
                parts.Add($"{Number(SiteOnly)} known by site only");
            }

            if (Unrecognised > 0)
            {
                parts.Add($"{Number(Unrecognised)} not recognised");
            }

            var counted = $"Of {Number(Answered)} asked about: {Join(parts)}.";

            return Waiting == 0 ? counted : counted + $" {Number(Waiting)} still to ask about.";
        }
    }

    private static string Join(IReadOnlyList<string> parts) => parts.Count switch
    {
        0 => string.Empty,
        1 => parts[0],
        _ => string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1],
    };

    private static string Number(int number) => number.ToString("N0", CultureInfo.InvariantCulture);
}

namespace Prdb.Ordeno.Core.Review;

/// <summary>
/// What a person decided about a file the tool could not settle on its own.
/// Both values are answers — ADR 0023 — and the difference is what happens next,
/// not how far the tool got.
/// </summary>
public enum ResolutionKind
{
    /// <summary>A person named the video. It is filed as that video, exactly as though prdb had.</summary>
    Assigned,

    /// <summary>
    /// A person said this file is not to be filed: not a video, or not one they
    /// want in the library. It is not deleted and not hidden from the inventory —
    /// it is a file the tool has been told to leave alone.
    /// </summary>
    Dismissed,
}

/// <summary>
/// How the person got to the video they named. One column, recorded because it
/// is the difference between confirming a choice prdb offered and finding one it
/// did not — which is what an assignment contributed back to prdb would rest on.
/// </summary>
public enum ResolvedFrom
{
    /// <summary>One of the candidates prdb named when it declined to choose.</summary>
    Candidate,

    /// <summary>A video the person searched prdb for.</summary>
    Search,
}

/// <summary>
/// A person's answer about one file, kept apart from prdb's — ADR 0023.
/// </summary>
/// <remarks>
/// The title, site and date are here for the same reason they are on an
/// identification: a row has to read as a sentence, and a path has to be built
/// without asking prdb again. They are what prdb answered about the video the
/// person named, fetched when the decision was recorded — never what the browser
/// sent, which is only ever an id.
/// </remarks>
/// <param name="From">How the video was found, or <c>null</c> for a dismissal, which came from neither.</param>
public sealed record Resolution(
    ResolutionKind Kind,
    ResolvedFrom? From,
    DateTimeOffset DecidedAt,
    Guid? VideoId = null,
    string? Title = null,
    DateOnly? ReleaseDate = null,
    string? SiteTitle = null)
{
    /// <summary>
    /// What was decided, in one line, said as something a person did. The queue
    /// shows it next to what prdb answered, and the two must not read as one
    /// thing that got better.
    /// </summary>
    public string InWords => Kind switch
    {
        ResolutionKind.Dismissed => "You said this one is not to be filed.",
        _ => $"You said this is {Named}.",
    };

    private string Named
    {
        get
        {
            var title = string.IsNullOrWhiteSpace(Title) ? "a video prdb knows" : Title;
            var site = string.IsNullOrWhiteSpace(SiteTitle) ? null : SiteTitle;

            return site is null ? title : $"{title} — {site}";
        }
    }
}

using System.Globalization;

using Prdb.Ordeno.Core.Library;

namespace Prdb.Ordeno.Core.MediaServer;

/// <summary>What the connection test found, worst thing first.</summary>
public enum MediaServerCheckStatus
{
    /// <summary>
    /// It answered, it reads the dates the tool writes, and it holds something
    /// the tool filed. This is the only status that has proved anything.
    /// </summary>
    Working,

    /// <summary>
    /// It answered and nothing is wrong with it, but the tool has filed nothing
    /// yet, so the two have not been shown to be looking at the same files. Not a
    /// problem — the ordinary state during setup.
    /// </summary>
    Unproven,

    /// <summary>
    /// It answered and holds none of the videos this tool has filed. The state
    /// that looks fine and does nothing, which is why it is said out loud.
    /// </summary>
    Unmatched,

    /// <summary>
    /// It answered, and its release date format will discard every date the tool
    /// writes without either side reporting anything.
    /// </summary>
    DatesDiscarded,

    /// <summary>It answered, and would not let this key in.</summary>
    Refused,

    /// <summary>Nothing answered.</summary>
    Unreachable,
}

/// <summary>
/// The connection test of ADR 0018, which proves more than reachability: that
/// the key works, that the server will read the dates the tool writes, and that
/// the two are looking at the same files.
/// </summary>
/// <remarks>
/// This is the one place a media server connection fails out loud. Everywhere
/// else a server that is down is a line in a log and nothing else, because
/// everywhere else nobody is standing in front of it.
/// </remarks>
public sealed record MediaServerCheck(MediaServerCheckStatus Status, string Message)
{
    /// <summary>
    /// The connection is worth storing. Anything the server said about itself is
    /// something the user can act on; a server that never answered is not, and a
    /// key it refused is wrong rather than untested.
    /// </summary>
    public bool Answered => Status is not (MediaServerCheckStatus.Refused or MediaServerCheckStatus.Unreachable);

    /// <summary>Nothing at all is wrong, and it has been demonstrated rather than assumed.</summary>
    public bool Working => Status is MediaServerCheckStatus.Working;

    public static MediaServerCheck Refused(string problem) =>
        new(MediaServerCheckStatus.Refused, problem);

    public static MediaServerCheck Unreachable(string problem) =>
        new(MediaServerCheckStatus.Unreachable, problem);

    /// <summary>
    /// The verdict on a server that answered: what it is, what it will do with
    /// the dates, and whether it holds anything the tool put there.
    /// </summary>
    /// <param name="libraryRoot">
    /// The library as this container sees it, which is half of the sentence that
    /// makes a path substitution visible. Nobody configured that substitution and
    /// nothing else in either product will ever mention it.
    /// </param>
    public static MediaServerCheck Of(MediaServerFacts facts, MediaServerMatch match, string libraryRoot)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(match);

        var sentences = new List<string>
        {
            $"{facts.Name} {facts.Version} answered, and it accepted the key.",
        };

        // Named first when it is wrong, because it is the failure neither side
        // reports: correct sidecars, and a library with no dates in it.
        var dates = DatesOf(facts, sentences);
        var matching = MatchOf(match, libraryRoot, sentences);

        var status = dates is MediaServerCheckStatus.DatesDiscarded
            ? MediaServerCheckStatus.DatesDiscarded
            : matching;

        return new MediaServerCheck(status, string.Join(' ', sentences));
    }

    private static MediaServerCheckStatus DatesOf(MediaServerFacts facts, List<string> sentences)
    {
        if (facts.ReleaseDateFormat is null)
        {
            sentences.Add(
                "Its release date format could not be read, so the tool cannot say whether the "
                + "dates it writes will be kept.");

            return MediaServerCheckStatus.Working;
        }

        if (string.Equals(facts.ReleaseDateFormat, MovieNfo.ReleaseDateFormat, StringComparison.Ordinal))
        {
            sentences.Add(
                $"It reads release dates as {MovieNfo.ReleaseDateFormat}, which is the one format "
                + "the tool writes.");

            return MediaServerCheckStatus.Working;
        }

        sentences.Add(
            $"Its release date format is set to '{facts.ReleaseDateFormat}', and the tool writes "
            + $"{MovieNfo.ReleaseDateFormat}. Every date it writes is discarded without an error on "
            + "either side, and the library ends up with no dates and no production years — set the "
            + $"format back to {MovieNfo.ReleaseDateFormat} in the media server's metadata settings.");

        return MediaServerCheckStatus.DatesDiscarded;
    }

    private static MediaServerCheckStatus MatchOf(
        MediaServerMatch match,
        string libraryRoot,
        List<string> sentences)
    {
        if (match.LookedFor == 0)
        {
            sentences.Add(
                $"It holds {Count(match.Held, "item")}. Nothing has been filed yet, so there was "
                + "nothing of the tool's to look for over there — run this test again after the "
                + "first filing to see the two ends meet.");

            return MediaServerCheckStatus.Unproven;
        }

        if (!match.Proven)
        {
            sentences.Add(
                $"It holds {Count(match.Held, "item")}, and none of them is one of the "
                + $"{Count(match.LookedFor, "video")} this tool has filed. Nothing filed here will "
                + "be refreshed there: check that the media server's library points at the same "
                + "directory as this tool's library directory, and that it has scanned since.");

            return MediaServerCheckStatus.Unmatched;
        }

        sentences.Add(
            $"It holds {Count(match.Held, "item")}, including '{match.MatchedName}', which this "
            + $"tool filed — so {libraryRoot} here is {match.Substitution} there, and a sidecar "
            + "rewritten here can be shown there without waiting for a scan.");

        return MediaServerCheckStatus.Working;
    }

    private static string Count(int number, string singular) =>
        number == 1
            ? $"1 {singular}"
            : $"{number.ToString("N0", CultureInfo.InvariantCulture)} {singular}s";
}

/// <summary>
/// What came of looking for the tool's own files on the server.
/// </summary>
/// <param name="Held">How many items the server holds altogether.</param>
/// <param name="LookedFor">
/// How many filed videos were looked for. Zero means the tool has filed nothing
/// yet, which is not the same as having looked and found nothing.
/// </param>
/// <param name="MatchedName">The one that was found, in words a user recognises.</param>
/// <param name="Substitution">
/// What the server has in front of the library root — the mount prefix nobody
/// configured, worked out from the match itself.
/// </param>
public sealed record MediaServerMatch(
    int Held,
    int LookedFor,
    string? MatchedName = null,
    string? Substitution = null)
{
    public bool Proven => MatchedName is not null;
}

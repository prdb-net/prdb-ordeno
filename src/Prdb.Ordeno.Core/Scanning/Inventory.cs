using System.Globalization;

using Prdb.Ordeno.Core.Identification;

namespace Prdb.Ordeno.Core.Scanning;

/// <summary>
/// One watched directory and what the last scan left in it.
/// </summary>
/// <param name="Reachable">
/// Whether the directory can be read now — checked when this is built, not when
/// the scan ran. A share that went away over the weekend is the difference
/// between the two, and reporting it as an empty directory whose files all
/// vanished would be a lie the user acts on.
/// </param>
public sealed record ScannedSource(
    int SourceId,
    string Path,
    bool Reachable,
    string? Problem,
    int Ready,
    int Settling)
{
    public int Total => Ready + Settling;
}

/// <summary>
/// One file the tool found, as the screen shows it.
/// </summary>
/// <param name="Name">
/// The path below its source directory, which is what a person recognises. The
/// full path is on the row too, because a bug report quotes that one.
/// </param>
/// <param name="Recognised">
/// What prdb said about it, or <c>null</c> if it has not been asked about yet —
/// which is every file that has not settled, and every settled one the next run
/// has not reached.
/// </param>
public sealed record ScannedFile(
    int Id,
    int SourceId,
    string Path,
    string Name,
    long SizeBytes,
    bool Ready,
    DateTimeOffset FirstSeenAt,
    Recognition? Recognised = null);

/// <summary>
/// What the tool currently believes is in the download directories. Built from
/// the database and a fresh look at each directory; nothing here has been moved,
/// renamed or written.
/// </summary>
/// <param name="Files">
/// The most recently discovered ones, capped at <see cref="Limit"/>. A first run
/// over an existing library is thousands of rows and a screen full of them helps
/// nobody — the counts carry the scale, the list carries the examples.
/// </param>
/// <param name="Recognition">
/// How far the files have got with prdb. Counted over the whole table rather
/// than over <paramref name="Files"/>, which is only the visible end of it.
/// </param>
public sealed record Inventory(
    bool OnboardingComplete,
    IReadOnlyList<ScannedSource> Sources,
    IReadOnlyList<ScannedFile> Files,
    RecognitionSummary Recognition)
{
    /// <summary>How many files the inventory sends to the browser at most.</summary>
    public const int Limit = 200;

    public int Ready => Sources.Sum(source => source.Ready);

    public int Settling => Sources.Sum(source => source.Settling);

    public int Total => Ready + Settling;

    /// <summary>
    /// The line at the top of the screen: what is there, and — because it would
    /// otherwise be assumed — what is not happening to it yet.
    /// </summary>
    public string WhatItFound
    {
        get
        {
            if (!OnboardingComplete)
            {
                return "Nothing is scanned yet. Finish the setup first.";
            }

            if (Sources.Count == 0)
            {
                return "There is no download directory to look in.";
            }

            if (Sources.All(source => !source.Reachable))
            {
                return Sources.Count == 1
                    ? "The download directory cannot be read at the moment, so nothing was scanned."
                    : "None of the download directories can be read at the moment, so nothing was "
                        + "scanned.";
            }

            if (Total == 0)
            {
                return "No videos in the download directories. New downloads are picked up on the "
                    + "next scan." + NotYet;
            }

            // "Waiting" rather than "still being written": a file is waiting
            // until two scans have seen it unchanged, and the first scan over a
            // library that finished downloading last year puts every file in
            // that state for a few minutes. Saying they are being written would
            // be wrong about exactly the case a first run produces.
            var found = Settling == 0
                ? $"{Count(Ready, "video")}, all of them finished downloading."
                : Ready == 0
                    ? $"{Count(Settling, "video")}, none confirmed finished yet — the tool watches "
                        + "a file until it has stopped changing before counting on it."
                    : $"{Count(Total, "video")}: {Number(Ready)} finished downloading, "
                        + $"{Number(Settling)} still waiting to be confirmed.";

            var unreachable = Sources.Count(source => !source.Reachable);
            var missing = unreachable == 0
                ? string.Empty
                : $" {Count(unreachable, "download directory", "download directories")} could not be "
                    + "read, so what is in there is not counted.";

            return found + missing + NotYet;
        }
    }

    /// <summary>
    /// The tool files what it recognises, and does it only when asked —
    /// ADR 0022, until the operation log gives the unattended version a way
    /// back. Saying so is not modesty: someone who reads a list of their
    /// downloads with the video each was recognised as next to it will otherwise
    /// conclude that it is filing them on its own, and find months later that
    /// nothing moved.
    /// </summary>
    private string NotYet =>
        Total == 0
            ? string.Empty
            : " Nothing is filed on its own yet: the tool works out what it would do, and moves a "
                + "file only when you ask it to.";

    private static string Count(int number, string singular, string? plural = null) =>
        number == 1 ? $"{Number(number)} {singular}" : $"{Number(number)} {plural ?? singular + "s"}";

    private static string Number(int number) => number.ToString("N0", CultureInfo.InvariantCulture);
}

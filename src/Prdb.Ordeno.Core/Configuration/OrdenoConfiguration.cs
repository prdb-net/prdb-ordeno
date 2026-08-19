using Prdb.Ordeno.Core.Library;

namespace Prdb.Ordeno.Core.Configuration;

/// <summary>One watched directory, as it stands right now.</summary>
public sealed record ConfiguredSource(int Id, DirectoryInspection Inspection, FileMovement Movement);

/// <summary>
/// Everything onboarding collects (ADR 0009), together with what the tool found
/// when it last looked at the paths. It is a read model: built fresh from the
/// database and the filesystem, never stored, so a mount that disappeared over
/// the weekend shows up the next time the page is opened.
/// </summary>
/// <remarks>
/// Neither API key is on it. The tool stores them and never hands them back to
/// the browser — whether one is set is all the UI is told.
/// </remarks>
/// <param name="MediaServerUrl">
/// Where the optional media server connection points, or <c>null</c> because
/// there is none — which is the default and not a degraded state (ADR 0018).
/// The key that goes with it is not here, for the reason above; an address is
/// only ever stored together with one, so this being set is what "a connection
/// is configured" means.
/// </param>
/// <param name="Artwork">
/// Whether filing downloads one image per scene (ADR 0027). Off unless somebody
/// turned it on: it spends a connection and a disk that are not the tool's, and
/// it is not part of what onboarding collects, because the tool runs without it.
/// </param>
/// <param name="Unattended">
/// Whether the tool files on its own, on the interval in
/// <see cref="Library.FilingSchedule"/> (ADR 0031). Off unless somebody turned it
/// on — the same rule as the image switch, applied to the one thing in this tool
/// that moves a file the user cannot get back.
/// </param>
public sealed record OrdenoConfiguration(
    bool ApiKeySet,
    IReadOnlyList<ConfiguredSource> Sources,
    DirectoryInspection? Target,
    LibraryLayout? Layout,
    string? MediaServerUrl,
    bool Artwork,
    bool Unattended,
    DateTimeOffset? OnboardingCompletedAt)
{
    /// <summary>
    /// Onboarding has been walked to its end. Until it has, the tool scans
    /// nothing — ADR 0009.
    /// </summary>
    public bool Complete => OnboardingCompletedAt is not null;

    /// <summary>
    /// Everything is answered and everything answered still checks out, so
    /// finishing is allowed. A source that has become unreadable since it was
    /// added blocks this as surely as one that was never added.
    /// </summary>
    public bool ReadyToComplete =>
        ApiKeySet
        && Sources.Count > 0
        && Sources.All(source => source.Inspection.Usable)
        && Target is { Usable: true }
        && Layout is not null;

    /// <summary>
    /// The line the guided path ends on, and the line it shows in the meantime:
    /// either what the tool is waiting for, or what it is going to do.
    /// </summary>
    public string WhatHappensNext
    {
        get
        {
            if (!ApiKeySet)
            {
                return "Nothing is scanned yet. prdb-ordeno needs a prdb API key before it can "
                    + "recognise anything.";
            }

            if (Sources.Count == 0)
            {
                return "Nothing is scanned yet. Add at least one directory your downloads "
                    + "arrive in.";
            }

            if (Target is null || Layout is null)
            {
                return "Nothing is scanned yet. Choose the directory your library lives in and "
                    + "the media server that reads it.";
            }

            var broken = Sources.FirstOrDefault(source => !source.Inspection.Usable);
            if (broken is not null)
            {
                return $"Nothing is scanned while a directory is unusable: {broken.Inspection.Message}";
            }

            if (!Target.Usable)
            {
                return $"Nothing is scanned while the library directory is unusable: {Target.Message}";
            }

            var directories = Sources.Count == 1 ? "directory" : "directories";
            var copying = Sources.Count(source => source.Movement is not FileMovement.Rename);
            var speed = copying switch
            {
                0 => " Everything it files will be renamed into place, which is instant.",
                _ when copying == Sources.Count =>
                    " Videos will be copied to the library and only then deleted from the download "
                    + "directory, because the two are on different filesystems — correct, but "
                    + "as slow as the files are large.",
                _ => $" {copying} of those {directories} sit on a different filesystem from the "
                    + "library, so videos from them will be copied rather than renamed into place.",
            };

            var ending = Complete
                ? $"prdb-ordeno is watching {Sources.Count} {directories} and will file what it "
                : $"prdb-ordeno will watch {Sources.Count} {directories} and file what it ";

            // Whether the tool moves files on its own is now a switch rather than
            // a promise (ADR 0031), and this sentence says which way it is set.
            // A vaguer one in its place would be worse than the promise it
            // replaces: somebody who believes the wrong half of this stops
            // looking, and reports months later as a bug either that nothing was
            // filed or that everything was.
            var filing = !Complete
                ? string.Empty
                : Unattended
                    ? " It files on its own, a few minutes after a download has finished arriving. "
                        + "Every run is in the History, where one that went wrong can be put back."
                    : " Filing happens when you ask for it: the Filing screen shows what would "
                        + "happen to each video, and a button carries it out. Under Settings → "
                        + "Library it can be told to file on its own instead.";

            // The same rule as below, applied to the other switch nobody has to
            // touch: an installation that files without images is the ordinary
            // one, so silence is what off reads as.
            var images = Artwork
                ? " Each scene it files also gets one image next to it as "
                    + $"'{ScenePath.ArtworkFileName}', downloaded from prdb, where there is no file "
                    + "at that name already."
                : string.Empty;

            // Said only when there is something to say. A setup that left the two
            // fields blank is the ordinary one, and a sentence about what it is
            // missing would turn a deliberate choice into a warning — ADR 0018.
            var connected = MediaServerUrl is null
                ? string.Empty
                : $" Each video it files is shown to {MediaServerUrl} straight away, so a new scene "
                    + "appears there without waiting for the next library scan.";

            return ending
                + $"recognises into {Target.Path}, in the layout {LibraryLayouts.NameOf(Layout.Value)} "
                + "reads." + speed + images + filing + connected;
        }
    }
}

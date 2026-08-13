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
/// The API key is not on it. The tool stores the key and never hands it back to
/// the browser — whether one is set is all the UI is told.
/// </remarks>
public sealed record OrdenoConfiguration(
    bool ApiKeySet,
    IReadOnlyList<ConfiguredSource> Sources,
    DirectoryInspection? Target,
    LibraryLayout? Layout,
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

            // A finished setup does watch now — the scan runs on its own — but it
            // still files nothing, and a sentence that lets someone believe
            // otherwise is one they act on: they stop looking, and report months
            // later as a bug that their downloads were never touched. The second
            // half of this goes when filing lands, and not one release earlier.
            var ending = Complete
                ? $"prdb-ordeno is watching {Sources.Count} {directories} and will file what it "
                : $"prdb-ordeno will watch {Sources.Count} {directories} and file what it ";

            var notYet = Complete
                ? " Nothing is filed yet — that arrives with the first release. Until then the tool "
                    + "reports what it finds, works out what it is, and leaves it where it is."
                : string.Empty;

            return ending
                + $"recognises into {Target.Path}, in the layout {LibraryLayouts.NameOf(Layout.Value)} "
                + "reads." + speed + notYet;
        }
    }
}

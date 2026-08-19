using Prdb.Ordeno.Core.Configuration;

namespace Prdb.Ordeno.Core.Library;

/// <summary>
/// Works out what would happen to one video, and writes nothing at all.
/// </summary>
/// <remarks>
/// <para>
/// Everything filing decides is decided here: whether the scene may be filed at
/// all, whether the library already holds it, whether this is a second quality
/// or a second copy, which directory it goes in and what it is called when it
/// gets there. The step that follows carries the answer out and makes no
/// decisions of its own — which is what
/// <see href="https://github.com/prdb-net/prdb-ordeno/blob/main/docs/adr/0022-filing-happens-when-it-is-asked-for.md">ADR 0022</see>
/// needs in order for a preview and a run to be the same thing rather than two
/// things that agree.
/// </para>
/// <para>
/// It asks a filesystem what is at a path and nothing else. That question is an
/// interface (<see cref="ISceneDirectories"/>), so this project still touches no
/// I/O — ADR 0012.
/// </para>
/// </remarks>
public sealed class FilingPlanner(
    TargetPaths targets,
    ISceneDirectories directories,
    ISidecars sidecars,
    ISceneArtwork artwork)
{
    /// <summary>
    /// The plan for one video.
    /// </summary>
    /// <param name="scene">
    /// What prdb named, or <c>null</c> when it named nothing that can be filed.
    /// ADR 0019 sends that file to the review queue rather than into the
    /// library, and <see cref="Scene.From"/> is where the question is asked.
    /// </param>
    /// <param name="quality">
    /// What was read out of the file. A reading that failed stops the filing:
    /// without it neither the skip nor the label can be decided (ADR 0020).
    /// </param>
    /// <param name="filed">
    /// What the tool remembers filing for this scene, in this library. Whether
    /// each of those is still on disk is checked here, because a record of a
    /// file the user has since deleted must not stop the scene being filed
    /// again.
    /// </param>
    /// <param name="wantsArtwork">
    /// Whether somebody switched artwork on. It arrives as an argument rather
    /// than being read here, because this project reads nothing (ADR 0012) — and
    /// it is false by default, which is the hard rule applied to bandwidth
    /// rather than to data (ADR 0027).
    /// </param>
    public FilingPlan Plan(
        int fileId,
        string sourcePath,
        string sourceName,
        string libraryRoot,
        FileMovement movement,
        Scene? scene,
        VideoQualityReading quality,
        IReadOnlyList<FiledCopy> filed,
        bool wantsArtwork = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryRoot);
        ArgumentNullException.ThrowIfNull(quality);
        ArgumentNullException.ThrowIfNull(filed);

        if (scene is null)
        {
            return FilingPlan.Blocked(
                fileId,
                sourcePath,
                sourceName,
                scene: null,
                "prdb named no scene for this file, so the layout has no name to file it under. "
                + "It stays where it is, and the review queue is where it gets one.");
        }

        if (!quality.WasRead || quality.Quality is not { } measured)
        {
            return FilingPlan.Blocked(
                fileId,
                sourcePath,
                sourceName,
                scene,
                quality.Message ?? "The quality of this file could not be read, so nothing was moved.");
        }

        var live = new List<FiledCopy>(filed.Count);

        foreach (var copy in filed)
        {
            switch (directories.StateOf(copy.Path))
            {
                case SceneDirectoryState.Occupied:
                    live.Add(copy);
                    break;

                case SceneDirectoryState.Unknown:
                    // The library could not be looked at. Filing on the strength
                    // of that would either put a second copy next to one it
                    // cannot see, or relabel a file it cannot find.
                    return FilingPlan.Blocked(
                        fileId,
                        sourcePath,
                        sourceName,
                        scene,
                        $"The tool files this scene as '{copy.FileName}' and could not look at "
                        + "that file to see whether it is still there. Nothing was moved.");

                default:
                    // Nothing at that path any more: the user moved or deleted
                    // it. The record is out of date rather than the library, and
                    // the scene is filed again as though for the first time.
                    break;
            }
        }

        return live.Count == 0
            ? First(fileId, sourcePath, sourceName, libraryRoot, movement, scene, measured, wantsArtwork)
            : Next(fileId, sourcePath, sourceName, movement, scene, measured, live, wantsArtwork);
    }

    /// <summary>
    /// A scene the library does not hold. This is the path #20 already answers:
    /// the layout's own name where it is free, the same name carrying prdb's
    /// scene id where it is not, and a stop where neither is possible.
    /// </summary>
    private FilingPlan First(
        int fileId,
        string sourcePath,
        string sourceName,
        string libraryRoot,
        FileMovement movement,
        Scene scene,
        VideoQuality quality,
        bool wantsArtwork)
    {
        var target = targets.For(libraryRoot, scene, sourceName);

        if (!target.CanBeFiled)
        {
            return FilingPlan.Blocked(fileId, sourcePath, sourceName, scene, target.Message!);
        }

        // Unlabelled: it is the only copy of this scene, and a label answers a
        // question nobody has asked yet. ADR 0020 puts one on it if and when a
        // second quality arrives.
        return new FilingPlan(
            target.Outcome is FilingTargetOutcome.CollisionBroken
                ? FilingOutcome.CollisionBroken
                : FilingOutcome.Filed,
            fileId,
            sourcePath,
            sourceName,
            scene,
            quality.Label,
            target.Directory,
            target.VideoFile(),
            Relabel: null,
            movement,
            target.Message,
            // Nothing is at that path, and this is known rather than asked:
            // a directory holding anything at all counts as occupied, so a
            // target that got this far is one no sidecar can be sitting in.
            new SidecarPlan(SidecarAction.Write, Sidecar(target.Directory!)),
            // And for the same reason there is no image in it either.
            wantsArtwork
                ? new ArtworkPlan(ArtworkAction.Write, Artwork(target.Directory!))
                : ArtworkPlan.None);
    }

    /// <summary>
    /// A scene the library already holds. Either at this quality, in which case
    /// nothing is filed (ADR 0003), or at another, in which case this one goes
    /// next to it and the copy that is there is relabelled first (ADR 0020).
    /// </summary>
    private FilingPlan Next(
        int fileId,
        string sourcePath,
        string sourceName,
        FileMovement movement,
        Scene scene,
        VideoQuality quality,
        IReadOnlyList<FiledCopy> live,
        bool wantsArtwork)
    {
        var same = live.FirstOrDefault(copy =>
            string.Equals(copy.QualityLabel, quality.Label, StringComparison.OrdinalIgnoreCase));

        if (same is not null)
        {
            return new FilingPlan(
                FilingOutcome.AlreadyFiled,
                fileId,
                sourcePath,
                sourceName,
                scene,
                quality.Label,
                same.Directory,
                TargetPath: null,
                Relabel: null,
                movement,
                $"The library already holds this scene at {quality.Label}, as '{same.FileName}'. "
                + "The file is left exactly where it is: a second copy of the same quality is "
                + "not filed, and it is not deleted either.",
                // Nothing is filed, so nothing is written — not even a sidecar
                // that might be missing from a directory the tool filled last
                // year. When one is refreshed is its own decision, and a run
                // that reports moving nothing must not be quietly writing.
                SidecarPlan.None,
                // Least of all a download. Spending somebody's connection on a
                // run that reports moving nothing is the surprise ADR 0027's
                // switch exists to prevent.
                ArtworkPlan.None);
        }

        // Every copy of one scene lives in one directory — that is what makes
        // Jellyfin show them as one entry with two versions rather than as two
        // entries. So the newcomer goes into the directory that is already
        // there, whatever the layout would name that scene today.
        var home = live[0];
        var names = ScenePath.At(home.Directory, System.IO.Path.GetExtension(sourceName));
        var target = System.IO.Path.Combine(home.Directory, names.VideoFileNameFor(quality.Label));

        // At most one copy can be unlabelled: it is the first one filed, and it
        // stops being unlabelled the moment a second quality joins it.
        var plain = live.FirstOrDefault(copy => !copy.IsLabelled);
        var relabel = plain is null
            ? null
            : new FilingRelabel(plain.Path, System.IO.Path.Combine(
                plain.Directory,
                plain.Names.VideoFileNameFor(plain.QualityLabel)));

        var message = relabel is null
            ? $"The library already holds this scene at {Qualities(live)}, so this one goes in "
                + "next to it as a second version."
            : $"The library already holds this scene at {plain!.QualityLabel}, filed before there "
                + "was anything to tell it apart from. That file is renamed to "
                + $"'{System.IO.Path.GetFileName(relabel.To)}' first, so the media server lists "
                + "both versions by their quality rather than one of them by its whole file name.";

        return new FilingPlan(
            FilingOutcome.SecondQuality,
            fileId,
            sourcePath,
            sourceName,
            scene,
            quality.Label,
            home.Directory,
            target,
            relabel,
            movement,
            message,
            SidecarIn(home.Directory),
            wantsArtwork ? ArtworkIn(home.Directory) : ArtworkPlan.None);
    }

    /// <summary>
    /// Whose the sidecar in a directory the library already holds is. This is the
    /// only place the question has to be asked: everywhere else the scene
    /// directory is one nothing is in yet.
    /// </summary>
    private SidecarPlan SidecarIn(string directory)
    {
        var path = Sidecar(directory);

        return sidecars.StateOf(path) switch
        {
            SidecarState.Missing => new SidecarPlan(SidecarAction.Write, path),
            SidecarState.Ours => new SidecarPlan(SidecarAction.Replace, path),

            SidecarState.Foreign => new SidecarPlan(
                SidecarAction.Keep,
                path,
                $"There is a '{ScenePath.SidecarFileName}' in that directory this tool did not "
                + "write. The video is filed next to it and the file is left exactly as it is."),

            _ => new SidecarPlan(
                SidecarAction.Keep,
                path,
                $"The '{ScenePath.SidecarFileName}' in that directory could not be read, so it is "
                + "left alone rather than written over."),
        };
    }

    /// <summary>
    /// Whether the directory the library already holds has an image in it. The
    /// only place the question arises, for the reason above — and the answer is
    /// never "replace it", so there is no state here that leads to a write over
    /// somebody's file.
    /// </summary>
    private ArtworkPlan ArtworkIn(string directory)
    {
        var path = Artwork(directory);

        return artwork.StateOf(path) switch
        {
            ArtworkState.Missing => new ArtworkPlan(ArtworkAction.Write, path),

            ArtworkState.Present => new ArtworkPlan(
                ArtworkAction.Keep,
                path,
                $"There is already a '{ScenePath.ArtworkFileName}' in that directory. It is left "
                + "exactly as it is — deleting it is how you ask for a fresh one."),

            _ => new ArtworkPlan(
                ArtworkAction.Keep,
                path,
                $"The '{ScenePath.ArtworkFileName}' in that directory could not be looked at, so "
                + "nothing is downloaded over it."),
        };
    }

    private static string Sidecar(string directory) =>
        System.IO.Path.Combine(directory, ScenePath.SidecarFileName);

    private static string Artwork(string directory) =>
        System.IO.Path.Combine(directory, ScenePath.ArtworkFileName);

    private static string Qualities(IReadOnlyList<FiledCopy> live) =>
        live.Count == 1
            ? live[0].QualityLabel
            : string.Join(" and ", live.Select(copy => copy.QualityLabel));
}

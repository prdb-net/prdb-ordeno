namespace Prdb.Ordeno.Core.Configuration;

/// <summary>
/// What a directory is for, which decides what has to be true of it: the tool
/// reads a source and writes to the target.
/// </summary>
public enum DirectoryRole
{
    Source,
    Target,
}

/// <summary>
/// What is wrong with a directory the user gave, if anything. The tool checks
/// this while the user is looking at the field rather than on the first scan —
/// an unreadable mount discovered at three in the morning is a support
/// conversation, and the same fact discovered here is a typo.
/// </summary>
public enum DirectoryProblem
{
    None,

    /// <summary>Nothing was entered.</summary>
    Empty,

    /// <summary>Relative, so it means nothing to a container.</summary>
    NotAbsolute,

    /// <summary>No such path inside the container — usually a volume that was never mounted.</summary>
    Missing,

    /// <summary>The path is there, but it is a file.</summary>
    NotADirectory,

    /// <summary>The process cannot list it, which is a <c>PUID</c>/<c>PGID</c> answer nine times in ten.</summary>
    NotReadable,

    /// <summary>The process can read it but cannot create anything in it.</summary>
    NotWritable,
}

/// <summary>
/// One directory as the tool finds it. Produced fresh every time the
/// configuration is read, because a mount that was there yesterday is a
/// statement about yesterday.
/// </summary>
public sealed record DirectoryInspection(string Path, DirectoryRole Role, DirectoryProblem Problem)
{
    public bool Usable => Problem is DirectoryProblem.None;

    /// <summary>
    /// What to tell the user, naming what is wrong rather than reporting that
    /// something is. <c>null</c> when there is nothing to say.
    /// </summary>
    public string? Message => Problem switch
    {
        DirectoryProblem.None => null,

        DirectoryProblem.Empty => "Enter a path.",

        DirectoryProblem.NotAbsolute =>
            $"'{Path}' is not a full path. Give the path as the container sees it, starting "
            + "with a slash — /downloads, for instance, which is the right-hand side of the "
            + "volume in your Compose file.",

        DirectoryProblem.Missing =>
            $"There is nothing at {Path} inside the container. That path is the right-hand "
            + $"side of a volume — '- /volume1/downloads:{Path}' — and not a path on the NAS "
            + "itself, so a directory that exists on the host is still missing here until it "
            + "is mounted.",

        DirectoryProblem.NotADirectory => $"{Path} is a file, not a directory.",

        DirectoryProblem.NotReadable =>
            $"The tool is not allowed to read {Path}. It runs as the user given by PUID and "
            + "PGID, so either point those at whoever owns the directory or let that user in.",

        DirectoryProblem.NotWritable =>
            $"The tool is not allowed to write to {Path}. It runs as the user given by PUID "
            + "and PGID, so either point those at whoever owns the directory or let that user "
            + "write there. Read-only volumes look exactly like this too — a ':ro' on the end "
            + "of the mount.",

        _ => $"{Path} cannot be used.",
    };

    public static DirectoryInspection Fine(string path, DirectoryRole role) =>
        new(path, role, DirectoryProblem.None);
}

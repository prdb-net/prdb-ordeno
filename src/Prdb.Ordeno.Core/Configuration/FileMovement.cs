namespace Prdb.Ordeno.Core.Configuration;

/// <summary>
/// How a video will travel from a source directory to the library.
/// </summary>
/// <remarks>
/// ADR 0002 moves files rather than copying or linking them, and the same call
/// costs a millisecond or an hour depending on something the user cannot see:
/// whether the two directories sit on one filesystem. Within one, a rename is
/// instant and cannot half-happen; across two it is a copy, a verification and a
/// delete. This is not an error either way — it is the sentence the user should
/// read while choosing the directories rather than infer from a progress bar.
/// </remarks>
public enum FileMovement
{
    /// <summary>Same filesystem: a rename.</summary>
    Rename,

    /// <summary>Different filesystems: copy, verify, then delete the original.</summary>
    CopyThenDelete,

    /// <summary>The tool could not tell, so it says so instead of promising the fast path.</summary>
    Unknown,
}

public static class FileMovements
{
    /// <summary>
    /// The answer in the words a NAS user can act on. Acting on it means
    /// mounting the downloads and the library from one volume, which is the
    /// whole reason this is shown before anything has been filed.
    /// </summary>
    public static string Describe(FileMovement movement) => movement switch
    {
        FileMovement.Rename =>
            "On the same filesystem as the library, so filing a video is instant and cannot "
            + "half-finish.",

        FileMovement.CopyThenDelete =>
            "On a different filesystem from the library, so every video is copied, checked and "
            + "only then deleted — safe, but as slow as the file is large. Mounting this "
            + "directory and the library from the same volume makes it instant.",

        _ =>
            "The tool could not work out whether this is on the same filesystem as the "
            + "library, so expect videos to be copied rather than renamed into place.",
    };
}

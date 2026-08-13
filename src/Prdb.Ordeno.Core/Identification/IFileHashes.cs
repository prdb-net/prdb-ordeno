namespace Prdb.Ordeno.Core.Identification;

/// <summary>Why a file did or did not produce an <c>osHash</c>.</summary>
public enum OsHashState
{
    Computed,

    /// <summary>
    /// Under 128 KiB, which is smaller than the two blocks the hash is made of.
    /// A state, not a bug: such a file is identified by its name or not at all,
    /// and asking again after every scan would never produce a different answer.
    /// </summary>
    TooSmall,

    /// <summary>
    /// It could not be read right now — locked, disappearing, or on a share that
    /// went away mid-run. Unlike the two above this is worth trying again, so it
    /// must not be recorded as "this file has no hash".
    /// </summary>
    Unreadable,
}

public sealed record OsHashReading(OsHashState State, string? Hash)
{
    public static readonly OsHashReading TooSmall = new(OsHashState.TooSmall, null);

    public static readonly OsHashReading Unreadable = new(OsHashState.Unreadable, null);

    public static OsHashReading Of(string hash) => new(OsHashState.Computed, hash);
}

/// <summary>
/// The cheap, exact hash of a file: 64 KiB from each end and the length. It is
/// asked for here rather than computed, because Core opens nothing.
/// </summary>
public interface IFileHashes
{
    OsHashReading OsHashOf(string path);
}

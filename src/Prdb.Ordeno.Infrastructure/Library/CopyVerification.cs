using Prdb.Hashing;

namespace Prdb.Ordeno.Infrastructure.Library;

/// <summary>What reading the copy back said about it.</summary>
public enum CopyVerdict
{
    /// <summary>As far as this can tell, the copy is the file that was sent.</summary>
    Same,

    /// <summary>It stopped early, or never started. The commonest way a copy goes wrong.</summary>
    DifferentSize,

    /// <summary>The right length and not the right file.</summary>
    DifferentContent,

    /// <summary>One of the two could not be read back, so nothing is claimed either way.</summary>
    Unreadable,
}

/// <param name="Because">In words, for the sentence the user reads. <c>null</c> when it matched.</param>
public sealed record CopyCheck(CopyVerdict Verdict, string? Because = null)
{
    public bool Same => Verdict is CopyVerdict.Same;

    public static readonly CopyCheck Matched = new(CopyVerdict.Same);
}

/// <summary>
/// Whether a copy is the file it was made from —
/// <see href="https://github.com/prdb-net/prdb-ordeno/blob/main/docs/adr/0021-a-copy-is-verified-by-size-and-os-hash.md">ADR 0021</see>.
/// </summary>
/// <remarks>
/// This is the whole weight of the sequence in ADR 0002. A cross-filesystem move
/// deletes the original once this says yes, so what it does not check is what
/// can be lost — which is why the ADR states the limit rather than implying a
/// promise.
/// </remarks>
public static class CopyVerification
{
    /// <summary>
    /// Size, then <c>osHash</c>: the length and the first and last 64 KiB, a few
    /// hundred kilobytes whatever the file weighs. Below the package's minimum
    /// size there is no <c>osHash</c> at all, and 128 KiB is nothing to read, so
    /// the one case where the cheap check is unavailable is the one where the
    /// exhaustive check is free.
    /// </summary>
    public static CopyCheck Check(string source, string copy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(copy);

        var original = new FileInfo(source);
        var arrived = new FileInfo(copy);

        if (!original.Exists || !arrived.Exists)
        {
            return new CopyCheck(CopyVerdict.Unreadable, "one of the two files is not there");
        }

        if (original.Length != arrived.Length)
        {
            return new CopyCheck(
                CopyVerdict.DifferentSize,
                $"the copy is {arrived.Length} bytes where the original is {original.Length}");
        }

        if (original.Length < OsHash.MinimumFileSize)
        {
            return SameBytes(source, copy)
                ? CopyCheck.Matched
                : new CopyCheck(CopyVerdict.DifferentContent, "the copy holds different bytes");
        }

        // Computed now, on both sides, and never taken from what identification
        // stored: that reading was made before the copy, so verifying against it
        // would verify nothing that happened during it.
        if (!OsHash.TryCompute(source, out var sent) || sent is null
            || !OsHash.TryCompute(copy, out var received) || received is null)
        {
            return new CopyCheck(
                CopyVerdict.Unreadable,
                "the tool could not read one of the two files back to check them");
        }

        return string.Equals(sent, received, StringComparison.OrdinalIgnoreCase)
            ? CopyCheck.Matched
            : new CopyCheck(CopyVerdict.DifferentContent, "the copy does not match the original");
    }

    private static bool SameBytes(string source, string copy)
    {
        try
        {
            return File.ReadAllBytes(source).AsSpan().SequenceEqual(File.ReadAllBytes(copy));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

using Microsoft.Extensions.Logging.Abstractions;

using Prdb.Ordeno.Core.Identification;
using Prdb.Ordeno.Infrastructure.Identification;

using Xunit;

namespace Prdb.Ordeno.Infrastructure.Tests.Identification;

/// <summary>
/// The exact hash, against real files. What is checked here is not the hash
/// itself — ADR 0004 leaves that to the package and its test vectors — but the
/// three answers this tool has to tell apart: a hash, a file too small to have
/// one, and a file it could not read.
/// </summary>
public sealed class OsHashesTests : IDisposable
{
    private readonly TempDirectory directory = new();
    private readonly OsHashes hashes = new(NullLogger<OsHashes>.Instance);

    public void Dispose() => directory.Dispose();

    [Fact]
    public void A_file_large_enough_has_a_hash()
    {
        var path = directory.Combine("video.mkv");
        File.WriteAllBytes(path, new byte[256 * 1024]);

        var reading = hashes.OsHashOf(path);

        Assert.Equal(OsHashState.Computed, reading.State);
        Assert.NotNull(reading.Hash);

        // Sixteen lowercase hex characters. Everything local compares bytes, and
        // a hash stored in the other casing never matches — silently.
        Assert.Equal(16, reading.Hash!.Length);
        Assert.Equal(reading.Hash.ToLowerInvariant(), reading.Hash, StringComparer.Ordinal);
    }

    /// <summary>
    /// A state, not a bug. Such a file is identified by its name or not at all,
    /// and it still gets asked about.
    /// </summary>
    [Fact]
    public void A_file_under_the_minimum_has_none_and_that_is_an_answer()
    {
        var path = directory.Combine("tiny.mkv");
        File.WriteAllBytes(path, new byte[1024]);

        Assert.Equal(OsHashState.TooSmall, hashes.OsHashOf(path).State);
    }

    /// <summary>
    /// "It is not there" must not be recorded as "it has no hash": the first is
    /// worth another look and the second is final.
    /// </summary>
    [Fact]
    public void A_file_that_is_gone_is_unreadable_rather_than_too_small()
    {
        Assert.Equal(OsHashState.Unreadable, hashes.OsHashOf(directory.Combine("gone.mkv")).State);
    }
}

using System.Security.Cryptography;

using Prdb.Hashing;
using Prdb.Ordeno.Infrastructure.Library;

using Xunit;

namespace Prdb.Ordeno.Infrastructure.Tests.Library;

/// <summary>
/// The check that stands between a copy and a delete the user cannot undo
/// (ADR 0021). What it says yes to is deleted, so every case here is one where
/// the wrong answer costs somebody their video.
/// </summary>
public sealed class CopyVerificationTests : IDisposable
{
    private readonly TempDirectory temp = new();

    public void Dispose() => temp.Dispose();

    [Fact]
    public void A_copy_of_the_file_matches()
    {
        var source = Write("source.mkv", RandomNumberGenerator.GetBytes(400 * 1024));
        var copy = temp.Combine("copy.mkv");
        File.Copy(source, copy);

        Assert.True(CopyVerification.Check(source, copy).Same);
    }

    /// <summary>
    /// The commonest way a copy goes wrong — a share that filled up, a container
    /// stopped, a write that never finished.
    /// </summary>
    [Fact]
    public void A_copy_that_stopped_early_is_caught()
    {
        var content = RandomNumberGenerator.GetBytes(400 * 1024);
        var source = Write("source.mkv", content);
        var copy = Write("copy.mkv", content[..(200 * 1024)]);

        var check = CopyVerification.Check(source, copy);

        Assert.False(check.Same);
        Assert.Equal(CopyVerdict.DifferentSize, check.Verdict);
        Assert.NotNull(check.Because);
    }

    /// <summary>
    /// The right length and the wrong file. <c>osHash</c> reads the first and
    /// last 64 KiB, which is where a copy that went to the wrong place or was
    /// written over differs.
    /// </summary>
    [Fact]
    public void A_file_of_the_right_length_that_is_not_the_file_is_caught()
    {
        var source = Write("source.mkv", RandomNumberGenerator.GetBytes(400 * 1024));
        var copy = Write("copy.mkv", RandomNumberGenerator.GetBytes(400 * 1024));

        Assert.Equal(CopyVerdict.DifferentContent, CopyVerification.Check(source, copy).Verdict);
    }

    /// <summary>
    /// Below the package's minimum size there is no <c>osHash</c> to compare —
    /// <c>PackageContractTests</c> holds that contract — so these are compared
    /// byte for byte. The point of this test is that such a file is verified at
    /// all: falling back to "the sizes match" would delete an original on the
    /// strength of a length.
    /// </summary>
    [Fact]
    public void A_file_too_small_to_hash_is_compared_byte_for_byte()
    {
        var length = (int)OsHash.MinimumFileSize / 2;
        var source = Write("source.mkv", RandomNumberGenerator.GetBytes(length));
        var same = temp.Combine("same.mkv");
        File.Copy(source, same);
        var different = Write("different.mkv", RandomNumberGenerator.GetBytes(length));

        Assert.True(CopyVerification.Check(source, same).Same);
        Assert.Equal(CopyVerdict.DifferentContent, CopyVerification.Check(source, different).Verdict);
    }

    /// <summary>
    /// Nothing is claimed about a file that is not there. ADR 0021: what cannot
    /// be verified is not deleted.
    /// </summary>
    [Fact]
    public void A_file_that_is_missing_is_not_a_match()
    {
        var source = Write("source.mkv", [1, 2, 3]);

        Assert.Equal(
            CopyVerdict.Unreadable,
            CopyVerification.Check(source, temp.Combine("never-arrived.mkv")).Verdict);
    }

    private string Write(string name, byte[] content)
    {
        var path = temp.Combine(name);
        File.WriteAllBytes(path, content);

        return path;
    }
}

using System.Security.Cryptography;

using Microsoft.Data.Sqlite;

using Prdb.Hashing;
using Prdb.Sdk;

using Xunit;

namespace Prdb.Ordeno.Infrastructure.Tests;

/// <summary>
/// These guard assumptions this repository's design rests on, not the packages
/// themselves. ADR 0005 claims the net8.0 packages are consumed from net10.0
/// unchanged, and AGENTS.md builds on the hasher's contract that a file below
/// 128 KiB has no osHash — a file that quietly gained one, or a client that
/// stopped constructing, would change what the identification path may assume.
/// </summary>
public sealed class PackageContractTests
{
    [Fact]
    public void A_file_below_the_minimum_size_has_no_os_hash()
    {
        using var directory = new TempDirectory();
        var file = directory.Combine("too-small.mkv");
        File.WriteAllBytes(file, new byte[1024]);

        Assert.Null(OsHash.Compute(file));
    }

    [Fact]
    public void A_file_at_the_minimum_size_hashes_to_sixteen_hex_characters()
    {
        using var directory = new TempDirectory();
        var file = directory.Combine("just-big-enough.mkv");
        File.WriteAllBytes(file, RandomNumberGenerator.GetBytes((int)OsHash.MinimumFileSize));

        var hash = OsHash.Compute(file);

        Assert.Matches("^[0-9a-f]{16}$", hash);
    }

    [Fact]
    public void The_prdb_client_is_built_without_reaching_the_network()
    {
        var client = PrdbClientFactory.Create("not-a-real-key");

        Assert.NotNull(client.Videos);
    }

    /// <summary>
    /// SQLite arrives as a native library, and this repository overrides the one
    /// EF Core ships with (see Directory.Packages.props). That override is only
    /// sound if the replacement actually loads and answers — on every
    /// architecture the image is built for, ADR 0011's linux/arm64 included.
    /// </summary>
    [Fact]
    public void Sqlite_opens_and_answers_on_this_platform()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "select sqlite_version();";

        Assert.NotNull(command.ExecuteScalar() as string);
    }
}

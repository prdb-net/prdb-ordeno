using Microsoft.Data.Sqlite;

namespace Prdb.Ordeno.Infrastructure.Persistence;

/// <summary>
/// Where the database file lives and how it is opened.
/// </summary>
/// <remarks>
/// ADR 0007 puts the state in a SQLite file in the mounted data volume, and
/// ADR 0009 makes that directory one of the few things the container
/// environment owns — so it arrives from outside rather than being discovered.
/// </remarks>
public sealed class OrdenoDatabaseLocation
{
    public const string FileName = "ordeno.db";

    public OrdenoDatabaseLocation(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        DirectoryPath = Path.GetFullPath(dataDirectory);
        FilePath = Path.Combine(DirectoryPath, FileName);

        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = FilePath,
            // SQLite takes one writer at a time (ADR 0007). Microsoft.Data.Sqlite
            // retries a busy database until this timeout runs out, so a scan that
            // writes while another request wants to means waiting rather than an
            // error the user has to understand.
            DefaultTimeout = 30,
        }.ConnectionString;
    }

    public string DirectoryPath { get; }

    public string FilePath { get; }

    public string ConnectionString { get; }

    /// <summary>
    /// Creates the data directory if it is not there yet. A user who mounted a
    /// volume has one; a user who is trying the tool out on a laptop may not.
    /// </summary>
    public void EnsureDirectoryExists() => Directory.CreateDirectory(DirectoryPath);
}

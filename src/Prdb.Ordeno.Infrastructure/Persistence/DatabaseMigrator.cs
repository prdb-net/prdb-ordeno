using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Prdb.Ordeno.Infrastructure.Persistence;

/// <summary>
/// Brings the database to the schema this build expects, at startup, before
/// anything is served (ADR 0007).
/// </summary>
public sealed class DatabaseMigrator(
    OrdenoDbContext context,
    OrdenoDatabaseLocation location,
    ILogger<DatabaseMigrator> logger)
{
    public async Task PrepareAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            location.EnsureDirectoryExists();

            var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
            if (pending.Length > 0)
            {
                logger.LogInformation(
                    "Applying {Count} migration(s) to {Database}: {Migrations}.",
                    pending.Length,
                    location.FilePath,
                    string.Join(", ", pending));
            }

            await context.Database.MigrateAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogCritical(
                exception,
                "The database at {Database} could not be migrated. The tool stops here rather "
                + "than running against a schema it does not understand.",
                location.FilePath);

            throw new DatabaseMigrationException(
                $"The database at {location.FilePath} could not be migrated.",
                exception);
        }

        await EnableWriteAheadLoggingAsync(cancellationToken);
    }

    /// <summary>
    /// Write-ahead logging lets a reader work while a writer holds the single
    /// write slot SQLite allows. It is a property of the file, so asking once is
    /// enough — but it is also the thing some network filesystems refuse, and a
    /// NAS user's data volume may well be one of those. That is worth a warning
    /// and not worth refusing to start over.
    /// </summary>
    private async Task EnableWriteAheadLoggingAsync(CancellationToken cancellationToken)
    {
        // A pragma belongs to a connection rather than to a query, so it is asked
        // directly instead of through the context.
        await using var connection = new SqliteConnection(location.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL;";

        var mode = await command.ExecuteScalarAsync(cancellationToken) as string;

        if (!string.Equals(mode, "wal", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "The database at {Database} runs in journal mode {Mode} rather than WAL. Some "
                + "network filesystems refuse it; expect readers to wait while something writes.",
                location.FilePath,
                mode);
        }
    }
}

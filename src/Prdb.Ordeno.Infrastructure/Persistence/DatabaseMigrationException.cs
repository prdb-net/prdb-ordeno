namespace Prdb.Ordeno.Infrastructure.Persistence;

/// <summary>
/// The database could not be brought to the schema this build expects. ADR 0007:
/// that stops the tool. It does not continue against a schema it does not
/// understand, because the user cannot be expected to restore this database and
/// a half-migrated one is worse than a container that refuses to start.
/// </summary>
public sealed class DatabaseMigrationException(string message, Exception innerException)
    : Exception(message, innerException);

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Prdb.Ordeno.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Ordeno.Infrastructure.Tests.Persistence;

/// <summary>
/// Against a real SQLite file in a temporary directory, because the failures
/// worth catching here — a directory that is not there, a file that is not a
/// database, a second start over an existing one — are the ones an in-memory
/// provider cannot have.
/// </summary>
public sealed class DatabaseMigratorTests
{
    [Fact]
    public async Task An_empty_data_directory_becomes_a_database()
    {
        using var directory = new TempDirectory();
        var dataDirectory = directory.Combine("data");

        await PrepareAsync(dataDirectory);

        var location = new OrdenoDatabaseLocation(dataDirectory);
        Assert.True(File.Exists(location.FilePath));

        await using var services = Services(dataDirectory);
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdenoDbContext>();

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());

        var configuration = await context.Configuration.SingleAsync();
        Assert.Equal(StoredConfiguration.SingletonId, configuration.Id);
        Assert.Null(configuration.PrdbApiKey);
        Assert.Null(configuration.OnboardingCompletedAt);
    }

    [Fact]
    public async Task The_database_runs_in_write_ahead_logging_mode()
    {
        using var directory = new TempDirectory();
        var dataDirectory = directory.Combine("data");

        await PrepareAsync(dataDirectory);

        using var connection = new SqliteConnection(new OrdenoDatabaseLocation(dataDirectory).ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";

        Assert.Equal("wal", command.ExecuteScalar() as string);
    }

    [Fact]
    public async Task Starting_again_over_an_existing_database_changes_nothing()
    {
        using var directory = new TempDirectory();
        var dataDirectory = directory.Combine("data");

        await PrepareAsync(dataDirectory);

        await using (var services = Services(dataDirectory))
        await using (var scope = services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<OrdenoDbContext>();
            context.SourceDirectories.Add(new SourceDirectory
            {
                Path = "/downloads",
                AddedAt = DateTimeOffset.UtcNow,
            });

            await context.SaveChangesAsync();
        }

        await PrepareAsync(dataDirectory);

        await using var restarted = Services(dataDirectory);
        await using var restartedScope = restarted.CreateAsyncScope();
        var reopened = restartedScope.ServiceProvider.GetRequiredService<OrdenoDbContext>();

        var source = await reopened.SourceDirectories.SingleAsync();
        Assert.Equal("/downloads", source.Path);
    }

    [Fact]
    public async Task A_database_that_cannot_be_migrated_stops_the_tool()
    {
        using var directory = new TempDirectory();
        var dataDirectory = directory.Combine("data");
        Directory.CreateDirectory(dataDirectory);

        // Whatever this is, it is not a database. A tool that carried on here
        // would be writing into something it does not understand.
        await File.WriteAllTextAsync(
            new OrdenoDatabaseLocation(dataDirectory).FilePath,
            "this is not a database");

        var failure = await Assert.ThrowsAsync<DatabaseMigrationException>(() => PrepareAsync(dataDirectory));

        Assert.Contains(OrdenoDatabaseLocation.FileName, failure.Message, StringComparison.Ordinal);
        Assert.NotNull(failure.InnerException);
    }

    [Fact]
    public async Task The_configuration_is_a_single_row()
    {
        using var directory = new TempDirectory();
        var dataDirectory = directory.Combine("data");

        await PrepareAsync(dataDirectory);

        await using var services = Services(dataDirectory);
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdenoDbContext>();

        context.Configuration.Add(new StoredConfiguration { Id = 2 });

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    private static async Task PrepareAsync(string dataDirectory)
    {
        await using var services = Services(dataDirectory);

        await services.PrepareOrdenoDatabaseAsync();
    }

    private static ServiceProvider Services(string dataDirectory)
    {
        var services = new ServiceCollection();

        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        services.AddOrdenoPersistence(dataDirectory);

        return services.BuildServiceProvider();
    }
}

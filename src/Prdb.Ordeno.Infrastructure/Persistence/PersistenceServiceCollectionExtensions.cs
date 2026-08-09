using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Prdb.Ordeno.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers the database in <paramref name="dataDirectory"/> — the mounted
    /// volume the container environment names (ADR 0009).
    /// </summary>
    public static IServiceCollection AddOrdenoPersistence(
        this IServiceCollection services,
        string dataDirectory)
    {
        var location = new OrdenoDatabaseLocation(dataDirectory);

        services.AddSingleton(location);
        services.AddDbContext<OrdenoDbContext>(options => options.UseSqlite(location.ConnectionString));
        services.AddScoped<DatabaseMigrator>();

        return services;
    }

    /// <summary>
    /// Applies the migrations. Called at startup, before anything is served;
    /// throws <see cref="DatabaseMigrationException"/> when the database cannot
    /// be brought up to date, which stops the process.
    /// </summary>
    public static async Task PrepareOrdenoDatabaseAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();

        await scope.ServiceProvider
            .GetRequiredService<DatabaseMigrator>()
            .PrepareAsync(cancellationToken);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Prdb.Ordeno.Infrastructure.Persistence;

/// <summary>
/// Used by <c>dotnet ef</c> when generating migrations, and by nothing else. It
/// exists so that scaffolding a migration does not depend on the application
/// starting, which at runtime needs a data directory that only the container
/// provides (ADR 0009). The path below is never opened.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<OrdenoDbContext>
{
    public OrdenoDbContext CreateDbContext(string[] args)
    {
        var location = new OrdenoDatabaseLocation(Path.Combine(Path.GetTempPath(), "prdb-ordeno-design-time"));

        var options = new DbContextOptionsBuilder<OrdenoDbContext>()
            .UseSqlite(location.ConnectionString)
            .Options;

        return new OrdenoDbContext(options);
    }
}

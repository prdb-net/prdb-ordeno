using Microsoft.EntityFrameworkCore;

namespace Prdb.Ordeno.Infrastructure.Persistence;

/// <summary>
/// Everything the tool remembers locally. It is not, and never becomes, a copy
/// of prdb's corpus — ADR 0001 keeps the lookups remote and ADR 0007 keeps this
/// store to what happened here.
/// </summary>
public sealed class OrdenoDbContext(DbContextOptions<OrdenoDbContext> options) : DbContext(options)
{
    public DbSet<StoredConfiguration> Configuration => Set<StoredConfiguration>();

    public DbSet<SourceDirectory> SourceDirectories => Set<SourceDirectory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StoredConfiguration>(configuration =>
        {
            configuration.ToTable(
                "Configuration",
                table => table.HasCheckConstraint(
                    "CK_Configuration_SingleRow",
                    $"\"Id\" = {StoredConfiguration.SingletonId}"));

            configuration.HasKey(row => row.Id);
            configuration.Property(row => row.Id).ValueGeneratedNever();
            configuration.Property(row => row.Layout).HasMaxLength(64);

            // The row exists from the first migration, so reading the
            // configuration is never "is there a row yet" — it is one query with
            // one answer, whose fields happen to be empty until onboarding runs.
            configuration.HasData(new StoredConfiguration { Id = StoredConfiguration.SingletonId });
        });

        modelBuilder.Entity<SourceDirectory>(source =>
        {
            source.HasKey(row => row.Id);
            source.Property(row => row.Path).IsRequired();
            source.HasIndex(row => row.Path).IsUnique();
        });
    }
}

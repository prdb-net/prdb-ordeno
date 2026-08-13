using Microsoft.EntityFrameworkCore;

using Prdb.Ordeno.Infrastructure.Access;

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

    public DbSet<DiscoveredFile> DiscoveredFiles => Set<DiscoveredFile>();

    public DbSet<Session> Sessions => Set<Session>();

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

        modelBuilder.Entity<DiscoveredFile>(file =>
        {
            file.HasKey(row => row.Id);
            file.Property(row => row.Path).IsRequired();

            // Stored as plain UTC rather than as an offset. SQLite has no date
            // type, and the provider refuses to put a DateTimeOffset on either
            // side of a comparison — which is precisely what a scan does with
            // these: "not seen by this scan" and "unchanged for long enough" are
            // both one query rather than a table read into memory. Everything
            // here is UTC anyway, so the offset carried no information.
            foreach (var timestamp in new[]
                     {
                         nameof(DiscoveredFile.LastWriteAt),
                         nameof(DiscoveredFile.FirstSeenAt),
                         nameof(DiscoveredFile.LastSeenAt),
                         nameof(DiscoveredFile.UnchangedSince),
                     })
            {
                file.Property<DateTimeOffset>(timestamp).HasConversion(
                    value => value.UtcDateTime,
                    stored => new DateTimeOffset(stored, TimeSpan.Zero));
            }

            // One path is one file, whichever directory it turned up under.
            file.HasIndex(row => row.Path).IsUnique();

            // A scan ends by deleting the rows it did not see, and the read model
            // counts by source; both go through this.
            file.HasIndex(row => row.SourceDirectoryId);

            // Unwatching a directory takes its files with it, in the schema
            // rather than in the code that happens to do the deleting — the
            // configuration removes a source with one statement and never loads
            // the rows this would otherwise leave behind.
            file.HasOne<SourceDirectory>()
                .WithMany()
                .HasForeignKey(row => row.SourceDirectoryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Session>(session =>
        {
            session.HasKey(row => row.Id);
            session.Property(row => row.TokenHash).IsRequired().HasMaxLength(64);

            // Every authenticated request looks a session up by this.
            session.HasIndex(row => row.TokenHash).IsUnique();
        });
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

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

    public DbSet<FileIdentification> FileIdentifications => Set<FileIdentification>();

    public DbSet<Session> Sessions => Set<Session>();

    /// <summary>
    /// Stored as plain UTC rather than as an offset. SQLite has no date type, and
    /// the provider refuses to put a <see cref="DateTimeOffset"/> on either side
    /// of a comparison — which is precisely what a scan and an identification run
    /// do with these: "not seen by this scan", "unchanged for long enough" and
    /// "not asked about yet" are each one query rather than a table read into
    /// memory. Everything here is UTC anyway, so the offset carried no
    /// information.
    /// </summary>
    private static readonly ValueConverter<DateTimeOffset, DateTime> UtcTimestamp = new(
        value => value.UtcDateTime,
        stored => new DateTimeOffset(stored, TimeSpan.Zero));

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

            file.Property(row => row.LastWriteAt).HasConversion(UtcTimestamp);
            file.Property(row => row.FirstSeenAt).HasConversion(UtcTimestamp);
            file.Property(row => row.LastSeenAt).HasConversion(UtcTimestamp);
            file.Property(row => row.UnchangedSince).HasConversion(UtcTimestamp);

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

            // As the name rather than as a number, so that reading the table
            // with a database browser answers the question instead of raising
            // one. The same reason the layout column carries "Jellyfin".
            file.Property(row => row.PerceptualHashState).HasConversion<string>().HasMaxLength(32);

            // A converter for the non-nullable type serves the nullable property:
            // EF answers null with null and never reaches it.
            file.Property(row => row.PerceptualHashAt).HasConversion(UtcTimestamp);
        });

        modelBuilder.Entity<FileIdentification>(identification =>
        {
            identification.HasKey(row => row.Id);
            identification.Property(row => row.Confidence).HasConversion<string>().HasMaxLength(32);
            identification.Property(row => row.MatchedBy).HasConversion<string>().HasMaxLength(32);

            identification.Property(row => row.AskedAt).HasConversion(UtcTimestamp);

            // One answer per file. A new one replaces it rather than joining it:
            // two claims about one file, with no way to tell which is current, is
            // the state this tool must never be in.
            identification.HasIndex(row => row.DiscoveredFileId).IsUnique();

            identification.HasOne<DiscoveredFile>()
                .WithOne()
                .HasForeignKey<FileIdentification>(row => row.DiscoveredFileId)
                .OnDelete(DeleteBehavior.Cascade);

            identification.HasMany(row => row.Candidates)
                .WithOne()
                .HasForeignKey(row => row.FileIdentificationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<IdentificationCandidate>(candidate =>
        {
            // Named here because nothing exposes it as a set: it is reached
            // through the identification it belongs to and nowhere else.
            candidate.ToTable("IdentificationCandidates");
            candidate.HasKey(row => row.Id);
            candidate.HasIndex(row => row.FileIdentificationId);
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

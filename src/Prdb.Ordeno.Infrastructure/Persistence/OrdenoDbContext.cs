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

    public DbSet<FileResolution> FileResolutions => Set<FileResolution>();

    public DbSet<FiledVideo> FiledVideos => Set<FiledVideo>();

    public DbSet<FileHold> FileHolds => Set<FileHold>();

    public DbSet<OperationRun> OperationRuns => Set<OperationRun>();

    public DbSet<OperationEntry> Operations => Set<OperationEntry>();

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

            candidate.Property(row => row.DescribedAt).HasConversion(UtcTimestamp);
        });

        modelBuilder.Entity<FileResolution>(resolution =>
        {
            resolution.HasKey(row => row.Id);
            resolution.Property(row => row.Kind).HasConversion<string>().HasMaxLength(32);
            resolution.Property(row => row.From).HasConversion<string>().HasMaxLength(32);

            resolution.Property(row => row.DecidedAt).HasConversion(UtcTimestamp);

            // One decision per file. A second one replaces it — two answers from
            // one person about one file, with no way to tell which is current, is
            // the same state two identifications would be.
            resolution.HasIndex(row => row.DiscoveredFileId).IsUnique();

            // It goes with the file, in the schema: a row deciding the fate of a
            // file that is no longer in any download directory decides nothing.
            resolution.HasOne<DiscoveredFile>()
                .WithOne()
                .HasForeignKey<FileResolution>(row => row.DiscoveredFileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FiledVideo>(filed =>
        {
            filed.HasKey(row => row.Id);
            filed.Property(row => row.LibraryRoot).IsRequired();
            filed.Property(row => row.Directory).IsRequired();
            filed.Property(row => row.FileName).IsRequired();
            filed.Property(row => row.QualityLabel).IsRequired().HasMaxLength(16);

            filed.Property(row => row.FiledAt).HasConversion(UtcTimestamp);
            filed.Property(row => row.MetadataCheckedAt).HasConversion(UtcTimestamp);

            // What every filing asks: is this scene already in this library, and
            // at which qualities. Not unique — that is the whole point of
            // ADR 0003, which keeps a 1080p and a 2160p copy side by side.
            filed.HasIndex(row => new { row.VideoId, row.LibraryRoot });

            // One file is one row. Two rows claiming one path would be two
            // answers to "what is at this name", with no way to tell which is
            // current — the state this tool must never be in about a file it
            // has moved.
            filed.HasIndex(row => new { row.Directory, row.FileName }).IsUnique();

            // What a refresh asks: which scenes of this library have gone longest
            // without being checked against what prdb says (ADR 0032). The
            // library root comes first because that is the equality, and a row
            // filed under a root the tool is no longer pointed at says nothing
            // about the one it is.
            filed.HasIndex(row => new { row.LibraryRoot, row.MetadataCheckedAt });
        });

        modelBuilder.Entity<FileHold>(hold =>
        {
            hold.HasKey(row => row.Id);
            hold.Property(row => row.Path).IsRequired();
            hold.Property(row => row.FiledTo).IsRequired();

            hold.Property(row => row.FiledAt).HasConversion(UtcTimestamp);
            hold.Property(row => row.HeldAt).HasConversion(UtcTimestamp);

            // One file is one hold. A second undo returning a file to a path that
            // is already held replaces what is there, and two rows about one path
            // would be two answers to "why is this file not being filed".
            hold.HasIndex(row => row.Path).IsUnique();
        });

        modelBuilder.Entity<OperationRun>(run =>
        {
            run.HasKey(row => row.Id);

            run.Property(row => row.Kind).HasConversion<string>().HasMaxLength(32);
            run.Property(row => row.AskedBy).HasConversion<string>().HasMaxLength(32);

            run.Property(row => row.StartedAt).HasConversion(UtcTimestamp);
            run.Property(row => row.FinishedAt).HasConversion(UtcTimestamp);

            // The screen reads the log newest first, and the trim reads it
            // oldest first. One index, both directions.
            run.HasIndex(row => row.StartedAt);
        });

        modelBuilder.Entity<OperationEntry>(operation =>
        {
            operation.ToTable("Operations");

            operation.HasKey(row => row.Id);
            operation.Property(row => row.FromPath).IsRequired();
            operation.Property(row => row.ToPath).IsRequired();
            operation.Property(row => row.QualityLabel).HasMaxLength(16);
            operation.Property(row => row.OsHash).HasMaxLength(64);
            operation.Property(row => row.ArtworkFingerprint).HasMaxLength(64);

            // As names rather than as numbers, the way every other state is
            // stored: reading this table with a database browser is one of the
            // three jobs it has.
            operation.Property(row => row.Kind).HasConversion<string>().HasMaxLength(32);
            operation.Property(row => row.Movement).HasConversion<string>().HasMaxLength(32);
            operation.Property(row => row.DecidedBy).HasConversion<string>().HasMaxLength(32);
            operation.Property(row => row.Confidence).HasConversion<string>().HasMaxLength(32);
            operation.Property(row => row.MatchedBy).HasConversion<string>().HasMaxLength(32);

            operation.Property(row => row.At).HasConversion(UtcTimestamp);
            operation.Property(row => row.DecidedAt).HasConversion(UtcTimestamp);
            operation.Property(row => row.UndoneAt).HasConversion(UtcTimestamp);

            // Every read is "the entries of this run", in the order they
            // happened — and an undo reads the same index backwards.
            operation.HasIndex(row => row.RunId);

            // And the one question an undo asks of the whole table: has a later
            // run taken this name. Reverse chronological order is the rule at
            // both scales (ADR 0029), and this is how it is enforced rather than
            // hoped for.
            operation.HasIndex(row => row.FromPath);

            // The entries go with the run, in the schema rather than in whatever
            // code happens to do the deleting: the trim removes whole runs
            // (ADR 0028), and half a run in the log is the one state worth
            // ruling out.
            operation.HasOne<OperationRun>()
                .WithMany()
                .HasForeignKey(row => row.RunId)
                .OnDelete(DeleteBehavior.Cascade);

            // The undo run that reversed it, on the other hand, is only a
            // reference: it is trimmed on its own schedule, and losing it must
            // not take the entry with it.
            operation.HasOne<OperationRun>()
                .WithMany()
                .HasForeignKey(row => row.UndoneByRunId)
                .OnDelete(DeleteBehavior.SetNull);
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

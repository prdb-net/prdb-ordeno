using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Ordeno.Core.Configuration;
using Prdb.Ordeno.Core.MediaServer;
using Prdb.Ordeno.Infrastructure.MediaServer;
using Prdb.Ordeno.Infrastructure.Persistence;

namespace Prdb.Ordeno.Infrastructure.Configuration;

/// <summary>
/// What onboarding collects and where it is kept (ADR 0009). The same service
/// serves the settings afterwards: there is one configuration, and the guided
/// path is a way of filling it in for the first time rather than a separate
/// thing with its own storage.
/// </summary>
/// <remarks>
/// Nothing is stored before it has been checked — the API key against prdb, the
/// directories against the filesystem the container can actually see — because a
/// setting that is wrong and saved is discovered by the unattended run at three
/// in the morning, and one that is wrong and refused is discovered by the person
/// who typed it.
/// </remarks>
public sealed class ConfigurationService(
    OrdenoDbContext context,
    IDirectoryInspector inspector,
    IPrdbApiKeyCheck apiKeyCheck,
    MediaServerService mediaServer,
    TimeProvider time,
    ILogger<ConfigurationService> logger)
{
    public async Task<OrdenoConfiguration> ReadAsync(CancellationToken cancellationToken = default) =>
        await BuildAsync(cancellationToken);

    /// <summary>
    /// Checks the key against prdb and stores it only if prdb accepted it. A key
    /// that could not be checked because prdb was unreachable is not stored
    /// either: saving it would be the tool claiming something it does not know.
    /// </summary>
    public async Task<ConfigurationChange> SetApiKeyAsync(
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        var check = await apiKeyCheck.CheckAsync(apiKey, cancellationToken);
        if (!check.Accepted)
        {
            return ConfigurationChange.Refused(
                await BuildAsync(cancellationToken),
                check.Message ?? "prdb did not accept this key.");
        }

        var configuration = await SingleConfigurationAsync(cancellationToken);
        configuration.PrdbApiKey = apiKey.Trim();

        await context.SaveChangesAsync(cancellationToken);

        return ConfigurationChange.Made(await BuildAsync(cancellationToken));
    }

    /// <summary>
    /// Adds a directory to watch. There can be several — downloads arrive
    /// wherever the download client was told to put them.
    /// </summary>
    public async Task<ConfigurationChange> AddSourceAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var inspection = inspector.Inspect(path, DirectoryRole.Source);
        if (!inspection.Usable)
        {
            return ConfigurationChange.Refused(await BuildAsync(cancellationToken), inspection.Message!);
        }

        var configuration = await SingleConfigurationAsync(cancellationToken);
        var existing = await context.SourceDirectories.ToListAsync(cancellationToken);

        if (existing.Any(source => IsTheSamePlace(source.Path, inspection.Path)))
        {
            return ConfigurationChange.Refused(
                await BuildAsync(cancellationToken),
                $"{inspection.Path} is already being watched.");
        }

        if (configuration.TargetDirectory is { } target && Overlaps(inspection.Path, target))
        {
            return ConfigurationChange.Refused(
                await BuildAsync(cancellationToken),
                $"{inspection.Path} and the library directory {target} are inside one another. "
                + "The tool would find what it had just filed and file it again — keep the "
                + "downloads and the library apart.");
        }

        context.SourceDirectories.Add(new SourceDirectory
        {
            Path = inspection.Path,
            AddedAt = time.GetUtcNow(),
        });

        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation("A source directory was added.");

        return ConfigurationChange.Made(await BuildAsync(cancellationToken));
    }

    /// <summary>
    /// Stops watching a directory. Removing one that is not there is not an
    /// error; the answer is the same either way, which is the configuration.
    /// </summary>
    public async Task<ConfigurationChange> RemoveSourceAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var removed = await context.SourceDirectories
            .Where(source => source.Id == id)
            .ExecuteDeleteAsync(cancellationToken);

        if (removed > 0)
        {
            logger.LogInformation("A source directory was removed.");
        }

        return ConfigurationChange.Made(await BuildAsync(cancellationToken));
    }

    /// <summary>
    /// Sets where the library lives and which media server reads it. The two
    /// belong together: a directory is only the right one for a layout somebody
    /// picked.
    /// </summary>
    public async Task<ConfigurationChange> SetTargetAsync(
        string path,
        string? layoutName,
        CancellationToken cancellationToken = default)
    {
        if (LibraryLayouts.Parse(layoutName) is not { } layout)
        {
            var known = string.Join(", ", LibraryLayouts.All.Select(choice => choice.Name));

            return ConfigurationChange.Refused(
                await BuildAsync(cancellationToken),
                $"'{layoutName}' is not a layout this release knows. Choose one of: {known}.");
        }

        var inspection = inspector.Inspect(path, DirectoryRole.Target);
        if (!inspection.Usable)
        {
            return ConfigurationChange.Refused(await BuildAsync(cancellationToken), inspection.Message!);
        }

        var sources = await context.SourceDirectories.ToListAsync(cancellationToken);
        if (sources.FirstOrDefault(source => Overlaps(source.Path, inspection.Path)) is { } clashing)
        {
            return ConfigurationChange.Refused(
                await BuildAsync(cancellationToken),
                $"{inspection.Path} and the download directory {clashing.Path} are inside one "
                + "another. The tool would find what it had just filed and file it again — keep "
                + "the downloads and the library apart.");
        }

        var configuration = await SingleConfigurationAsync(cancellationToken);
        configuration.TargetDirectory = inspection.Path;
        configuration.Layout = LibraryLayouts.NameOf(layout);

        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation("The library directory was set, in the {Layout} layout.", configuration.Layout);

        return ConfigurationChange.Made(await BuildAsync(cancellationToken));
    }

    /// <summary>
    /// Stores where the media server is and the key that gets in — the two
    /// optional fields of ADR 0018 — and only after the server has answered for
    /// them.
    /// </summary>
    /// <remarks>
    /// The check does more than reach the server: it reads back the one setting
    /// that would silently discard every date the tool writes, and it looks for
    /// something the tool has filed. Neither of those refuses the change. What
    /// refuses it is the same pair as everywhere else — an address that is not
    /// one, and a server that answered "no" or did not answer at all.
    /// </remarks>
    public async Task<MediaServerChange> SetMediaServerAsync(
        string? url,
        string? apiKey,
        CancellationToken cancellationToken = default)
    {
        if (MediaServerConnection.From(url, apiKey, out var problem) is not { } connection)
        {
            return MediaServerChange.Refused(await BuildAsync(cancellationToken), problem!);
        }

        var check = await mediaServer.CheckAsync(connection, cancellationToken);

        if (!check.Answered)
        {
            return MediaServerChange.Refused(await BuildAsync(cancellationToken), check.Message, check);
        }

        var configuration = await SingleConfigurationAsync(cancellationToken);
        configuration.MediaServerUrl = connection.Address;
        configuration.MediaServerApiKey = connection.ApiKey;

        await context.SaveChangesAsync(cancellationToken);

        // The address, never the key. Both halves of that are the rule in
        // ADR 0009, applied to the second credential the tool now holds.
        logger.LogInformation("A media server connection was stored for {Address}.", connection.Address);

        return MediaServerChange.Made(await BuildAsync(cancellationToken), check);
    }

    /// <summary>
    /// Asks the stored connection the same questions again. Worth its own call
    /// because two of the three answers change without anybody touching this
    /// tool: a key can be revoked, and a library can be pointed somewhere else.
    /// </summary>
    public async Task<MediaServerChange> CheckMediaServerAsync(CancellationToken cancellationToken = default)
    {
        if (await mediaServer.ConnectionAsync(cancellationToken) is not { } connection)
        {
            return MediaServerChange.Refused(
                await BuildAsync(cancellationToken),
                "No media server connection is stored, so there was nothing to test. That is a "
                + "complete setup — the tool files and writes its metadata files either way.");
        }

        var check = await mediaServer.CheckAsync(connection, cancellationToken);

        return check.Answered
            ? MediaServerChange.Made(await BuildAsync(cancellationToken), check)
            : MediaServerChange.Refused(await BuildAsync(cancellationToken), check.Message, check);
    }

    /// <summary>
    /// Forgets the connection, address and key together. It is how a user goes
    /// back to the state everything else is built for, so it is not an error and
    /// nothing else changes with it.
    /// </summary>
    public async Task<ConfigurationChange> ForgetMediaServerAsync(CancellationToken cancellationToken = default)
    {
        var configuration = await SingleConfigurationAsync(cancellationToken);
        var had = configuration.MediaServerUrl is not null;

        configuration.MediaServerUrl = null;
        configuration.MediaServerApiKey = null;

        await context.SaveChangesAsync(cancellationToken);

        if (had)
        {
            logger.LogInformation("The media server connection was forgotten.");
        }

        return ConfigurationChange.Made(await BuildAsync(cancellationToken));
    }

    /// <summary>
    /// Ends the guided path. It is refused while anything is missing or broken,
    /// because finishing is what tells the rest of the tool it may start —
    /// ADR 0009.
    /// </summary>
    public async Task<ConfigurationChange> CompleteOnboardingAsync(CancellationToken cancellationToken = default)
    {
        var current = await BuildAsync(cancellationToken);
        if (!current.ReadyToComplete)
        {
            return ConfigurationChange.Refused(current, current.WhatHappensNext);
        }

        if (current.Complete)
        {
            return ConfigurationChange.Made(current);
        }

        var configuration = await SingleConfigurationAsync(cancellationToken);
        configuration.OnboardingCompletedAt = time.GetUtcNow();

        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Onboarding was completed.");

        return ConfigurationChange.Made(await BuildAsync(cancellationToken));
    }

    /// <summary>
    /// The configuration as it stands, with every path looked at again. Stored
    /// answers are what the user typed; the inspections are what is true now,
    /// and a volume that failed to mount on the last restart is the difference
    /// between the two.
    /// </summary>
    private async Task<OrdenoConfiguration> BuildAsync(CancellationToken cancellationToken)
    {
        var configuration = await context.Configuration.AsNoTracking().SingleAsync(cancellationToken);

        var stored = await context.SourceDirectories
            .AsNoTracking()
            .OrderBy(source => source.Id)
            .ToListAsync(cancellationToken);

        var target = configuration.TargetDirectory is { } targetPath
            ? inspector.Inspect(targetPath, DirectoryRole.Target)
            : null;

        var sources = stored
            .Select(source =>
            {
                var inspection = inspector.Inspect(source.Path, DirectoryRole.Source);

                // Comparing two directories only says something while both are
                // there; anything else would be a promise about a path nobody
                // can reach.
                var movement = inspection.Usable && target is { Usable: true }
                    ? inspector.MovementBetween(inspection.Path, target.Path)
                    : FileMovement.Unknown;

                return new ConfiguredSource(source.Id, inspection, movement);
            })
            .ToList();

        return new OrdenoConfiguration(
            ApiKeySet: !string.IsNullOrWhiteSpace(configuration.PrdbApiKey),
            Sources: sources,
            Target: target,
            Layout: LibraryLayouts.Parse(configuration.Layout),
            MediaServerUrl: string.IsNullOrWhiteSpace(configuration.MediaServerApiKey)
                ? null
                : configuration.MediaServerUrl,
            OnboardingCompletedAt: configuration.OnboardingCompletedAt);
    }

    /// <summary>
    /// Two paths that are the same directory, or one inside the other. Filing
    /// into a directory the tool watches would make it find its own work.
    /// </summary>
    private static bool Overlaps(string left, string right) =>
        IsTheSamePlace(left, right) || IsInside(left, right) || IsInside(right, left);

    private static bool IsTheSamePlace(string left, string right) =>
        string.Equals(Normalise(left), Normalise(right), StringComparison.Ordinal);

    private static bool IsInside(string path, string maybeParent) =>
        Normalise(path).StartsWith(Normalise(maybeParent) + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    private static string Normalise(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));

    private async Task<StoredConfiguration> SingleConfigurationAsync(CancellationToken cancellationToken) =>
        await context.Configuration.SingleAsync(cancellationToken);
}

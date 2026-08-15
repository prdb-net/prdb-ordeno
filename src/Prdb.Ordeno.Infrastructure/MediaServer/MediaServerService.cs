using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Ordeno.Core.MediaServer;
using Prdb.Ordeno.Infrastructure.Persistence;

namespace Prdb.Ordeno.Infrastructure.MediaServer;

/// <summary>What came of telling the media server about what was just filed.</summary>
/// <param name="Told">How many items were refreshed.</param>
/// <param name="Missed">
/// How many filed videos the server does not hold. Not an error on its own — it
/// has not scanned yet — but all of them missing is the state the connection
/// test exists to name.
/// </param>
public sealed record MediaServerRefresh(int Told, int Missed, string? Problem = null)
{
    public static readonly MediaServerRefresh NotConfigured = new(0, 0);
}

/// <summary>
/// The optional half of a media server: the connection ADR 0018 allows and
/// nothing requires.
/// </summary>
/// <remarks>
/// <para>
/// Two callers, and the difference between them is the whole design. The
/// connection test is run by somebody standing in front of a form, so it fails
/// out loud and in detail. <see cref="RefreshAsync"/> is run after a filing run
/// has already finished and been reported, so it fails into the log and changes
/// nothing about the filing — a server that is down, moved or answering with a
/// stale key must never turn into a file that did not get filed.
/// </para>
/// <para>
/// Which client this is holds no decision yet: one layout ships (ADR 0008), so
/// one client is registered. A second media server arrives as a second
/// <see cref="IMediaServerClient"/> chosen by the configured layout — and only
/// once somebody has measured it, the same rule the sidecar writer follows.
/// </para>
/// </remarks>
public sealed class MediaServerService(
    OrdenoDbContext context,
    IMediaServerClient client,
    ILogger<MediaServerService> logger)
{
    /// <summary>
    /// How many filed videos the connection test looks for. One match is the
    /// whole proof, and the rest are there because the most recent thing filed
    /// may be the one the server has not scanned yet.
    /// </summary>
    private const int Sample = 50;

    /// <summary>
    /// The stored connection, or <c>null</c> because there is none — which is
    /// the ordinary state and never an error.
    /// </summary>
    public async Task<MediaServerConnection?> ConnectionAsync(CancellationToken cancellationToken = default)
    {
        var configuration = await context.Configuration.AsNoTracking().SingleAsync(cancellationToken);

        return MediaServerConnection.From(
            configuration.MediaServerUrl,
            configuration.MediaServerApiKey,
            out _);
    }

    /// <summary>
    /// Asks the server what it is, what it does with dates, and whether it holds
    /// anything this tool filed. The third question is the one worth asking: a
    /// server that answers but matches nothing looks fine and does nothing.
    /// </summary>
    public async Task<MediaServerCheck> CheckAsync(
        MediaServerConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var facts = await client.ExamineAsync(connection, cancellationToken);

        if (!facts.Answered)
        {
            return Failed(facts.Reach, facts.Problem);
        }

        var configuration = await context.Configuration.AsNoTracking().SingleAsync(cancellationToken);
        var libraryRoot = configuration.TargetDirectory;

        var filed = await FiledPathsAsync(libraryRoot, cancellationToken);
        var items = await client.ItemsAsync(connection, cancellationToken);

        if (!items.Answered)
        {
            return Failed(items.Reach, items.Problem);
        }

        var held = items.Value!.Count;

        foreach (var path in filed)
        {
            if (LibraryTail.Of(libraryRoot!, path) is not { } tail)
            {
                continue;
            }

            if (LibraryTail.Match(items.Value, tail) is [var found, ..])
            {
                return MediaServerCheck.Of(
                    facts.Value!,
                    new MediaServerMatch(
                        held,
                        filed.Count,
                        Path.GetFileName(path),
                        LibraryTail.Substitution(found, tail)),
                    libraryRoot!);
            }
        }

        return MediaServerCheck.Of(
            facts.Value!,
            new MediaServerMatch(held, filed.Count),
            libraryRoot ?? "the library directory");
    }

    /// <summary>
    /// Tells the server to read the given files again, so that a sidecar written
    /// a moment ago is visible without waiting for a scan.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One enumeration for the whole batch and one refresh per item found. The
    /// enumeration is the only route there is — the server cannot be asked to
    /// resolve a path — and it is what makes this worth doing per run rather than
    /// per file.
    /// </para>
    /// <para>
    /// A path the server does not hold is passed over in silence. It means the
    /// library has not been scanned since the video landed, and the scan that
    /// finds it will read the sidecar sitting next to it.
    /// </para>
    /// </remarks>
    public async Task<MediaServerRefresh> RefreshAsync(
        IReadOnlyList<string> filedPaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filedPaths);

        if (filedPaths.Count == 0)
        {
            return MediaServerRefresh.NotConfigured;
        }

        var configuration = await context.Configuration.AsNoTracking().SingleAsync(cancellationToken);

        // The library as this container sees it, which is where the tails are
        // measured from. It is read here rather than passed in so that there is
        // one answer to what the library root is.
        if (configuration.TargetDirectory is not { } libraryRoot
            || MediaServerConnection.From(configuration.MediaServerUrl, configuration.MediaServerApiKey, out _)
                is not { } connection)
        {
            return MediaServerRefresh.NotConfigured;
        }

        var items = await client.ItemsAsync(connection, cancellationToken);

        if (!items.Answered)
        {
            logger.LogWarning(
                "The media server could not be told about {Count} filed videos: {Problem}",
                filedPaths.Count,
                items.Problem);

            return new MediaServerRefresh(0, filedPaths.Count, items.Problem);
        }

        var told = 0;
        var missed = 0;

        foreach (var path in filedPaths)
        {
            if (LibraryTail.Of(libraryRoot, path) is not { } tail
                || LibraryTail.Match(items.Value!, tail) is not { Count: > 0 } found)
            {
                missed++;

                continue;
            }

            foreach (var item in found)
            {
                var refreshed = await client.RefreshAsync(connection, item.Id, cancellationToken);

                if (refreshed.Answered)
                {
                    told++;
                }
                else
                {
                    logger.LogWarning(
                        "The media server refused to read {Path} again: {Problem}",
                        path,
                        refreshed.Problem);
                }
            }
        }

        logger.LogInformation(
            "Told the media server to read {Told} of {Count} filed videos again; {Missed} it does not hold yet.",
            told,
            filedPaths.Count,
            missed);

        return new MediaServerRefresh(told, missed);
    }

    /// <summary>
    /// The most recently filed videos, as absolute paths. Most recent first,
    /// because one match is the whole proof and the oldest thing in the library
    /// is the least likely to still be on disk.
    /// </summary>
    private async Task<IReadOnlyList<string>> FiledPathsAsync(
        string? libraryRoot,
        CancellationToken cancellationToken)
    {
        if (libraryRoot is null)
        {
            return [];
        }

        return await context.FiledVideos
            .AsNoTracking()
            .Where(row => row.LibraryRoot == libraryRoot)
            .OrderByDescending(row => row.Id)
            .Take(Sample)
            .Select(row => row.Directory + "/" + row.FileName)
            .ToListAsync(cancellationToken);
    }

    private static MediaServerCheck Failed(MediaServerReach reach, string? problem) =>
        reach is MediaServerReach.Refused
            ? MediaServerCheck.Refused(problem ?? "The media server did not accept this API key.")
            : MediaServerCheck.Unreachable(problem ?? "The media server did not answer.");
}

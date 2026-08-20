using Microsoft.Extensions.Logging;
using Microsoft.Kiota.Abstractions;

using Prdb.Ordeno.Core.Identification;
using Prdb.Ordeno.Core.Review;
using Prdb.Ordeno.Infrastructure.Configuration;
using Prdb.Sdk;
using Prdb.Sdk.Generated;
using Prdb.Sdk.Generated.Models;

namespace Prdb.Ordeno.Infrastructure.Review;

/// <summary>
/// Asks prdb about videos on behalf of somebody working through the queue, over
/// <c>GET /videos</c> and <c>POST /videos/batch</c>.
/// </summary>
/// <remarks>
/// <para>
/// Neither of these is a rung of the recognition ladder and neither may become
/// one — ADR 0001 keeps that in prdb. This is the person's own question: they
/// looked at a file, they typed what they think it is, and prdb answers what it
/// has under that name. Nothing here is stored as an identification.
/// </para>
/// <para>
/// The client is built per call through <see cref="PrdbClientFactory"/>, never by
/// hand, because that is what refuses a redirect to another origin while
/// <c>X-Api-Key</c> is on the request.
/// </para>
/// </remarks>
public sealed class PrdbVideoLookup(
    IHttpMessageHandlerFactory handlers,
    ILogger<PrdbVideoLookup> logger)
    : IVideoLookup
{
    /// <summary>
    /// Somebody is waiting in front of a search box. Long enough for a slow
    /// answer, short enough that a stalled connection is a message rather than a
    /// screen that never comes back.
    /// </summary>
    private static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(20);

    public async Task<VideoLookupAnswer> SearchAsync(
        string apiKey,
        string query,
        Guid? siteId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return VideoLookupAnswer.From([]);
        }

        try
        {
            var client = Client(apiKey);

            var response = await client.Videos.GetAsync(
                configuration =>
                {
                    configuration.QueryParameters.Search = query.Trim();
                    configuration.QueryParameters.SiteId = siteId;
                    configuration.QueryParameters.Page = page;
                    configuration.QueryParameters.PageSize = pageSize;
                },
                cancellationToken);

            if (response?.Items is null)
            {
                logger.LogWarning("prdb answered a video search without any items in it.");

                return VideoLookupAnswer.Stopped(
                    "prdb answered something the tool did not understand, so there is nothing to show.");
            }

            return VideoLookupAnswer.From(
                [.. response.Items.Select(Read)],
                response.TotalCount ?? response.Items.Count);
        }
        catch (Exception exception) when (Handled(exception, cancellationToken))
        {
            return Stopped(exception, "searched");
        }
    }

    public async Task<VideoLookupAnswer> DescribeAsync(
        string apiKey,
        IReadOnlyList<Guid> videoIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(videoIds);

        if (videoIds.Count == 0)
        {
            return VideoLookupAnswer.From([]);
        }

        if (videoIds.Count > IVideoLookup.MaxBatch)
        {
            throw new ArgumentException(
                $"The endpoint takes {IVideoLookup.MaxBatch} videos at a time, not {videoIds.Count}.",
                nameof(videoIds));
        }

        // Read off the answer this call is making anyway. The review queue has
        // no use for it; the metadata refresh walks a library fifty scenes at a
        // time and stops on it (ADR 0032), and asking GET /rate-limit instead
        // would spend a request to find out whether there are requests left.
        var limits = new RateLimitOption();

        try
        {
            var client = Client(apiKey);

            var response = await client.Videos.Batch.PostAsync(
                new GetVideosByIdsRequest { Ids = [.. videoIds.Select(id => (Guid?)id)] },
                configuration => configuration.Options.Add(limits),
                cancellationToken);

            // An id prdb does not know is left out of the answer rather than
            // failing it, so this is a shorter list and not an error: a video
            // that has been merged away since the identification is exactly that
            // case, and the row it belongs to says so by having no words on it.
            return VideoLookupAnswer.From([.. (response ?? []).Select(Read)], rateLimit: Reading(limits));
        }
        catch (Exception exception) when (Handled(exception, cancellationToken))
        {
            return Stopped(exception, "asked about");
        }
    }

    private PrdbClient Client(string apiKey) => PrdbClientFactory.Create(
        apiKey,
        transport: handlers.CreateHandler(PrdbTransport.HttpClientName),
        // Somebody pressed a button. Three tries behind one press is a screen
        // that takes a minute to say the same thing one try would have said.
        retry: PrdbRetryOptions.Disabled,
        timeout: LookupTimeout);

    /// <summary>
    /// What the answer said about the quota, in this repository's own terms.
    /// Both windows, because a run that repeats nightly over a whole library
    /// spends a month where identification spends an hour.
    /// </summary>
    private static RateLimitReading? Reading(RateLimitOption limits) =>
        limits is { Hour: null, Month: null }
            ? null
            : new RateLimitReading(
                limits.Hour?.Remaining,
                limits.Hour is { } hour ? TimeSpan.FromSeconds(hour.ResetInSeconds) : null,
                limits.Month?.Remaining);

    private static VideoSummary Read(VideoSummaryDto video) => new(
        video.Id ?? Guid.Empty,
        video.Title,
        video.ReleaseDate is { } date ? (DateOnly)date : null,
        video.SiteId,
        video.SiteTitle,
        [.. (video.Actors ?? []).Select(actor => actor.Name).OfType<string>()]);

    private static VideoSummary Read(VideoDetailDto video) => new(
        video.Id ?? Guid.Empty,
        video.Title,
        video.ReleaseDate is { } date ? (DateOnly)date : null,
        video.Site?.Id,
        video.Site?.Title,
        [.. (video.Actors ?? []).Select(actor => actor.Name).OfType<string>()],
        FirstImage(video));

    /// <summary>
    /// The first image prdb lists for a video, or <c>null</c> where it lists
    /// none — which is a scene nobody has photographed rather than an answer
    /// that went wrong.
    /// </summary>
    /// <remarks>
    /// First, because prdb documents the order as stable — oldest first, image id
    /// breaking ties — and a filing decision needs two runs to choose the same
    /// image. It fixes the order and not a ranking: nothing says the oldest image
    /// is the best one, and ADR 0027 picks it for being reproducible.
    /// The value is a complete URL, which the schema has said since
    /// <c>Prdb.Sdk</c> 0.6.2 and names accurately since 0.7.0.
    /// </remarks>
    private static string? FirstImage(VideoDetailDto video) => (video.Images ?? [])
        .Select(image => image.Url)
        .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));

    /// <summary>
    /// Everything except a cancellation this caller asked for, which belongs to
    /// whoever asked rather than in a message on a screen.
    /// </summary>
    private static bool Handled(Exception exception, CancellationToken cancellationToken) =>
        exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested;

    /// <param name="what">
    /// What could not be done, as a past participle, so the message reads as a
    /// sentence about the thing the person just pressed.
    /// </param>
    private VideoLookupAnswer Stopped(Exception exception, string what)
    {
        switch (exception)
        {
            case ApiException api:
                logger.LogWarning("prdb refused a video lookup with status {Status}.", api.ResponseStatusCode);

                return VideoLookupAnswer.Stopped(api.ResponseStatusCode switch
                {
                    401 => "prdb does not know the stored API key any more, so nothing could be "
                        + $"{what}. Put the current key in under Settings.",
                    403 => "prdb knows the API key but will not let it in. Check that the account it "
                        + "belongs to still has an active subscription.",
                    429 => "prdb's quota for this key is spent for now, so nothing could be "
                        + $"{what}. Wait a while and try again.",
                    >= 500 => $"prdb is having trouble answering, so nothing could be {what}. Try "
                        + "again in a moment.",
                    _ => $"prdb answered with {api.ResponseStatusCode}, which the tool did not "
                        + $"expect. Nothing was {what}.",
                });

            case CrossOriginRedirectException:
                logger.LogError(
                    exception,
                    "A video lookup was redirected off the prdb host and stopped before the key was sent.");

                return VideoLookupAnswer.Stopped(
                    "Something answered for prdb and tried to send the tool somewhere else. The API "
                    + "key was not handed over — a proxy between this container and api.prdb.net is "
                    + "the usual explanation.");

            default:
                logger.LogWarning(exception, "prdb could not be reached for a video lookup.");

                return VideoLookupAnswer.Stopped(
                    $"prdb could not be reached, so nothing could be {what}. Try again in a moment.");
        }
    }
}

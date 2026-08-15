using Microsoft.AspNetCore.Http.HttpResults;

using Prdb.Ordeno.Infrastructure.Review;

namespace Prdb.Ordeno.Host.Review;

internal static class ReviewEndpoints
{
    /// <summary>
    /// Behind the password like everything else. Nothing here moves a file, but
    /// what it writes down is what the next filing run acts on — and two of these
    /// spend the user's prdb quota.
    /// </summary>
    public static IEndpointRouteBuilder MapReview(this IEndpointRouteBuilder endpoints)
    {
        var queue = endpoints.MapGroup("/api/queue").WithTags("Review");

        // One page of it. The filter, the site and the page are in the query
        // string because they are what a link to "the queue as I was looking at
        // it" consists of.
        queue.MapGet("/", async Task<Ok<ReviewQueueState>> (
            ReviewQueueService service,
            CancellationToken cancellationToken,
            string? filter = null,
            Guid? site = null,
            bool noSite = false,
            int page = 1) =>
            TypedResults.Ok(ReviewState.Of(await service.ReadAsync(
                ReviewState.FilterCalled(filter),
                site,
                noSite,
                page,
                cancellationToken))));

        // A request against the user's prdb quota, spent because they typed
        // something and pressed a button. It answers 200 with what went wrong
        // rather than an error status: prdb being unreachable is a line under a
        // search box, not a failed call.
        queue.MapGet("/search", async Task<Ok<VideoSearchState>> (
            ReviewQueueService service,
            CancellationToken cancellationToken,
            string? q = null,
            Guid? site = null) =>
            TypedResults.Ok(ReviewState.Of(await service.SearchAsync(q ?? string.Empty, site, cancellationToken))));

        // The video is named by id and by nothing else. What it is comes from
        // prdb — ADR 0023 — because the title becomes a directory name, and a
        // path built from what a page posted is a path built from unvalidated
        // input.
        queue.MapPost("/{fileId:int}/assignment", async Task<Results<Ok<ReviewDecisionState>, BadRequest<ReviewDecisionState>>> (
            int fileId,
            AssignRequest request,
            ReviewQueueService service,
            CancellationToken cancellationToken) =>
            Answer(await service.AssignAsync(fileId, request.VideoId, cancellationToken)));

        // Saying no. No body, because there is nothing to say: the file is not to
        // be filed, and it is not deleted or hidden either.
        queue.MapPost("/{fileId:int}/dismissal", async Task<Results<Ok<ReviewDecisionState>, BadRequest<ReviewDecisionState>>> (
            int fileId,
            ReviewQueueService service,
            CancellationToken cancellationToken) =>
            Answer(await service.DismissAsync(fileId, cancellationToken)));

        // The first day's version of the same thing: a thousand samples are one
        // decision somebody makes once.
        queue.MapPost("/dismissals", async Task<Results<Ok<ReviewDecisionState>, BadRequest<ReviewDecisionState>>> (
            DismissManyRequest request,
            ReviewQueueService service,
            CancellationToken cancellationToken) =>
            Answer(await service.DismissAsync(request.FileIds ?? [], cancellationToken)));

        // The way back from a wrong button, for either kind of decision. It is a
        // delete because that is what it is — the row goes, and the file waits
        // for an answer again.
        queue.MapDelete("/{fileId:int}/decision", async Task<Ok<ReviewDecisionState>> (
            int fileId,
            ReviewQueueService service,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(ReviewState.Of(await service.ForgetAsync(fileId, cancellationToken))));

        return endpoints;
    }

    /// <summary>
    /// A refusal keeps the shape of a decision, counts and all. The screen has to
    /// show a message and stay true to what is stored, and a second request to
    /// find out would be a second answer to disagree with.
    /// </summary>
    private static Results<Ok<ReviewDecisionState>, BadRequest<ReviewDecisionState>> Answer(
        Core.Review.ReviewDecision decision)
    {
        var state = ReviewState.Of(decision);

        return decision.Made ? TypedResults.Ok(state) : TypedResults.BadRequest(state);
    }
}

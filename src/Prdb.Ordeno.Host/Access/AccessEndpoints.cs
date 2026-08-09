using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;

using Prdb.Ordeno.Infrastructure.Access;

namespace Prdb.Ordeno.Host.Access;

/// <summary>What a caller may do without being signed in, and what it takes to sign in.</summary>
public sealed record AccessState(bool PasswordSet, bool Authenticated);

public sealed record SetPasswordRequest(string Password);

public sealed record SignInRequest(string Password);

public sealed record ProblemResponse(string Message);

internal static class AccessEndpoints
{
    /// <summary>
    /// The window in which anyone can claim the installation. It is open on a
    /// fresh database and closed for good once a password exists — ADR 0010.
    /// </summary>
    public const string SignInRateLimitPolicy = "sign-in";

    /// <summary>
    /// The return types are the union of what each endpoint can answer, because
    /// that is what the OpenAPI document is generated from — ADR 0014. A
    /// response the compiler knows about is one the frontend's types know about.
    /// </summary>
    public static IEndpointRouteBuilder MapAccess(this IEndpointRouteBuilder endpoints)
    {
        // The tag names the group in the document; without one every endpoint
        // is filed under the assembly name, which tells a reader nothing.
        var access = endpoints.MapGroup("/api/access").WithTags("Access");

        access.MapGet("/state", async Task<Ok<AccessState>> (
            AccessService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(new AccessState(
                PasswordSet: await service.IsPasswordSetAsync(cancellationToken),
                Authenticated: context.User.Identity?.IsAuthenticated == true)))
            .AllowAnonymous();

        access.MapPost("/password", async Task<Results<Ok<AccessState>, BadRequest<ProblemResponse>, Conflict<ProblemResponse>>> (
                SetPasswordRequest request,
                AccessService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                var result = await service.SetInitialPasswordAsync(request.Password, cancellationToken);

                return result.Status switch
                {
                    SetInitialPasswordStatus.Set => SignedIn(context, result.SessionToken!),
                    SetInitialPasswordStatus.TooShort => TypedResults.BadRequest(new ProblemResponse(
                        $"The password must be at least {AccessService.MinimumPasswordLength} characters long.")),
                    _ => TypedResults.Conflict(new ProblemResponse(
                        "This installation already has a password. Sign in with it, or reset it from the "
                        + "machine the data directory is mounted on.")),
                };
            })
            .AllowAnonymous()
            .RequireRateLimiting(SignInRateLimitPolicy);

        // The one endpoint whose responses are declared rather than returned: a
        // 401 that carries a body has no typed result behind it, and the
        // untyped JSON one describes itself as a 200. Declaring both here keeps
        // the document honest; changing what this answers means changing the
        // two lines below with it.
        access.MapPost("/session", async Task<IResult> (
                SignInRequest request,
                AccessService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                var result = await service.SignInAsync(request.Password, cancellationToken);

                return result.Succeeded
                    ? SignedIn(context, result.SessionToken!)
                    : Results.Json(
                        new ProblemResponse("That password is wrong."),
                        statusCode: StatusCodes.Status401Unauthorized);
            })
            .AllowAnonymous()
            .RequireRateLimiting(SignInRateLimitPolicy)
            .Produces<AccessState>(StatusCodes.Status200OK)
            .Produces<ProblemResponse>(StatusCodes.Status401Unauthorized);

        access.MapDelete("/session", async Task<NoContent> (
            AccessService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (context.Request.Cookies.TryGetValue(SessionCookie.Name, out var token) && token is not null)
            {
                await service.SignOutAsync(token, cancellationToken);
            }

            SessionCookie.Delete(context.Response);

            return TypedResults.NoContent();
        });

        return endpoints;
    }

    private static Ok<AccessState> SignedIn(HttpContext context, string token)
    {
        SessionCookie.Write(context.Response, token, DateTimeOffset.UtcNow + AccessService.SessionLifetime);

        return TypedResults.Ok(new AccessState(PasswordSet: true, Authenticated: true));
    }
}

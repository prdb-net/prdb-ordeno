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

    public static IEndpointRouteBuilder MapAccess(this IEndpointRouteBuilder endpoints)
    {
        var access = endpoints.MapGroup("/api/access");

        access.MapGet("/state", async (AccessService service, HttpContext context, CancellationToken cancellationToken) =>
            Results.Ok(new AccessState(
                PasswordSet: await service.IsPasswordSetAsync(cancellationToken),
                Authenticated: context.User.Identity?.IsAuthenticated == true)))
            .AllowAnonymous();

        access.MapPost("/password", async (
                SetPasswordRequest request,
                AccessService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                var result = await service.SetInitialPasswordAsync(request.Password, cancellationToken);

                return result.Status switch
                {
                    SetInitialPasswordStatus.Set => SignedIn(context, result.SessionToken!),
                    SetInitialPasswordStatus.TooShort => Results.BadRequest(new ProblemResponse(
                        $"The password must be at least {AccessService.MinimumPasswordLength} characters long.")),
                    _ => Results.Conflict(new ProblemResponse(
                        "This installation already has a password. Sign in with it, or reset it from the "
                        + "machine the data directory is mounted on.")),
                };
            })
            .AllowAnonymous()
            .RequireRateLimiting(SignInRateLimitPolicy);

        access.MapPost("/session", async (
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
            .RequireRateLimiting(SignInRateLimitPolicy);

        access.MapDelete("/session", async (
            AccessService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (context.Request.Cookies.TryGetValue(SessionCookie.Name, out var token) && token is not null)
            {
                await service.SignOutAsync(token, cancellationToken);
            }

            SessionCookie.Delete(context.Response);

            return Results.NoContent();
        });

        return endpoints;
    }

    private static IResult SignedIn(HttpContext context, string token)
    {
        SessionCookie.Write(context.Response, token, DateTimeOffset.UtcNow + AccessService.SessionLifetime);

        return Results.Ok(new AccessState(PasswordSet: true, Authenticated: true));
    }
}

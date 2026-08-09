using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

using Prdb.Ordeno.Infrastructure.Access;

namespace Prdb.Ordeno.Host.Access;

/// <summary>
/// Turns the session cookie into an authenticated request by looking the token
/// up in the database, which is what makes a session revocable and lets one
/// survive a restart (ADR 0010).
/// </summary>
internal sealed class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    AccessService access)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    public const string SchemeName = "OrdenoSession";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Cookies.TryGetValue(SessionCookie.Name, out var token) || string.IsNullOrWhiteSpace(token))
        {
            return AuthenticateResult.NoResult();
        }

        var session = await access.AuthenticateAsync(token, Context.RequestAborted);
        if (session is null)
        {
            // Expired or revoked. Clearing it stops the browser from presenting a
            // token that will never work again on every request it makes.
            SessionCookie.Delete(Response);
            return AuthenticateResult.NoResult();
        }

        var identity = new ClaimsIdentity(SchemeName);
        identity.AddClaim(new Claim(
            ClaimTypes.NameIdentifier,
            session.Id.ToString(CultureInfo.InvariantCulture)));

        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    /// <summary>
    /// No redirect to a sign-in page. The browser side is one page that decides
    /// for itself what to show, so an unauthenticated request gets the answer
    /// rather than a document.
    /// </summary>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}

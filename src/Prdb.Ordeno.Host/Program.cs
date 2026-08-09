using System.Threading.RateLimiting;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;

using Prdb.Ordeno.Host.Access;
using Prdb.Ordeno.Infrastructure.Access;
using Prdb.Ordeno.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// ADR 0009: the container environment carries only what has to exist before the
// application starts. This is one of those things — everything the user answers
// lives in the database this points at. /data is where the image mounts it.
var dataDirectory = builder.Configuration["ORDENO_DATA_DIRECTORY"] ?? "/data";

builder.Services.AddOrdenoPersistence(dataDirectory);
builder.Services.AddOrdenoAccess();

builder.Services
    .AddAuthentication(SessionAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(
        SessionAuthenticationHandler.SchemeName,
        configureOptions: null);

// Everything is behind the password unless it says otherwise, rather than
// everything being open unless someone remembered to close it. The endpoints
// that opt out are the ones a visitor needs before they can be signed in.
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder(SessionAuthenticationHandler.SchemeName)
        .RequireAuthenticatedUser()
        .Build());

// One password and no username is the easiest thing in the world to try
// repeatedly (ADR 0010). Partitioned by caller so that one machine hammering the
// door cannot lock the household out.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy(AccessEndpoints.SignInRateLimitPolicy, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
});

var app = builder.Build();

// Before anything is served. A migration that cannot be applied stops the tool
// rather than letting it run against a schema it does not understand — ADR 0007.
try
{
    await app.Services.PrepareOrdenoDatabaseAsync();
}
catch (DatabaseMigrationException)
{
    // The migrator has already logged what happened and why, at critical level.
    // Adding a stack trace on top of it would only bury that message in the
    // container's log, which is the one place the user will look.
    return 1;
}

if (builder.Configuration.GetValue("ORDENO_RESET_PASSWORD", defaultValue: false))
{
    await app.Services.ResetOrdenoAccessAsync();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/health", () => Results.Ok(new HealthResponse("ok"))).AllowAnonymous();

app.MapAccess();

// ADR 0006: routing happens in the browser, so unknown paths return index.html
// and let the frontend decide. Unknown API paths must not — a caller that asked
// a question the API does not have gets that answer, not a page.
app.MapFallback("/api/{*rest}", () => Results.NotFound()).AllowAnonymous();
app.MapFallbackToFile("index.html").AllowAnonymous();

app.Run();

return 0;

internal sealed record HealthResponse(string Status);

/// <summary>
/// Exposed so that the tests can host the application exactly as it is composed
/// here — the wiring is the part worth testing, not a copy of it.
/// </summary>
public partial class Program;

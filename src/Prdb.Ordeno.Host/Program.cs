using System.Reflection;
using System.Threading.RateLimiting;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi;

using Prdb.Ordeno.Host.Access;
using Prdb.Ordeno.Host.Configuration;
using Prdb.Ordeno.Host.Identification;
using Prdb.Ordeno.Host.Library;
using Prdb.Ordeno.Host.Review;
using Prdb.Ordeno.Host.Scanning;
using Prdb.Ordeno.Infrastructure.Access;
using Prdb.Ordeno.Infrastructure.Configuration;
using Prdb.Ordeno.Infrastructure.Identification;
using Prdb.Ordeno.Infrastructure.Library;
using Prdb.Ordeno.Infrastructure.Persistence;
using Prdb.Ordeno.Infrastructure.Review;
using Prdb.Ordeno.Infrastructure.Scanning;

var builder = WebApplication.CreateBuilder(args);

// ADR 0009: the container environment carries only what has to exist before the
// application starts. This is one of those things — everything the user answers
// lives in the database this points at. /data is where the image mounts it.
var dataDirectory = builder.Configuration["ORDENO_DATA_DIRECTORY"] ?? "/data";

builder.Services.AddOrdenoPersistence(dataDirectory);
builder.Services.AddOrdenoAccess();
builder.Services.AddOrdenoConfiguration();
builder.Services.AddOrdenoScanning();
builder.Services.AddOrdenoIdentification();
builder.Services.AddOrdenoLibrary();
builder.Services.AddOrdenoReview();

// The tool is set up once and left alone, so looking in the download
// directories is something it does rather than something it is asked for. The
// same goes for asking prdb what was found, and for the hashing behind it —
// three timers rather than one chain, because they fail in different ways and
// none of them may take the others down with it.
builder.Services.AddHostedService<ScanWorker>();
builder.Services.AddHostedService<IdentificationWorker>();
builder.Services.AddHostedService<PerceptualHashWorker>();

// ADR 0014: this describes the API for the build that turns it into the
// frontend's types. Nothing maps it as an endpoint — the document is written to
// a file at build time and committed, and the browser never asks for it.
builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
{
    document.Info = new OpenApiInfo
    {
        Title = "prdb-ordeno",
        Version = "v1",
        Description =
            "The API the browser side of prdb-ordeno talks to. Generated from the "
            + "code at build time and committed: change an endpoint, not this file.",
    };

    return Task.CompletedTask;
}));

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

// ADR 0014: the build-time generator loads this application to read its
// endpoints and stops it where it would start listening. Everything below runs
// in that process too, so what prepares a real installation is skipped there —
// a build has no business creating a database or clearing a password.
var readingTheEndpoints = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

if (!readingTheEndpoints)
{
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
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/health", () => TypedResults.Ok(new HealthResponse("ok")))
    .AllowAnonymous()
    .WithTags("Health");

app.MapAccess();
app.MapConfiguration();
app.MapScanning();
app.MapIdentification();
app.MapFiling();
app.MapReview();

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

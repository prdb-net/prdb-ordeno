using Prdb.Ordeno.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// ADR 0009: the container environment carries only what has to exist before the
// application starts. This is one of those things — everything the user answers
// lives in the database this points at. /data is where the image mounts it.
var dataDirectory = builder.Configuration["ORDENO_DATA_DIRECTORY"] ?? "/data";

builder.Services.AddOrdenoPersistence(dataDirectory);

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

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", () => Results.Ok(new HealthResponse("ok")));

// ADR 0006: routing happens in the browser, so unknown paths return index.html
// and let the frontend decide. Unknown API paths must not — a caller that asked
// a question the API does not have gets that answer, not a page.
app.MapFallback("/api/{*rest}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

app.Run();

return 0;

internal sealed record HealthResponse(string Status);

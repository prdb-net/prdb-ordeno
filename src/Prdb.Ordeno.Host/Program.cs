var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", () => Results.Ok(new HealthResponse("ok")));

// ADR 0006: routing happens in the browser, so unknown paths return index.html
// and let the frontend decide. Unknown API paths must not — a caller that asked
// a question the API does not have gets that answer, not a page.
app.MapFallback("/api/{*rest}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

app.Run();

internal sealed record HealthResponse(string Status);

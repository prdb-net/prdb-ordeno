using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Ordeno.Infrastructure.Configuration;

namespace Prdb.Ordeno.Host.Tests;

/// <summary>
/// The application as <c>Program.cs</c> composes it, pointed at a data directory
/// of the test's own. Nothing is replaced with a test double: what is behind the
/// password and what is not is a property of the wiring, so the wiring is what
/// runs here.
/// </summary>
/// <param name="prdb">
/// The connection the SDK sends through, when a test needs prdb to answer
/// something in particular. This replaces the socket and nothing above it — see
/// <see cref="FakePrdb"/>.
/// </param>
internal sealed class OrdenoApplication(
    string dataDirectory,
    bool resetPassword = false,
    HttpMessageHandler? prdb = null)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ORDENO_DATA_DIRECTORY", dataDirectory);

        if (resetPassword)
        {
            builder.UseSetting("ORDENO_RESET_PASSWORD", "true");
        }

        if (prdb is not null)
        {
            builder.ConfigureServices(services => services
                .AddHttpClient(PrdbApiKeyCheck.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => prdb));
        }
    }
}

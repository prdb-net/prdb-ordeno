using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Prdb.Ordeno.Host.Tests;

/// <summary>
/// The application as <c>Program.cs</c> composes it, pointed at a data directory
/// of the test's own. Nothing is replaced with a test double: what is behind the
/// password and what is not is a property of the wiring, so the wiring is what
/// runs here.
/// </summary>
internal sealed class OrdenoApplication(string dataDirectory, bool resetPassword = false)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ORDENO_DATA_DIRECTORY", dataDirectory);

        if (resetPassword)
        {
            builder.UseSetting("ORDENO_RESET_PASSWORD", "true");
        }
    }
}

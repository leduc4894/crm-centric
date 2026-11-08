using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Integration.Fixtures;

public sealed class ApiFactory(IReadOnlyDictionary<string, string?> configuration)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(configuration));
        // Keep test configuration and any future dependency diagnostics out of test output.
        builder.ConfigureLogging(logging => logging.ClearProviders());
    }
}

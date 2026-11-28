using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Integration.Fixtures;

public sealed class ApiFactory(IReadOnlyDictionary<string, string?> configuration)
    : WebApplicationFactory<Program>
{
    private static readonly string TestCursorKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ContactCursor:SigningKey"] = TestCursorKey,
            ["Oidc:Issuer"] = "http://127.0.0.1:1/",
            ["Oidc:Audience"] = "crm-api",
            ["Oidc:MetadataAddress"] = "http://127.0.0.1:1/.well-known/openid-configuration",
            ["Oidc:AllowHttpLoopback"] = "true"
        }).AddInMemoryCollection(configuration));
        // Keep test configuration and any future dependency diagnostics out of test output.
        builder.ConfigureLogging(logging => logging.ClearProviders());
    }
}

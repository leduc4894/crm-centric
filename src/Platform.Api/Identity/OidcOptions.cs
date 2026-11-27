namespace Platform.Api.Identity;

public sealed record OidcOptions(string Issuer, string Audience, string MetadataAddress, bool AllowHttpLoopback)
{
    public static OidcOptions Parse(IConfiguration configuration, IHostEnvironment environment)
    {
        var section = configuration.GetSection("Oidc");
        var issuer = section["Issuer"];
        var audience = section["Audience"];
        var metadata = section["MetadataAddress"];
        var allowHttp = bool.TryParse(section["AllowHttpLoopback"], out var value) && value;
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(audience) ||
            !Uri.TryCreate(issuer, UriKind.Absolute, out var issuerUri) ||
            !Uri.TryCreate(metadata, UriKind.Absolute, out var metadataUri) ||
            !Allowed(issuerUri) || !Allowed(metadataUri))
            throw new ArgumentException("Invalid OIDC configuration.");
        return new OidcOptions(issuer, audience, metadata!, allowHttp);

        bool Allowed(Uri uri) => uri.Scheme == Uri.UriSchemeHttps ||
            (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback && allowHttp &&
             (environment.IsDevelopment() || environment.IsEnvironment("Testing")));
    }
}

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Platform.Api.Operations;

namespace Platform.Api.Identity;

public static class AuthenticationSetup
{
    private sealed class MetadataHandler(bool allowLoopback) : DelegatingHandler(new HttpClientHandler { AllowAutoRedirect = false })
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri;
            if (uri is null || (uri.Scheme != Uri.UriSchemeHttps &&
                !(allowLoopback && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
                throw new HttpRequestException("Invalid metadata transport.");
            return base.SendAsync(request, cancellationToken);
        }
    }

    public static IServiceCollection AddPlatformAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IHostEnvironment>((options, environment) =>
            {
                var oidc = OidcOptions.Parse(configuration, environment);
                options.MetadataAddress = oidc.MetadataAddress;
                options.RequireHttpsMetadata = new Uri(oidc.MetadataAddress).Scheme == Uri.UriSchemeHttps;
                options.BackchannelHttpHandler = new MetadataHandler(oidc.AllowHttpLoopback && (environment.IsDevelopment() || environment.IsEnvironment("Testing")));
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true, ValidIssuer = oidc.Issuer,
                    IssuerValidator = (issuer, _, _) => string.Equals(issuer, oidc.Issuer, StringComparison.Ordinal)
                        ? issuer : throw new SecurityTokenInvalidIssuerException(),
                    ValidateAudience = true, ValidAudience = oidc.Audience,
                    IgnoreTrailingSlashWhenValidatingAudience = false,
                    ValidateIssuerSigningKey = true, RequireSignedTokens = true,
                    ValidateLifetime = true, RequireExpirationTime = true,
                    ClockSkew = TimeSpan.Zero,
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256]
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        if (string.IsNullOrWhiteSpace(context.Principal?.FindFirst("iss")?.Value) ||
                            string.IsNullOrWhiteSpace(context.Principal?.FindFirst("sub")?.Value))
                            context.Fail("Missing identity claims.");
                        else
                            VerifiedEmail.ApplyValidatedPayload(context.Principal!, context.SecurityToken);
                        return Task.CompletedTask;
                    },
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        await OperationalErrors.WriteProblemAsync(context.HttpContext, 401, "AUTHENTICATION_REQUIRED", context.HttpContext.RequestAborted);
                    }
                };
            });
        services.AddAuthorization();
        return services;
    }
}

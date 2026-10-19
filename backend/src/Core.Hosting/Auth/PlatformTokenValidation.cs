using Saaskit.Core.Composition;
using Saaskit.Core.Configuration;
using Saaskit.Core.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Saaskit.Core.Hosting.Auth;

/// <summary>
/// Access token validation, identical in every service: ES256 only, keys only from the configured issuer,
/// iss/exp/nbf with at most 30 s skew, typ at+jwt (RFC 9068), and aud limited to the services this host runs.
/// </summary>
public static class PlatformTokenValidation
{
    public const string AccessTokenType = "at+jwt";

    public static IServiceCollection AddPlatformTokenValidation(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ConfigureJwtBearer>();
        return services;
    }

    private sealed class ConfigureJwtBearer(
        IOptions<TenancyOptions> tenancy,
        IOptions<AuthOptions> auth,
        HostedPlatformServices hosted) : IConfigureNamedOptions<JwtBearerOptions>
    {
        public void Configure(string? name, JwtBearerOptions options)
        {
            if (name != JwtBearerDefaults.AuthenticationScheme)
                return;
            Configure(options);
        }

        public void Configure(JwtBearerOptions options)
        {
            var issuer = tenancy.Value.Issuer;
            options.MapInboundClaims = false;
            options.RequireHttpsMetadata = auth.Value.RequireHttpsMetadata;
            options.MetadataAddress = auth.Value.MetadataAddress ?? $"{issuer}.well-known/openid-configuration";
            if (auth.Value.MetadataAddress is { } internalAddress)
            {
                // Discovery is fetched internally; the document's endpoints name the public issuer, so map them back.
                var retriever = new IssuerMappingDocumentRetriever(issuer, new Uri(internalAddress).GetLeftPart(UriPartial.Authority), auth.Value.RequireHttpsMetadata);
                options.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                    options.MetadataAddress, new OpenIdConnectConfigurationRetriever(), retriever);
            }
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = issuer,
                ValidAudiences = hosted.All.Select(s => s.Audience).ToList(),
                ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
                ValidTypes = [AccessTokenType],
                ClockSkew = TimeSpan.FromSeconds(auth.Value.ClockSkewSeconds),
                RequireSignedTokens = true,
                RequireExpirationTime = true,
                ValidateIssuerSigningKey = true,
                NameClaimType = PlatformClaims.Subject,
                // Roles are "{module}:{role}" and compared by level; IsInRole is deliberately never true.
                RoleClaimType = "platform:no-role-claim",
            };
        }
    }

    /// <summary>Fetches the issuer's documents from an internal origin.</summary>
    private sealed class IssuerMappingDocumentRetriever(string issuer, string internalOrigin, bool requireHttps) : IDocumentRetriever
    {
        private readonly HttpDocumentRetriever _inner = new() { RequireHttps = requireHttps };

        public Task<string> GetDocumentAsync(string address, CancellationToken cancel) =>
            _inner.GetDocumentAsync(
                address.StartsWith(issuer, StringComparison.OrdinalIgnoreCase) ? internalOrigin.TrimEnd('/') + "/" + address[issuer.Length..] : address,
                cancel);
    }
}

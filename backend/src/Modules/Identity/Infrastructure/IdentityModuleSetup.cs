using Farm360.Core.Configuration;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using OpenIddict.Server;

namespace Farm360.Identity.Infrastructure;

internal static class IdentityModuleSetup
{
    public const string TenantCookieClaim = "tenant_slug";

    /// <summary>The token and authorization request parameter naming the tenant (its slug).</summary>
    public const string TenantParameter = "tenant";

    public static void Add(IServiceCollection services, IConfiguration configuration)
    {
        services.AddPlatformOptions<TokenServerOptions>(configuration);
        var tokenServer = configuration.GetValidatedOptions<TokenServerOptions>();

        services.AddHttpClient(NistPasswordValidator.HttpClientName, client => client.BaseAddress = new Uri("https://api.pwnedpasswords.com/"));
        services.AddScoped<IPasswordValidator<PlatformUser>, NistPasswordValidator>();

        services.AddIdentityCore<PlatformUser>(options =>
            {
                options.User.RequireUniqueEmail = false;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredLength = 1;           // length is enforced by NistPasswordValidator
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(1);   // grows with ConsecutiveLockouts
                options.Tokens.PasswordResetTokenProvider = TokenOptions.DefaultProvider;
            })
            .AddEntityFrameworkStores<IdentityDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        // Passkeys are bound to the one central sign-in origin, https://login.{BaseDomain}.
        services.AddOptions<IdentityPasskeyOptions>().Configure<Microsoft.Extensions.Options.IOptions<TenancyOptions>>((passkey, tenancy) =>
            passkey.ServerDomain = new Uri(tenancy.Value.Issuer).Host);

        // The sign-in pages' own session on login.{BaseDomain}. These register the Identity cookie schemes
        // without making them the default scheme: APIs keep authenticating with bearer tokens.
        services.AddAuthentication()
            .AddCookie(IdentityConstants.ApplicationScheme, options =>
            {
                options.Cookie.Name = "__Host-login";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.Always;
                options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
                options.LoginPath = "/account/login";
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
            })
            .AddCookie(IdentityConstants.TwoFactorUserIdScheme, options =>
            {
                options.Cookie.Name = "__Host-login-mfa";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.Always;
                options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
            });
        services.AddScoped<IUserClaimsPrincipalFactory<PlatformUser>, TenantUserClaimsPrincipalFactory>();
        services.AddAntiforgery(options =>
        {
            options.Cookie.Name = "__Host-login-csrf";
            options.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.Always;
            options.FormFieldName = "__csrf";
            options.HeaderName = "X-CSRF-Token";
        });

        services.AddScoped<AccessPrincipalFactory>();
        services.AddScoped<Application.IAuditWriter, AuditWriter>();
        services.AddScoped<Application.IClientRegistry, ClientRegistry>();
        services.AddScoped<Application.IAccountService, AccountService>();
        services.AddScoped<Application.IAccessManagement, AccessManagement>();
        services.AddScoped<Application.ITenantSecurity, TenantSecurity>();
        services.AddScoped<Application.ISupportTokenIssuer, SupportTokenIssuer>();
        services.AddHostedService<PlatformClientSeeder>();
        services.AddSingleton<Application.IAccountMessages, LoggingAccountMessages>();

        services.AddScoped<OpenIddict.OidcStoreContext>();
        services.AddScoped<OpenIddict.OidcApplicationStore>();

        services.AddOpenIddict()
            .AddCore(core =>
            {
                core.SetDefaultApplicationEntity<OpenIddict.OidcApplication>()
                    .SetDefaultAuthorizationEntity<OpenIddict.OidcAuthorization>()
                    .SetDefaultTokenEntity<OpenIddict.OidcToken>();
                core.ReplaceApplicationStore<OpenIddict.OidcApplication, OpenIddict.OidcApplicationStore>(ServiceLifetime.Scoped)
                    .ReplaceAuthorizationStore<OpenIddict.OidcAuthorization, OpenIddict.OidcAuthorizationStore>(ServiceLifetime.Scoped)
                    .ReplaceTokenStore<OpenIddict.OidcToken, OpenIddict.OidcTokenStore>(ServiceLifetime.Scoped);
            })
            .AddServer(server =>
            {
                server.SetAuthorizationEndpointUris("/connect/authorize")
                    .SetTokenEndpointUris("/connect/token")
                    .SetEndSessionEndpointUris("/connect/logout")
                    .SetRevocationEndpointUris("/connect/revoke")
                    .SetJsonWebKeySetEndpointUris("/.well-known/jwks")
                    .SetConfigurationEndpointUris("/.well-known/openid-configuration");

                // No password grant (RFC 9700). Authorization code + PKCE, client credentials, refresh, and token exchange (RFC 8693).
                server.AllowAuthorizationCodeFlow().RequireProofKeyForCodeExchange()
                    .AllowClientCredentialsFlow()
                    .AllowRefreshTokenFlow()
                    .AllowTokenExchangeFlow();

                server.RegisterScopes(OpenIddictConstants.Scopes.OpenId, OpenIddictConstants.Scopes.OfflineAccess);

                // The issuer is constant for every tenant: https://login.{BaseDomain}.
                server.SetIssuer(new Uri(configuration.GetValidatedOptions<TenancyOptions>().Issuer));

                foreach (var key in TokenServerKeys.SigningKeys(tokenServer))
                    server.AddSigningKey(key);
                foreach (var key in TokenServerKeys.EncryptionKeys(tokenServer))
                    server.AddEncryptionKey(key);

                // Access tokens are signed JWTs, not encrypted, so other services validate them with public keys only.
                // Authorization codes and refresh tokens stay encrypted.
                server.DisableAccessTokenEncryption();

                server.SetAccessTokenLifetime(TimeSpan.FromMinutes(tokenServer.AccessTokenMinutes))
                    .SetRefreshTokenLifetime(TimeSpan.FromDays(tokenServer.RefreshTokenDays))
                    .SetRefreshTokenReuseLeeway(TimeSpan.Zero)
                    .SetAuthorizationCodeLifetime(TimeSpan.FromMinutes(5));

                // Audiences are per service (RFC 8707 resource indicators), checked against platform.yaml in the token endpoint.
                server.DisableResourceValidation().IgnoreResourcePermissions().DisableAudienceValidation().IgnoreAudiencePermissions()
                    .DisableScopeValidation().IgnoreScopePermissions();

                var aspNetCore = server.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough()
                    .EnableStatusCodePagesIntegration();
                if (!tokenServer.RequireTransportSecurity)
                    aspNetCore.DisableTransportSecurityRequirement();

                server.RemoveEventHandler(OpenIddictServerHandlers.Exchange.ValidateAuthorizedParty.Descriptor);
                server.AddEventHandler(ExchangeAuthorizedParty.Descriptor);

                server.AddEventHandler<OpenIddictServerEvents.ValidateTokenRequestContext>(handler =>
                    handler.UseScopedHandler<SelectTenantForTokenRequest>().SetOrder(int.MinValue + 1000));
            });
    }
}

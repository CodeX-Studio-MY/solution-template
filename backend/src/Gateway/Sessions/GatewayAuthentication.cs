using System.Security.Claims;
using Saaskit.Core.Configuration;
using Saaskit.Core.Platform;
using Saaskit.Core.Tenancy;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Saaskit.Gateway.Sessions;

/// <summary>
/// The Gateway is a confidential OAuth client (authorization code with PKCE). After sign-in the tokens stay server-side; the
/// browser gets a session cookie: <c>__Host-</c> prefix, HttpOnly, Secure, SameSite=Lax (Lax so a link from an email or another
/// site doesn't look signed out on the first page; state-changing requests need the CSRF header instead).
/// </summary>
internal static class GatewayAuthentication
{
    public const string CookieScheme = "bff";
    public const string OidcScheme = "oidc";
    public const string TenantClaim = "tenant_slug";
    public const string SessionIdClaim = "Microsoft.AspNetCore.Authentication.Cookies-SessionId";

    public static IServiceCollection AddGatewayAuthentication(this IServiceCollection services)
    {
        services.AddSingleton<RedisTicketStore>();
        services.AddSingleton<SessionTokenService>();
        services.AddHttpClient(SessionTokenService.HttpClientName);

        services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieScheme;
                options.DefaultChallengeScheme = OidcScheme;
            })
            .AddCookie(CookieScheme, options =>
            {
                options.Cookie.Name = "__Host-bff";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.Path = "/";
                options.SlidingExpiration = true;
                // APIs answer 401 (the app redirects to /bff/login itself); only /bff/login challenges the browser.
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            })
            .AddOpenIdConnect(OidcScheme, _ => { });

        services.AddOptions<CookieAuthenticationOptions>(CookieScheme).Configure<RedisTicketStore, IOptions<BffOptions>>((cookie, store, bff) =>
        {
            cookie.SessionStore = store;
            cookie.ExpireTimeSpan = TimeSpan.FromDays(bff.Value.SessionDays);
        });

        services.AddOptions<OpenIdConnectOptions>(OidcScheme).Configure<IOptions<TenancyOptions>, IOptions<AuthOptions>, IOptions<BffOptions>>((oidc, tenancy, auth, bff) =>
        {
            var issuer = tenancy.Value.Issuer;
            oidc.SignInScheme = CookieScheme;
            oidc.Authority = issuer;
            oidc.MetadataAddress = auth.Value.MetadataAddress ?? $"{issuer}.well-known/openid-configuration";
            oidc.RequireHttpsMetadata = auth.Value.RequireHttpsMetadata;
            if (auth.Value.MetadataAddress is { } internalAddress)
                oidc.BackchannelHttpHandler = new InternalAddressHandler(new Uri(issuer), new Uri(new Uri(internalAddress).GetLeftPart(UriPartial.Authority)));
            oidc.ClientId = PlatformClients.Bff;
            oidc.ClientSecret = bff.Value.ClientSecret;
            oidc.ResponseType = OpenIdConnectResponseType.Code;
            oidc.ResponseMode = OpenIdConnectResponseMode.Query;   // a GET callback works with SameSite=Lax cookies
            oidc.UsePkce = true;
            oidc.SaveTokens = true;                       // into the server-side ticket, never the cookie
            oidc.GetClaimsFromUserInfoEndpoint = false;
            oidc.MapInboundClaims = false;
            oidc.CallbackPath = "/bff/signin-oidc";
            oidc.SignedOutCallbackPath = "/bff/signout-callback-oidc";
            oidc.Scope.Clear();
            oidc.Scope.Add("openid");
            oidc.Scope.Add("offline_access");

            oidc.Events.OnRedirectToIdentityProvider = context =>
            {
                // The central sign-in host authenticates against this tenant's users.
                context.ProtocolMessage.SetParameter("tenant", Slug(context.HttpContext));
                return Task.CompletedTask;
            };
            oidc.Events.OnAuthorizationCodeReceived = context =>
            {
                context.TokenEndpointRequest?.SetParameter("tenant", Slug(context.HttpContext));
                return Task.CompletedTask;
            };
            oidc.Events.OnTokenValidated = context =>
            {
                // The session belongs to this tenant. Checked again on every request.
                ((ClaimsIdentity)context.Principal!.Identity!).AddClaim(new Claim(TenantClaim, Slug(context.HttpContext)));
                return Task.CompletedTask;
            };
            oidc.Events.OnRemoteFailure = context =>
            {
                context.Response.Redirect("/?error=signin");
                context.HandleResponse();
                return Task.CompletedTask;
            };
        });
        return services;
    }

    private static string Slug(HttpContext context) => context.RequestServices.GetRequiredService<ITenantContext>().Tenant.Slug;
}

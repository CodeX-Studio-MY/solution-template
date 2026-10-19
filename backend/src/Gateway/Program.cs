using Saaskit.Core.Configuration;
using Saaskit.Core.Hosting;
using Saaskit.Gateway.Http;
using Saaskit.Gateway.Proxy;
using Saaskit.Gateway.Sessions;
using Microsoft.AspNetCore.HttpOverrides;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

builder.AddPlatformInfrastructure("gateway");
builder.Services.AddPlatformOptions<BffOptions>(builder.Configuration);
builder.Services.AddPlatformOptions<FrontendOptions>(builder.Configuration);
builder.Services.AddGatewayAuthentication();
builder.Services.AddAuthorization();

var (routes, clusters) = ProxyConfiguration.Build(
    builder.Configuration.GetValidatedOptions<TenancyOptions>(),
    builder.Configuration.GetValidatedOptions<BffOptions>());
builder.Services.AddReverseProxy()
    .LoadFromMemory(routes, clusters)
    .AddServiceDiscoveryDestinationResolver()
    .AddTransforms(context =>
    {
        // The tenant's host stays the Host header downstream, so each service can check the token's tenant against it.
        context.AddOriginalHost(true);
        if (context.Route.Metadata?.ContainsKey(ProxyConfiguration.AudienceMetadata) == true)
        {
            // The browser's cookie and any headers it tries to set for the platform never reach a service.
            context.AddRequestHeaderRemove("Cookie");
            context.AddRequestHeaderRemove("Authorization");
            context.AddRequestHeaderRemove(Saaskit.Core.Security.PlatformHeaders.Tenant);
            context.AddRequestHeaderRemove(Saaskit.Core.Security.PlatformHeaders.Csrf);
            context.AddRequestTransform(transform =>
            {
                transform.ProxyRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", (string)transform.HttpContext.Items[SessionItems.AccessToken]!);
                return default;
            });
        }
    });

var app = builder.Build();

app.UseForwardedHeaders(new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost });
app.UseExceptionHandler();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<GatewayTenantMiddleware>();
app.UseMiddleware<CsrfMiddleware>();
ShellHost.Use(app);
app.UseAuthentication();
app.UseAuthorization();

app.MapPlatformHealth();
BffEndpoints.Map(app);
app.MapReverseProxy(proxy => proxy.Use(SessionGate.InvokeAsync));

app.Run();

/// <summary>Entry point marker for the integration tests (WebApplicationFactory).</summary>
public partial class Program;

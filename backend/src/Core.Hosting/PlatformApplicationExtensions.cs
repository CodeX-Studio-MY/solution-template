using Acme.Core.Hosting.Auth;
using Acme.Core.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;

namespace Acme.Core.Hosting;

public static class PlatformApplicationExtensions
{
    /// <summary>Enables the platform pipeline and maps health endpoints and every added module's endpoints.</summary>
    public static WebApplication UsePlatform(this WebApplication app)
    {
        app.UseForwardedHeaders(new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
        });
        app.UseExceptionHandler();
        app.UseAuthentication();
        app.UseMiddleware<TenantResolutionMiddleware>();
        app.UseMiddleware<AccessVersionMiddleware>();
        app.UseMiddleware<RateLimitMiddleware>();
        app.UseAuthorization();

        app.MapPlatformHealth();

        foreach (var module in app.Services.GetServices<ModuleEndpoints>())
        {
            module.Map(app.MapGroup(module.Service.RoutePrefix));
            module.MapRoot(app);
        }

        return app;
    }

    /// <summary>
    /// <c>/health/live</c> checks only that the process responds (no dependencies, so a database blip doesn't restart every container);
    /// <c>/health/ready</c> checks the catalog database and Redis (and RabbitMQ once messaging is added).
    /// </summary>
    public static WebApplication MapPlatformHealth(this WebApplication app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(HealthTags.Ready) })
            .AllowAnonymous();
        return app;
    }
}

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
        app.UseAuthorization();

        // Liveness: the process responds. No dependencies, so a database blip doesn't restart containers.
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
        // Readiness: the catalog database, RabbitMQ and Redis.
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(HealthTags.Ready) })
            .AllowAnonymous();

        foreach (var module in app.Services.GetServices<ModuleEndpoints>())
            module.Map(app.MapGroup(module.Service.RoutePrefix));

        return app;
    }
}

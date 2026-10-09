using Acme.Core.Composition;
using Acme.Core.Configuration;
using Acme.Core.Data;
using Acme.Core.ServiceCalls;
using Acme.Core.Platform;
using Acme.Tenancy.Api;
using Acme.Tenancy.Application;
using Acme.Tenancy.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Acme.Tenancy;

/// <summary>Catalog, tenant lifecycle, onboarding, entitlements, settings, and the platform operator realm.</summary>
public sealed class TenancyModule : IPlatformModule
{
    public static PlatformService Service => PlatformServices.Tenancy;

    public static void Register(IHostApplicationBuilder builder)
    {
        var services = builder.Services;
        services.AddCatalogDbContext<TenancyCatalogDbContext>(Service.Schema);
        services.AddPlatformOptions<OnboardingOptions>(builder.Configuration);
        services.AddPlatformOptions<FrontendOptions>(builder.Configuration);
        services.AddPlatformOptions<OperatorOptions>(builder.Configuration);
        services.AddDataProtection();

        services.AddScoped<TenantManagement>();
        services.AddScoped<ITenantAdmin>(sp => sp.GetRequiredService<TenantManagement>());
        services.AddScoped<ITenantLifecycle>(sp => sp.GetRequiredService<TenantManagement>());
        services.AddScoped<IOnboarding, Onboarding>();
        services.AddScoped<ITenantMigrator, ProcessTenantMigrator>();
        services.AddScoped<ITenantExporter, NoExportYet>();
        services.AddScoped<IBillingProvider, NoBilling>();
        services.AddScoped<IModuleManifestProvider, ModuleManifestProvider>();
        services.AddScoped<IOperatorAccounts, OperatorAccounts>();
        services.AddScoped<IOperatorSessions, OperatorSessions>();
        services.AddScoped<ISupportAccess, SupportAccess>();
        services.AddPlatformServiceClient<IIdentityClient, IdentityClient>(PlatformServices.Identity);

        // Operators are a separate realm with their own scheme, so tenant bearer tokens never authenticate them.
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, OperatorAuthenticationHandler>(OperatorAuthenticationHandler.SchemeName, null);
        services.AddAuthorizationBuilder().AddPolicy(OperatorAuthenticationHandler.Policy, policy =>
            policy.AddAuthenticationSchemes(OperatorAuthenticationHandler.SchemeName).RequireAuthenticatedUser());
    }

    public static void MapEndpoints(IEndpointRouteBuilder api) => TenancyEndpoints.Map(api);
}

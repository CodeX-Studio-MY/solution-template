using Acme.Tenancy.Infrastructure;
using Acme.Core.Data;
using Acme.Core.Composition;
using Acme.Core.Platform;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;

namespace Acme.Tenancy;

/// <summary>Catalog, tenant lifecycle, onboarding, entitlements and settings.</summary>
public sealed class TenancyModule : IPlatformModule
{
    public static PlatformService Service => PlatformServices.Tenancy;

    public static void Register(IHostApplicationBuilder builder)
    {
        builder.Services.AddCatalogDbContext<TenancyCatalogDbContext>(Service.Schema);
    }

    public static void MapEndpoints(IEndpointRouteBuilder api)
    {
    }
}

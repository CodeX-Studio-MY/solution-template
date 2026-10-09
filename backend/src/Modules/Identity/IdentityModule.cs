using Acme.Core.Composition;
using Acme.Core.Data;
using Acme.Core.Platform;
using Acme.Identity.Api;
using Acme.Identity.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;

namespace Acme.Identity;

/// <summary>Token server, users, machines, service clients, groups and module roles.</summary>
public sealed class IdentityModule : IPlatformModule
{
    public static PlatformService Service => PlatformServices.Identity;

    public static void Register(IHostApplicationBuilder builder)
    {
        builder.Services.AddModuleDbContext<IdentityDbContext>(Service.Schema);
        builder.Services.AddCatalogDbContext<IdentityCatalogDbContext>(Service.Schema);
        IdentityModuleSetup.Add(builder.Services, builder.Configuration);
    }

    public static void MapEndpoints(IEndpointRouteBuilder api)
    {
        AccessManagementEndpoints.Map(api);
        PlatformEndpoints.Map(api);
    }

    public static void MapRootEndpoints(IEndpointRouteBuilder app)
    {
        ConnectEndpoints.Map(app);
        AccountEndpoints.Map(app);
    }
}

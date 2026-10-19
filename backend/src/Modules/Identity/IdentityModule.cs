using Saaskit.Core.Composition;
using Saaskit.Core.Data;
using Saaskit.Core.Platform;
using Saaskit.Identity.Api;
using Saaskit.Identity.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;

namespace Saaskit.Identity;

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

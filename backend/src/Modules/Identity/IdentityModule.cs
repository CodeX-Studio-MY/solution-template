using Acme.Identity.Infrastructure;
using Acme.Core.Data;
using Acme.Core.Composition;
using Acme.Core.Platform;
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
    }

    public static void MapEndpoints(IEndpointRouteBuilder api)
    {
    }
}

using Acme.PurchaseRequest.Infrastructure;
using Acme.Core.Data;
using Acme.Core.Composition;
using Acme.Core.Platform;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;

namespace Acme.PurchaseRequest;

/// <summary>Reference scenario: the standard module every tenant has.</summary>
public sealed class PurchaseRequestModule : IPlatformModule
{
    public static PlatformService Service => PlatformServices.PurchaseRequest;

    public static void Register(IHostApplicationBuilder builder)
    {
        builder.Services.AddModuleDbContext<PurchaseRequestDbContext>(Service.Schema);
    }

    public static void MapEndpoints(IEndpointRouteBuilder api)
    {
    }
}

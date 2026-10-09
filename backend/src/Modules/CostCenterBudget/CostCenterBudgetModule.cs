using Acme.CostCenterBudget.Infrastructure;
using Acme.Core.Data;
using Acme.Core.Composition;
using Acme.Core.Platform;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;

namespace Acme.CostCenterBudget;

/// <summary>Reference scenario: an add-on module entitled only in the Plus plan.</summary>
public sealed class CostCenterBudgetModule : IPlatformModule
{
    public static PlatformService Service => PlatformServices.CostCenterBudget;

    public static void Register(IHostApplicationBuilder builder)
    {
        builder.Services.AddModuleDbContext<CostCenterBudgetDbContext>(Service.Schema);
    }

    public static void MapEndpoints(IEndpointRouteBuilder api)
    {
    }
}

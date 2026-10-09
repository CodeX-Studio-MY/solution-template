using Acme.Contracts;
using Acme.Core.Composition;
using Acme.Core.Data;
using Acme.Core.Localization;
using Acme.Core.Messaging;
using Acme.Core.Platform;
using Acme.CostCenterBudget.Api;
using Acme.CostCenterBudget.Application;
using Acme.CostCenterBudget.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Acme.CostCenterBudget;

/// <summary>Reference scenario: an add-on module entitled only in the Plus plan (rung 7). Finance sets budgets; approved requests use them up.</summary>
public sealed class CostCenterBudgetModule : IPlatformModule
{
    public static PlatformService Service => PlatformServices.CostCenterBudget;

    public static void Register(IHostApplicationBuilder builder)
    {
        var services = builder.Services;
        services.AddModuleDbContext<CostCenterBudgetDbContext>(Service.Schema);
        services.AddModuleMessages(typeof(CostCenterBudgetModule).Assembly);
        services.AddScoped<IBudgetRepository, BudgetRepository>();
        services.AddScoped<BudgetService>();

        // Rung 5: react to a purchase request being approved. Only tenants entitled to this module get the event.
        services.AddEventConsumer<PurchaseRequestApproved.V1, UseBudgetOnApproval, CostCenterBudgetDbContext>(Service, Modules.CostCenterBudget);
    }

    public static void MapEndpoints(IEndpointRouteBuilder api) => BudgetEndpoints.Map(api);
}

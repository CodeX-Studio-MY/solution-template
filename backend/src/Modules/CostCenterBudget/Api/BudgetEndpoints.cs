using Acme.Core.Authorization;
using Acme.Core.Platform;
using Acme.CostCenterBudget.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Acme.CostCenterBudget.Api;

internal static class BudgetEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var budgets = api.MapGroup("/v1/budgets").WithTags("budgets");

        // Finance (managers) sees and sets every budget.
        budgets.MapGet("", (BudgetService service, CancellationToken ct) => service.ListAsync(ct))
            .RequireModuleRole(Modules.CostCenterBudget, Role.Manager);

        // Anyone in the module may see what is left in one cost center: the panel on the purchase request page shows it.
        budgets.MapGet("/{code}", async (string code, BudgetService service, HttpResponse response, CancellationToken ct) =>
        {
            var budget = await service.GetAsync(code, ct);
            response.Headers.ETag = budget.ETag;
            return budget;
        }).RequireModuleRole(Modules.CostCenterBudget, Role.User);

        budgets.MapPut("/{code}", async (string code, [FromHeader(Name = "If-Match")] string? ifMatch, SaveBudgetInput body, BudgetService service, HttpResponse response, CancellationToken ct) =>
        {
            var budget = await service.SaveAsync(code, ifMatch, body, ct);
            response.Headers.ETag = budget.ETag;
            return budget;
        }).RequireModuleRole(Modules.CostCenterBudget, Role.Manager);
    }
}

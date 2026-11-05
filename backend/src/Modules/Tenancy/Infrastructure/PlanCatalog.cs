using System.Text.Json;
using Saaskit.Core.Catalog;
using Saaskit.Core.Catalog.Entities;
using Saaskit.Core.Data;
using Saaskit.Core.Errors;
using Saaskit.Core.Platform;
using Saaskit.Core.Tenancy;
using Saaskit.Tenancy.Application;
using Saaskit.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;

namespace Saaskit.Tenancy.Infrastructure;

internal sealed class PlanCatalog(
    CatalogDbContext catalog,
    TenancyCatalogDbContext tenancy,
    ITenantChangeNotifier changes,
    TimeProvider time) : IPlanCatalog
{
    private const decimal MaxPrice = 1_000_000_000m;

    public async Task<IReadOnlyList<PlanRow>> ListAsync(CancellationToken cancellationToken = default)
    {
        // Tracked, because the ETag is read from the row version.
        var plans = await catalog.Plans.OrderBy(p => p.SortOrder).ThenBy(p => p.PlanId).ToListAsync(cancellationToken);
        return await RowsAsync(plans, cancellationToken);
    }

    public async Task<PlanRow> CreateAsync(PlanCreate input, string actor, CancellationToken cancellationToken = default)
    {
        var key = input.PlanKey?.Trim().ToLowerInvariant() ?? "";
        var errors = new List<FieldError>();
        if (!KeyRules.Pattern.IsMatch(key))
            errors.Add(new FieldError("planKey", "validation.invalid", ["planKey"]));
        var order = input.SortOrder;
        if (order is null)
        {
            var last = await catalog.Plans.MaxAsync(p => (byte?)p.SortOrder, cancellationToken) ?? 0;
            if (last == byte.MaxValue)
                errors.Add(new FieldError("sortOrder", "validation.invalid", ["sortOrder"]));
            order = (byte)Math.Min(last + 1, byte.MaxValue);
        }

        var fields = ValidateFields(input.DisplayName, input.Description, input.MonthlyPrice, input.Currency ?? PlatformInfo.DefaultCurrency, order.Value, errors);
        if (await catalog.Plans.AnyAsync(p => p.PlanKey == key, cancellationToken))
            throw new ConflictException(TenancyErrors.PlanKeyInUse);

        var plan = new Plan(key, fields.DisplayName, fields.Description, fields.MonthlyPrice, fields.Currency, fields.SortOrder);
        catalog.Plans.Add(plan);
        Log("plan-created", actor, new { plan = key, fields.DisplayName });
        await SaveAsync(TenancyErrors.PlanKeyInUse, cancellationToken);
        return (await RowsAsync([plan], cancellationToken))[0];
    }

    public async Task<PlanRow> UpdateAsync(short planId, PlanUpdate input, string? ifMatch, string actor, CancellationToken cancellationToken = default)
    {
        var fields = ValidateFields(input.DisplayName, input.Description, input.MonthlyPrice, input.Currency, input.SortOrder, []);
        var plan = await FindAsync(planId, cancellationToken);
        catalog.ApplyIfMatch(plan, ifMatch);
        plan.Change(fields.DisplayName, fields.Description, fields.MonthlyPrice, fields.Currency, fields.SortOrder);
        Log("plan-changed", actor, new { plan = plan.PlanKey, fields.DisplayName, fields.MonthlyPrice, fields.Currency, fields.SortOrder });
        await SaveAsync(null, cancellationToken);
        return (await RowsAsync([plan], cancellationToken))[0];
    }

    public async Task<PlanRow> SetActiveAsync(short planId, bool active, string? ifMatch, string actor, CancellationToken cancellationToken = default)
    {
        var plan = await FindAsync(planId, cancellationToken);
        catalog.ApplyIfMatch(plan, ifMatch);
        plan.SetActive(active);
        Log(active ? "plan-restored" : "plan-retired", actor, new { plan = plan.PlanKey });
        await SaveAsync(null, cancellationToken);
        return (await RowsAsync([plan], cancellationToken))[0];
    }

    public async Task<PlanRow> AddModuleAsync(short planId, string moduleKey, string actor, CancellationToken cancellationToken = default)
    {
        var plan = await FindAsync(planId, cancellationToken);
        var module = await FindModuleAsync(moduleKey, cancellationToken);
        if (await catalog.PlanModuleAccess.AnyAsync(a => a.PlanId == planId && a.ModuleId == module.ModuleId, cancellationToken))
            return (await RowsAsync([plan], cancellationToken))[0];

        catalog.PlanModuleAccess.Add(new PlanModuleAccess { PlanId = planId, ModuleId = module.ModuleId });
        var tenants = await TenantsOnPlanAsync(planId, cancellationToken);
        var having = await catalog.TenantModules.Where(t => t.ModuleId == module.ModuleId && tenants.Contains(t.TenantId)).Select(t => t.TenantId).ToListAsync(cancellationToken);
        foreach (var tenantId in tenants.Except(having))
            catalog.TenantModules.Add(new TenantModule { TenantId = tenantId, ModuleId = module.ModuleId });

        LogForTenants("plan-module-added", actor, tenants, new { plan = plan.PlanKey, module = moduleKey });
        Log("plan-module-added", actor, new { plan = plan.PlanKey, module = moduleKey, tenants = tenants.Count });
        await SaveAndPublishAsync(tenants, cancellationToken);
        return (await RowsAsync([plan], cancellationToken))[0];
    }

    public async Task<PlanRow> RemoveModuleAsync(short planId, string moduleKey, string actor, CancellationToken cancellationToken = default)
    {
        var plan = await FindAsync(planId, cancellationToken);
        var module = await FindModuleAsync(moduleKey, cancellationToken);
        var access = await catalog.PlanModuleAccess.SingleOrDefaultAsync(a => a.PlanId == planId && a.ModuleId == module.ModuleId, cancellationToken);
        if (access is null)
            return (await RowsAsync([plan], cancellationToken))[0];

        catalog.PlanModuleAccess.Remove(access);
        var tenants = await TenantsOnPlanAsync(planId, cancellationToken);
        // Add-ons go too: the same as a plan change today.
        catalog.TenantModules.RemoveRange(await catalog.TenantModules.Where(t => t.ModuleId == module.ModuleId && tenants.Contains(t.TenantId)).ToListAsync(cancellationToken));

        LogForTenants("plan-module-removed", actor, tenants, new { plan = plan.PlanKey, module = moduleKey });
        Log("plan-module-removed", actor, new { plan = plan.PlanKey, module = moduleKey, tenants = tenants.Count });
        await SaveAndPublishAsync(tenants, cancellationToken);
        return (await RowsAsync([plan], cancellationToken))[0];
    }

    public async Task<IReadOnlyList<ModuleRow>> ListModulesAsync(CancellationToken cancellationToken = default) =>
        await catalog.Modules.AsNoTracking().OrderBy(m => m.DisplayName).Select(m => new ModuleRow(m.ModuleKey, m.DisplayName)).ToListAsync(cancellationToken);

    // ---- Helpers --------------------------------------------------------------------------------

    private async Task<List<PlanRow>> RowsAsync(IReadOnlyCollection<Plan> plans, CancellationToken cancellationToken)
    {
        var ids = plans.Select(p => p.PlanId).ToList();
        var modules = await (from access in catalog.PlanModuleAccess.AsNoTracking()
                             join module in catalog.Modules.AsNoTracking() on access.ModuleId equals module.ModuleId
                             where ids.Contains(access.PlanId)
                             select new { access.PlanId, module.ModuleKey }).ToListAsync(cancellationToken);
        var counts = await catalog.Tenants.AsNoTracking()
            .Where(t => ids.Contains(t.PlanId) && t.Status != TenantStatus.Deleted)
            .GroupBy(t => t.PlanId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        return [.. plans.Select(p => new PlanRow(
            p.PlanId, p.PlanKey, p.DisplayName, p.Description, p.MonthlyPrice, p.Currency, p.SortOrder, p.IsActive,
            counts.GetValueOrDefault(p.PlanId),
            [.. modules.Where(m => m.PlanId == p.PlanId).Select(m => m.ModuleKey).Order(StringComparer.Ordinal)],
            catalog.GetETag(p)))];
    }

    private async Task<Plan> FindAsync(short planId, CancellationToken cancellationToken) =>
        await catalog.Plans.SingleOrDefaultAsync(p => p.PlanId == planId, cancellationToken)
        ?? throw new NotFoundException(TenancyErrors.PlanNotFound);

    private async Task<CatalogModule> FindModuleAsync(string moduleKey, CancellationToken cancellationToken) =>
        await catalog.Modules.AsNoTracking().SingleOrDefaultAsync(m => m.ModuleKey == moduleKey, cancellationToken)
        ?? throw new NotFoundException(TenancyErrors.ModuleNotFound);

    private async Task<List<Guid>> TenantsOnPlanAsync(short planId, CancellationToken cancellationToken) =>
        await catalog.Tenants.AsNoTracking().Where(t => t.PlanId == planId && t.Status != TenantStatus.Deleted).Select(t => t.Id).ToListAsync(cancellationToken);

    private void Log(string kind, string actor, object details) =>
        tenancy.AuditLog.Add(new TenancyAuditEntry(time.GetUtcNow().UtcDateTime, null, kind, actor, JsonSerializer.Serialize(details)));

    private void LogForTenants(string kind, string actor, IEnumerable<Guid> tenants, object details)
    {
        var now = time.GetUtcNow().UtcDateTime;
        foreach (var tenantId in tenants)
            tenancy.AuditLog.Add(new TenancyAuditEntry(now, tenantId, kind, actor, JsonSerializer.Serialize(details)));
    }

    private async Task SaveAsync(ErrorCode? duplicate, CancellationToken cancellationToken)
    {
        try
        {
            await catalog.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException e) when (duplicate is not null && e is not DbUpdateConcurrencyException)
        {
            throw new ConflictException(duplicate);   // lost a race the check before saving can't see (the unique index)
        }
        await tenancy.SaveChangesAsync(cancellationToken);
    }

    private async Task SaveAndPublishAsync(IEnumerable<Guid> tenants, CancellationToken cancellationToken)
    {
        await catalog.SaveChangesAsync(cancellationToken);
        await tenancy.SaveChangesAsync(cancellationToken);
        foreach (var tenantId in tenants)
            await changes.PublishAsync(tenantId, cancellationToken);
    }

    private sealed record Fields(string DisplayName, string? Description, decimal? MonthlyPrice, string Currency, byte SortOrder);

    private static Fields ValidateFields(string? displayName, string? description, decimal? monthlyPrice, string? currency, byte sortOrder, List<FieldError> errors)
    {
        var name = displayName?.Trim() ?? "";
        if (name.Length == 0)
            errors.Add(new FieldError("displayName", "validation.required", ["displayName"]));
        else if (name.Length > Plan.MaxDisplayNameLength)
            errors.Add(new FieldError("displayName", "validation.maxLength", ["displayName", Plan.MaxDisplayNameLength]));
        var note = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (note?.Length > Plan.MaxDescriptionLength)
            errors.Add(new FieldError("description", "validation.maxLength", ["description", Plan.MaxDescriptionLength]));
        if (monthlyPrice is < 0 or > MaxPrice)
            errors.Add(new FieldError("monthlyPrice", "validation.invalid", ["monthlyPrice"]));
        var code = currency?.Trim().ToUpperInvariant() ?? "";
        if (!TenantManagement.IsoCurrencies.Contains(code))
            errors.Add(new FieldError("currency", "validation.invalid", ["currency"]));
        if (sortOrder == 0)
            errors.Add(new FieldError("sortOrder", "validation.invalid", ["sortOrder"]));
        return errors.Count > 0
            ? throw new ValidationException(errors)
            : new Fields(name, note, monthlyPrice is { } price ? Math.Round(price, 4) : null, code, sortOrder);
    }
}

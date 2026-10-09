using System.Text.Json;
using Acme.Core.Catalog;
using Acme.Core.Catalog.Entities;
using Acme.Core.Configuration;
using Acme.Core.Data;
using Acme.Core.Errors;
using Acme.Core.Platform;
using Acme.Core.Tenancy;
using Acme.Tenancy.Application;
using Acme.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Acme.Tenancy.Infrastructure;

/// <summary>
/// The onboarding pipeline: create the catalog row, the database, run the migrator for that tenant, seed reference data,
/// enable the plan's modules, activate (services refuse a tenant that is still provisioning), register the tenant's callback
/// URIs, and invite the first admin by email.
/// Every step is recorded, so running it again for the same slug resumes at the step that failed.
/// </summary>
internal sealed partial class Onboarding(
    CatalogDbContext catalog,
    TenancyCatalogDbContext tenancy,
    ITenantMigrator migrator,
    IIdentityClient identity,
    ITenantChangeNotifier changes,
    IDatabaseProviderAccessor providers,
    IOptions<DatabaseOptions> database,
    IOptions<OnboardingOptions> options,
    IOptions<TenancyOptions> tenancyOptions,
    TimeProvider time,
    ILogger<Onboarding> logger) : IOnboarding
{
    public async Task<OnboardResult> OnboardAsync(OnboardRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);
        var slug = request.Slug.ToLowerInvariant();

        var run = await tenancy.OnboardingRuns.SingleOrDefaultAsync(r => r.Slug == slug, cancellationToken);
        if (run is null)
        {
            if (await catalog.Tenants.AnyAsync(t => t.Slug == slug, cancellationToken))
                throw new ConflictException(TenancyErrors.SlugInUse);
            run = new OnboardingRun(slug, request.DisplayName, request.Plan, request.AdminEmail, request.AdminName, request.ServerPool ?? options.Value.DefaultServerPool);
            tenancy.OnboardingRuns.Add(run);
            await tenancy.SaveChangesAsync(cancellationToken);
        }

        Uri? invitation = null;
        while (!run.IsDone)
        {
            var step = run.NextStep;
            try
            {
                invitation = await RunStepAsync(run, step, cancellationToken) ?? invitation;
                run.Completed(step);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogStepFailed(logger, ex, slug, step);
                run.Failed($"{step}: {ex.Message}");
                await tenancy.SaveChangesAsync(CancellationToken.None);
                throw new PlatformOnboardingException(step, ex);
            }
            await tenancy.SaveChangesAsync(cancellationToken);
        }
        return new OnboardResult(run.TenantId!.Value, slug, invitation);
    }

    private async Task<Uri?> RunStepAsync(OnboardingRun run, OnboardingStep step, CancellationToken cancellationToken)
    {
        switch (step)
        {
            case OnboardingStep.CatalogRow:
            {
                var tenant = await catalog.Tenants.SingleOrDefaultAsync(t => t.Slug == run.Slug, cancellationToken);
                if (tenant is null)
                {
                    tenant = new CatalogTenant(
                        run.Slug,
                        run.DisplayName,
                        run.Plan,
                        PlatformInfo.DefaultTimeZone,
                        PlatformInfo.DefaultCurrency,
                        PlatformInfo.DefaultLocales[0],
                        new TenantPlacementData
                        {
                            Provider = database.Value.Provider,
                            ServerPool = run.ServerPool,
                            DatabaseName = DatabaseName(run.Slug),
                        });
                    catalog.Tenants.Add(tenant);
                    await catalog.SaveChangesAsync(cancellationToken);
                }
                run.Attach(tenant.Id);
                tenancy.AuditLog.Add(new TenancyAuditEntry(time.GetUtcNow().UtcDateTime, tenant.Id, "onboarding-started", null, JsonSerializer.Serialize(new { run.Plan })));
                return null;
            }
            case OnboardingStep.Database:
            {
                var tenant = await TenantAsync(run, cancellationToken);
                var pool = database.Value.Servers.GetValueOrDefault(tenant.Placement.ServerPool)
                    ?? throw new InvalidOperationException($"Database:Servers:{tenant.Placement.ServerPool} is not configured.");
                await providers.ForName(tenant.Placement.Provider).EnsureDatabaseAsync(pool, tenant.Placement.DatabaseName, cancellationToken);
                return null;
            }
            case OnboardingStep.Migrations:
                await migrator.MigrateAsync(run.TenantId!.Value, cancellationToken);
                return null;
            case OnboardingStep.ReferenceData:
                // Reference data a module needs is created by the module itself the first time it runs for the tenant.
                return null;
            case OnboardingStep.Modules:
            {
                var tenant = await TenantAsync(run, cancellationToken);
                var existing = await catalog.TenantModules.Where(t => t.TenantId == tenant.Id).Select(t => t.ModuleId).ToListAsync(cancellationToken);
                foreach (var module in Modules.All.Where(m => m.Plans.Contains(tenant.Plan) && !existing.Contains(m.Id)))
                    catalog.TenantModules.Add(new TenantModule { TenantId = tenant.Id, ModuleId = module.Id });
                await catalog.SaveChangesAsync(cancellationToken);
                await changes.PublishAsync(tenant.Id, cancellationToken);
                return null;
            }
            case OnboardingStep.BffRedirects:
            {
                var origin = tenancyOptions.Value.TenantOrigin(run.Slug);
                await identity.SetBffRedirectUrisAsync(run.TenantId!.Value, [$"{origin}/bff/signin-oidc", $"{origin}/bff/signout-callback-oidc"], [], cancellationToken);
                return null;
            }
            case OnboardingStep.Activate:
            {
                var tenant = await TenantAsync(run, cancellationToken);
                if (tenant.Status == TenantStatus.Provisioning)
                    tenant.ChangeStatus(TenantStatus.Active);
                tenancy.AuditLog.Add(new TenancyAuditEntry(time.GetUtcNow().UtcDateTime, tenant.Id, "onboarding-activated", null, null));
                await catalog.SaveChangesAsync(cancellationToken);
                await changes.PublishAsync(tenant.Id, cancellationToken);
                return null;
            }
            case OnboardingStep.FirstAdminInvitation:
            {
                var tenant = await TenantAsync(run, cancellationToken);
                var modules = Modules.All.Where(m => m.Plans.Contains(tenant.Plan)).Select(m => m.Key);
                // No default passwords: the first admin gets an emailed, single-use invitation.
                return await identity.InviteFirstAdminAsync(tenant.Id, run.AdminEmail, run.AdminName, modules, cancellationToken);
            }
            default:
                return null;
        }
    }

    private async Task<CatalogTenant> TenantAsync(OnboardingRun run, CancellationToken cancellationToken) =>
        await catalog.Tenants.SingleAsync(t => t.Id == run.TenantId, cancellationToken);

    /// <summary>The tenant's database name: product prefix plus slug, safe for both providers (letters, digits, underscores).</summary>
    internal static string DatabaseName(string slug) => $"{PlatformInfo.Product}_{slug.Replace('-', '_')}";

    private static void Validate(OnboardRequest request)
    {
        var slug = request.Slug.ToLowerInvariant();
        if (!KeyRules.Pattern.IsMatch(slug))
            throw new ValidationException(new Dictionary<string, string[]> { ["slug"] = [$"Use lowercase letters, digits and hyphens, starting with a letter (2–30 characters)."] }, TenancyErrors.SlugInvalid);
        if (KeyRules.ReservedTenantSlugs.Contains(slug))
            throw new BusinessRuleException(TenancyErrors.ReservedSlug, slug);
        if (!Plans.All.Contains(request.Plan))
            throw new BusinessRuleException(TenancyErrors.UnknownPlan, request.Plan);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Onboarding of {Slug} failed at step {Step}; run it again to resume")]
    private static partial void LogStepFailed(ILogger logger, Exception exception, string slug, OnboardingStep step);
}

/// <summary>Onboarding stopped at a step. Running it again resumes there.</summary>
public sealed class PlatformOnboardingException(OnboardingStep step, Exception inner)
    : PlatformException(TenancyErrors.OnboardingFailed, $"Onboarding failed at {step}: {inner.Message}", inner);

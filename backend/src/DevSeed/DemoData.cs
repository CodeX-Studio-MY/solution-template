using System.Text.Json;
using System.Text.Json.Nodes;
using Acme.Contracts;
using Acme.Core.Data;
using Acme.Core.Platform;
using Acme.Core.Tenancy;
using Acme.CostCenterBudget.Application;
using Acme.PurchaseRequest.Application;
using Acme.PurchaseRequest.Domain;
using Acme.PurchaseRequest.Infrastructure;
using Acme.Tenancy.Application;
using Microsoft.EntityFrameworkCore;

namespace Acme.DevSeed;

/// <summary>
/// The two demo tenants of the reference scenario (SAAS_TEMPLATE_PROMPT 7.1): <c>demo</c> on the Standard plan with defaults, and
/// <c>demo-plus</c> on the Plus plan with its own settings, approval strategy, flag, cost center field, budgets and brand colour.
/// Safe to run again: what exists is left alone.
/// </summary>
internal sealed partial class DemoData(IServiceProvider services)
{
    private const string Actor = "dev-seed";
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly Guid SampleRequester = new("00000000-0000-0000-0000-00000000d3a0");
    private static readonly Guid SampleApprover = new("00000000-0000-0000-0000-00000000d3a1");
    private static readonly string[] CostCenters = ["CC-100", "CC-200", "CC-300"];

    /// <summary>A small purple square, as a data URI: the policy allows images from this site and data URIs, nothing else.</summary>
    private const string PlusLogo = "data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHdpZHRoPSIzMiIgaGVpZ2h0PSIzMiI+PHJlY3Qgd2lkdGg9IjMyIiBoZWlnaHQ9IjMyIiByeD0iNiIgZmlsbD0iIzdiM2ZhMCIvPjx0ZXh0IHg9IjE2IiB5PSIyMyIgZm9udC1zaXplPSIyMCIgZm9udC1mYW1pbHk9IkFyaWFsIiBmb250LXdlaWdodD0iNzAwIiB0ZXh0LWFuY2hvcj0ibWlkZGxlIiBmaWxsPSIjZmZmIj4rPC90ZXh0Pjwvc3ZnPg==";

    public async Task SeedAsync(string invitationsFile, CancellationToken cancellationToken)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger<DemoData>();
        var invitations = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (slug, name, plan) in new[] { ("demo", "Demo Co", Plans.Standard), ("demo-plus", "Demo Plus Co", Plans.Plus) })
        {
            if (await OnboardAsync(slug, name, plan, cancellationToken) is { } url)
            {
                invitations[slug] = url;
                LogInvitation(logger, slug, url);
            }
        }

        await EnsureOperatorAsync(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(invitationsFile))!, "operator.json"), cancellationToken);
        await CustomizePlusAsync(cancellationToken);
        await SeedTenantAsync("demo", withCostCenters: false, cancellationToken);
        await SeedTenantAsync("demo-plus", withCostCenters: true, cancellationToken);

        if (invitations.Count > 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(invitationsFile))!);
            var existing = File.Exists(invitationsFile) ? JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(invitationsFile, cancellationToken)) ?? [] : [];
            foreach (var (slug, url) in invitations)
                existing[slug] = url;
            await File.WriteAllTextAsync(invitationsFile, JsonSerializer.Serialize(existing, Indented), cancellationToken);
        }
        LogDone(logger);
    }

    /// <summary>Onboards a tenant that doesn't exist yet. Returns the first admin's invitation link, or null if it was already there.</summary>
    private async Task<string?> OnboardAsync(string slug, string name, string plan, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        if (await scope.ServiceProvider.GetRequiredService<ITenantDirectory>().FindBySlugAsync(slug, cancellationToken) is not null)
            return null;
        var result = await scope.ServiceProvider.GetRequiredService<IOnboarding>()
            .OnboardAsync(new OnboardRequest(slug, name, plan, $"admin@{slug}.example", $"{name} Admin"), cancellationToken);
        return result.AdminInvitationUrl?.ToString();
    }

    /// <summary>
    /// A platform operator for local development, so scripts/onboard-tenant can create more tenants. Its credentials are saved next to
    /// the invitation links (git-ignored); a production operator is created with <c>Migrator --create-operator</c> and never saved.
    /// </summary>
    private async Task EnsureOperatorAsync(string file, CancellationToken cancellationToken)
    {
        if (File.Exists(file))
            return;
        await using var scope = services.CreateAsyncScope();
        try
        {
            var created = await scope.ServiceProvider.GetRequiredService<IOperatorAccounts>().CreateAsync("dev-operator@example.com", "Dev Operator", cancellationToken);
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(new { email = created.Email, password = created.Password, totpSecret = created.TotpSecret }, Indented), cancellationToken);
        }
        catch (Acme.Core.Errors.ConflictException)
        {
            // It exists from an earlier run whose credentials were lost: scripts/dev-down --reset starts clean.
        }
    }

    /// <summary>What makes demo-plus different, as data: settings, the approval strategy, a flag and the brand colour (rungs 1 to 3).</summary>
    private async Task CustomizePlusAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var tenant = await scope.ServiceProvider.GetRequiredService<ITenantDirectory>().FindBySlugAsync("demo-plus", cancellationToken) ?? throw new InvalidOperationException("demo-plus is missing.");
        var customization = scope.ServiceProvider.GetRequiredService<ITenantCustomization>();
        await customization.SetSettingsAsync(tenant.Id, Modules.PurchaseRequest, new JsonObject { ["autoApproveBelow"] = 200, ["approvalPolicy"] = ApprovalPolicyKeys.TwoLevel }, tenantAdmin: false, Actor, cancellationToken);
        await customization.SetSettingsAsync(tenant.Id, Modules.TenantSettings, new JsonObject { ["primaryColor"] = "#7b3fa0", ["logoUrl"] = PlusLogo }, tenantAdmin: false, Actor, cancellationToken);
        await customization.SetFeatureAsync(tenant.Id, PurchaseRequestFeatures.DraftSharing, true, Actor, cancellationToken);
    }

    private async Task SeedTenantAsync(string slug, bool withCostCenters, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var tenant = await scope.ServiceProvider.GetRequiredService<ITenantDirectory>().FindBySlugAsync(slug, cancellationToken) ?? throw new InvalidOperationException($"{slug} is missing.");
        scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(tenant);
        var db = scope.ServiceProvider.GetRequiredService<PurchaseRequestDbContext>();
        if (await db.Requests.AnyAsync(cancellationToken))
            return;   // already seeded

        var budgets = withCostCenters ? scope.ServiceProvider.GetRequiredService<BudgetService>() : null;
        if (withCostCenters)
        {
            // Rung 4: the cost center field, required, chosen from a list.
            await scope.ServiceProvider.GetRequiredService<CustomFieldService>().SaveAsync(
                Modules.PurchaseRequest, WellKnownCustomFields.CostCenter, new SaveCustomFieldInput("Cost center", "list", true, CostCenters, 1), cancellationToken);
            // Rung 7: finance's budgets.
            await budgets!.SaveAsync("CC-100", null, new SaveBudgetInput("Facilities", 10_000m), cancellationToken);
            await budgets.SaveAsync("CC-200", null, new SaveBudgetInput("Marketing", 5_000m), cancellationToken);
            await budgets.SaveAsync("CC-300", null, new SaveBudgetInput("IT", 20_000m), cancellationToken);
        }

        var sequences = scope.ServiceProvider.GetRequiredService<ISequenceService>();
        var format = new PurchaseRequestSettings().NumberFormat;
        var now = TimeProvider.System.GetUtcNow().UtcDateTime;
        var businessDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(now, TimeZoneInfo.FindSystemTimeZoneById(tenant.TimeZone)));

        async Task AddAsync(string title, string item, decimal quantity, decimal price, string? costCenter, Action<Acme.PurchaseRequest.Domain.PurchaseRequest> advance)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var number = NumberFormatter.Format(format, businessDate, await sequences.NextAsync(db, NumberFormatter.CounterName(format, businessDate), cancellationToken));
            var request = new Acme.PurchaseRequest.Domain.PurchaseRequest(number, SampleRequester, title, "Sample data from scripts/dev-up", businessDate, tenant.Currency);
            request.ReplaceDetails(title, "Sample data from scripts/dev-up", [new RequestLine(item, quantity, price)]);
            if (costCenter is not null)
                request.ReplaceCustomFields(new Dictionary<string, string> { [WellKnownCustomFields.CostCenter] = costCenter });
            advance(request);
            db.Requests.Add(request);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            if (budgets is not null && request.Status == RequestStatus.Approved && costCenter is not null)
                await budgets.UseAsync(request.Id, request.Number, costCenter, request.TotalAmount, request.Currency, cancellationToken);
        }

        if (!withCostCenters)
        {
            await AddAsync("Office chairs", "Ergonomic chair", 4, 350m, null, _ => { });
            await AddAsync("Printer toner", "Toner cartridge", 2, 120m, null, r => { r.Submit(now); r.AutoApprove(now); });
            await AddAsync("Laptops for new hires", "Laptop", 3, 4500m, null, r => r.Submit(now));
            await AddAsync("Conference catering", "Catering package", 1, 800m, null, r => { r.Submit(now); r.Reject(SampleApprover, "Over budget this quarter.", now); });
        }
        else
        {
            await AddAsync("Standing desks", "Standing desk", 10, 600m, "CC-100", r => r.Submit(now));
            await AddAsync("Monitors", "27-inch monitor", 5, 450m, "CC-200", r => { r.Submit(now); r.Approve(1, SampleApprover, now, _ => null); });
            await AddAsync("Team workshop", "Workshop day", 1, 900m, "CC-300", _ => { });
            await AddAsync("Software licences", "Annual licence", 20, 90m, "CC-100", r => r.Submit(now));
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "INVITATION {Tenant} {Url}")]
    private static partial void LogInvitation(ILogger logger, string tenant, string url);

    [LoggerMessage(Level = LogLevel.Information, Message = "Demo data is ready")]
    private static partial void LogDone(ILogger logger);
}

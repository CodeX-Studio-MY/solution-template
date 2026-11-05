using System.Security.Claims;
using System.Text.Json.Nodes;
using Saaskit.Core.Authorization;
using Saaskit.Core.Errors;
using Saaskit.Core.Http;
using Saaskit.Core.Platform;
using Saaskit.Core.Security;
using Saaskit.Core.Tenancy;
using Saaskit.Tenancy.Application;
using Saaskit.Tenancy.Domain;
using Saaskit.Tenancy.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Saaskit.Tenancy.Api;

internal static class TenancyEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var v1 = api.MapGroup("/v1");
        MapTenantEndpoints(v1);
        MapOperatorEndpoints(v1.MapGroup("/operator").WithTags("operator"));
    }

    /// <summary>What the signed-in tenant sees: its modules for the shell, and who has support access.</summary>
    private static void MapTenantEndpoints(RouteGroupBuilder v1)
    {
        // Any signed-in principal of the tenant: the shell builds its remotes and menu from this.
        v1.MapGet("/me/modules", (IModuleManifestProvider manifest, HttpResponse response, CancellationToken ct) =>
        {
            response.Headers.CacheControl = "no-cache";
            return manifest.ForCurrentTenantAsync(ct);
        });
        // What the frontend needs to look and behave like this tenant: branding, every flag's value, and the UI-visible settings.
        v1.MapGet("/me/settings", (ITenantContext tenant, ITenantCustomization customization, HttpResponse response, CancellationToken ct) =>
        {
            response.Headers.CacheControl = "no-cache";
            return customization.GetUiSettingsAsync(tenant.Tenant.Id, ct);
        });
        MapSettingsEndpoints(v1);
        v1.MapGet("/me/tenant", async (ITenantContext tenant, ITenantAdmin admin, CancellationToken ct) => await admin.GetAsync(tenant.Tenant.Id, ct))
            .RequireModuleRole(Modules.TenantSettings, Role.User);
        v1.MapGet("/me/activity", (ITenantContext tenant, ITenantAdmin admin, [AsParameters] PageRequest page, CancellationToken ct) => admin.ListActivityAsync(tenant.Tenant.Id, page, ct))
            .RequireModuleRole(Modules.TenantSettings, Role.Admin);
        v1.MapPut("/me/tenant/profile", (ITenantContext tenant, [FromBody] TenantProfile body, ICurrentPrincipal who, ITenantAdmin admin, CancellationToken ct) =>
            admin.UpdateProfileAsync(tenant.Tenant.Id, body, who.Id ?? "unknown", ct)).RequireModuleRole(Modules.TenantSettings, Role.Admin);

        var support = v1.MapGroup("/support-access").WithTags("support-access");
        support.MapGet("", (ITenantContext tenant, ISupportAccess access, CancellationToken ct) => access.ListForTenantAsync(tenant.Tenant.Id, ct))
            .RequireModuleRole(Modules.TenantSettings, Role.Admin);
        support.MapPost("/{id:guid}/approve", (Guid id, ITenantContext tenant, ICurrentPrincipal who, ISupportAccess access, CancellationToken ct) =>
            access.DecideAsync(tenant.Tenant.Id, id, SupportAccessStatus.Approved, who.Id ?? "unknown", ct)).RequireModuleRole(Modules.TenantSettings, Role.Admin);
        support.MapPost("/{id:guid}/deny", (Guid id, ITenantContext tenant, ICurrentPrincipal who, ISupportAccess access, CancellationToken ct) =>
            access.DecideAsync(tenant.Tenant.Id, id, SupportAccessStatus.Denied, who.Id ?? "unknown", ct)).RequireModuleRole(Modules.TenantSettings, Role.Admin);
        support.MapPost("/{id:guid}/revoke", (Guid id, ITenantContext tenant, ICurrentPrincipal who, ISupportAccess access, CancellationToken ct) =>
            access.DecideAsync(tenant.Tenant.Id, id, SupportAccessStatus.Revoked, who.Id ?? "unknown", ct)).RequireModuleRole(Modules.TenantSettings, Role.Admin);
    }

    /// <summary>
    /// A module's settings (rung 1): its managers read them and its admins change the ones marked editable. A tenant-settings admin
    /// may do both for every module. Flags are read-only here.
    /// </summary>
    private static void MapSettingsEndpoints(RouteGroupBuilder v1)
    {
        var settings = v1.MapGroup("/settings").WithTags("settings");
        // Only the modules the caller may read: the shell shows a module's Settings entry from this list.
        settings.MapGet("", async (ITenantContext tenant, IAccessEvaluator access, ITenantCustomization customization, CancellationToken ct) =>
            (await customization.GetAllSettingsAsync(tenant.Tenant.Id, ct)).Where(view => MayConfigure(access, view.ModuleKey, Role.Manager)).ToList());
        settings.MapGet("/{moduleKey}", (string moduleKey, ITenantContext tenant, IAccessEvaluator access, ITenantCustomization customization, CancellationToken ct) =>
            MayConfigure(access, moduleKey, Role.Manager) ? customization.GetSettingsAsync(tenant.Tenant.Id, moduleKey, ct) : throw new ForbiddenException());
        settings.MapPut("/{moduleKey}", (string moduleKey, [FromBody] JsonObject changes, ITenantContext tenant, ICurrentPrincipal who, IAccessEvaluator access, ITenantCustomization customization, CancellationToken ct) =>
            MayConfigure(access, moduleKey, Role.Admin)
                ? customization.SetSettingsAsync(tenant.Tenant.Id, moduleKey, changes, tenantAdmin: true, who.Id ?? "unknown", ct)
                : throw new ForbiddenException());
        v1.MapGet("/features", (ITenantContext tenant, ITenantCustomization customization, CancellationToken ct) => customization.GetFeaturesAsync(tenant.Tenant.Id, ct))
            .RequireModuleRole(Modules.TenantSettings, Role.Admin);
    }

    /// <summary>The operator console's API: tenant lifecycle and support access. Signed in with <c>Authorization: Operator …</c>.</summary>
    private static void MapOperatorEndpoints(RouteGroupBuilder op)
    {
        op.MapPost("/sessions", async ([FromBody] SignInBody body, IOperatorSessions sessions, CancellationToken ct) =>
            await sessions.SignInAsync(body.Email, body.Password, body.TotpCode, ct)).AllowAnonymous().WithMetadata(AuthRateLimited.Instance);

        var secured = op.MapGroup("").RequireAuthorization(OperatorAuthenticationHandler.Policy);
        secured.MapGet("/me", (ClaimsPrincipal user) => Identity(user));
        secured.MapGet("/tenants", (ITenantAdmin admin, CancellationToken ct) => admin.ListAsync(ct));
        secured.MapGet("/tenants/{id:guid}", (Guid id, ITenantAdmin admin, CancellationToken ct) => admin.GetAsync(id, ct));
        secured.MapPost("/tenants", async ([FromBody] OnboardRequest body, IOnboarding onboarding, CancellationToken ct) =>
        {
            var result = await onboarding.OnboardAsync(body, ct);
            return TypedResults.Created($"{PlatformServices.Tenancy.RoutePrefix}/v1/operator/tenants/{result.TenantId}", result);
        });
        secured.MapPut("/tenants/{id:guid}/plan", (Guid id, [FromBody] PlanBody body, ClaimsPrincipal user, ITenantAdmin admin, CancellationToken ct) =>
            admin.ChangePlanAsync(id, body.Plan, Actor(user), ct));
        secured.MapGet("/tenants/{id:guid}/activity", (Guid id, ITenantAdmin admin, [AsParameters] PageRequest page, CancellationToken ct) => admin.ListActivityAsync(id, page, ct));
        secured.MapPut("/tenants/{id:guid}/profile", (Guid id, [FromBody] TenantProfile body, ClaimsPrincipal user, ITenantAdmin admin, CancellationToken ct) =>
            admin.UpdateProfileAsync(id, body, Actor(user), ct));
        secured.MapPut("/tenants/{id:guid}/modules/{module}", (Guid id, string module, ClaimsPrincipal user, ITenantAdmin admin, CancellationToken ct) =>
            admin.GrantModuleAsync(id, module, Actor(user), ct));
        secured.MapDelete("/tenants/{id:guid}/modules/{module}", (Guid id, string module, ClaimsPrincipal user, ITenantAdmin admin, CancellationToken ct) =>
            admin.RevokeModuleAsync(id, module, Actor(user), ct));
        // Rungs 1 and 2: any setting and any flag, for any tenant.
        secured.MapGet("/tenants/{id:guid}/settings", (Guid id, ITenantCustomization customization, CancellationToken ct) => customization.GetAllSettingsAsync(id, ct));
        secured.MapPut("/tenants/{id:guid}/settings/{moduleKey}", (Guid id, string moduleKey, [FromBody] JsonObject changes, ClaimsPrincipal user, ITenantCustomization customization, CancellationToken ct) =>
            customization.SetSettingsAsync(id, moduleKey, changes, tenantAdmin: false, Actor(user), ct));
        secured.MapGet("/tenants/{id:guid}/features", (Guid id, ITenantCustomization customization, CancellationToken ct) => customization.GetFeaturesAsync(id, ct));
        secured.MapPut("/tenants/{id:guid}/features/{key}", (Guid id, string key, [FromBody] FeatureBody body, ClaimsPrincipal user, ITenantCustomization customization, CancellationToken ct) =>
            customization.SetFeatureAsync(id, key, body.Enabled, Actor(user), ct));
        secured.MapPut("/tenants/{id:guid}/support-approval", (Guid id, [FromBody] RequiredBody body, ClaimsPrincipal user, ITenantAdmin admin, CancellationToken ct) =>
            admin.SetSupportApprovalAsync(id, body.Required, Actor(user), ct));
        secured.MapPost("/tenants/{id:guid}/suspend", (Guid id, ClaimsPrincipal user, ITenantLifecycle lifecycle, CancellationToken ct) => lifecycle.SuspendAsync(id, Actor(user), ct));
        secured.MapPost("/tenants/{id:guid}/resume", (Guid id, ClaimsPrincipal user, ITenantLifecycle lifecycle, CancellationToken ct) => lifecycle.ResumeAsync(id, Actor(user), ct));
        secured.MapPost("/tenants/{id:guid}/offboard", (Guid id, ClaimsPrincipal user, ITenantLifecycle lifecycle, CancellationToken ct) => lifecycle.BeginOffboardingAsync(id, Actor(user), ct));
        secured.MapPost("/tenants/{id:guid}/cancel-offboarding", (Guid id, ClaimsPrincipal user, ITenantLifecycle lifecycle, CancellationToken ct) => lifecycle.CancelOffboardingAsync(id, Actor(user), ct));
        secured.MapPost("/tenants/{id:guid}/complete-deletion", (Guid id, ClaimsPrincipal user, ITenantLifecycle lifecycle, CancellationToken ct) => lifecycle.CompleteDeletionAsync(id, Actor(user), ct));

        secured.MapPost("/support-access", (ClaimsPrincipal user, [FromBody] SupportAccessRequest body, ISupportAccess access, CancellationToken ct) =>
            access.RequestAsync(Identity(user), body, ct));
        secured.MapPost("/support-access/{id:guid}/token", (Guid id, ClaimsPrincipal user, ISupportAccess access, CancellationToken ct) =>
            access.IssueTokenAsync(Identity(user), id, ct));

        MapPrincipalEndpoints(secured);
    }

    /// <summary>The catalog's users, devices and applications, and which tenants each may access.</summary>
    private static void MapPrincipalEndpoints(RouteGroupBuilder secured)
    {
        secured.MapGet("/principals", (PlatformPrincipalType? type, string? search, [AsParameters] PageRequest page, IPrincipalDirectory directory, CancellationToken ct) =>
            directory.ListAsync(type, search, page, ct));
        secured.MapPost("/principals", async ([FromBody] PrincipalInput body, ClaimsPrincipal user, IPrincipalDirectory directory, HttpResponse response, CancellationToken ct) =>
        {
            var principal = await directory.CreateAsync(body, ActorId(user), ct);
            response.Headers.ETag = principal.ETag;
            return TypedResults.Created($"{PlatformServices.Tenancy.RoutePrefix}/v1/operator/principals/{principal.PrincipalId}", principal);
        });
        secured.MapGet("/principals/{id:guid}", async (Guid id, IPrincipalDirectory directory, HttpResponse response, CancellationToken ct) =>
        {
            var principal = await directory.GetAsync(id, ct);
            response.Headers.ETag = principal.ETag;
            return principal;
        });
        secured.MapPut("/principals/{id:guid}", async (Guid id, [FromBody] PrincipalInput body, [FromHeader(Name = "If-Match")] string? ifMatch, ClaimsPrincipal user, IPrincipalDirectory directory, HttpResponse response, CancellationToken ct) =>
        {
            var principal = await directory.UpdateAsync(id, body, ifMatch, ActorId(user), ct);
            response.Headers.ETag = principal.ETag;
            return principal;
        });
        secured.MapPut("/principals/{id:guid}/active", async (Guid id, [FromBody] ActiveBody body, [FromHeader(Name = "If-Match")] string? ifMatch, ClaimsPrincipal user, IPrincipalDirectory directory, HttpResponse response, CancellationToken ct) =>
        {
            var principal = await directory.SetActiveAsync(id, body.Active, ifMatch, ActorId(user), ct);
            response.Headers.ETag = principal.ETag;
            return principal;
        });
        secured.MapGet("/principals/{id:guid}/access-audit", (Guid id, Guid? tenantId, [AsParameters] PageRequest page, IPrincipalDirectory directory, CancellationToken ct) =>
            directory.ListAuditForPrincipalAsync(id, tenantId, page, ct));

        secured.MapGet("/tenants/{id:guid}/principals", (Guid id, [AsParameters] PageRequest page, IPrincipalDirectory directory, CancellationToken ct) =>
            directory.ListForTenantAsync(id, page, ct));
        secured.MapPut("/tenants/{id:guid}/principals/{principalId:guid}", (Guid id, Guid principalId, [FromBody] GrantBody body, ClaimsPrincipal user, IPrincipalDirectory directory, CancellationToken ct) =>
            directory.GrantAsync(id, principalId, body.Reason, ActorId(user), ct));
        secured.MapPut("/tenants/{id:guid}/principals/{principalId:guid}/status", (Guid id, Guid principalId, [FromBody] AccessStatusBody body, ClaimsPrincipal user, IPrincipalDirectory directory, CancellationToken ct) =>
            directory.SetStatusAsync(id, principalId, body.Status, body.Reason, ActorId(user), ct));
        secured.MapGet("/tenants/{id:guid}/principal-access-audit", (Guid id, [AsParameters] PageRequest page, IPrincipalDirectory directory, CancellationToken ct) =>
            directory.ListAuditForTenantAsync(id, page, ct));
        secured.MapGet("/tenants/{id:guid}/principal-logins", (Guid id, [AsParameters] PageRequest page, IPrincipalDirectory directory, CancellationToken ct) =>
            directory.ListLoginsAsync(id, page, ct));
    }

    /// <summary>At least <paramref name="role"/> in the module itself, or admin in tenant-settings (who configures every module).</summary>
    private static bool MayConfigure(IAccessEvaluator access, string moduleKey, Role role) =>
        access.HasRole(moduleKey, role) || access.HasRole(Modules.TenantSettings, Role.Admin);

    private static string Actor(ClaimsPrincipal user) => user.FindFirstValue(OperatorAuthenticationHandler.OperatorIdClaim) ?? "unknown";

    private static Guid ActorId(ClaimsPrincipal user) => Guid.Parse(Actor(user));

    private static OperatorIdentity Identity(ClaimsPrincipal user) =>
        new(Guid.Parse(Actor(user)), user.FindFirstValue(ClaimTypes.Email) ?? "", user.FindFirstValue(ClaimTypes.Name) ?? "");

    internal sealed record SignInBody(string Email, string Password, string TotpCode);

    internal sealed record PlanBody(string Plan);

    internal sealed record RequiredBody(bool Required);

    internal sealed record FeatureBody(bool? Enabled);

    internal sealed record ActiveBody(bool Active);

    internal sealed record GrantBody(string? Reason);

    internal sealed record AccessStatusBody(TenantAccessStatus Status, string Reason);
}

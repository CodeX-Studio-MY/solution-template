using Saaskit.Core.Authorization;
using Saaskit.Core.Platform;
using Saaskit.Identity.Application;
using Saaskit.Identity.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Saaskit.Identity.Api;

/// <summary>Called by other platform services (Tenancy), never by users or tenants.</summary>
internal static class PlatformEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var platform = api.MapGroup("/v1/platform").WithTags("platform");
        platform.MapPost("/revoke-all-tokens", async (ITenantSecurity security, CancellationToken ct) => new { revoked = await security.RevokeAllTokensAsync(ct) })
            .RequirePlatformService();
        platform.MapPost("/support-tokens", async ([FromBody] SupportTokenBody body, ISupportTokenIssuer issuer, CancellationToken ct) =>
            await issuer.IssueAsync(new SupportTokenRequest(body.GrantId, body.OperatorId, body.OperatorName, body.Module, body.Role, body.Audience, body.Reason), ct))
            .RequirePlatformService();
        // Each step of a support-access request (requested, approved, denied, revoked) appears in the tenant's own security log.
        platform.MapPost("/support-access-events", async ([FromBody] SupportAccessEventBody body, IAuditWriter audit, CancellationToken ct) =>
        {
            await audit.WriteAsync(
                AuditKinds.SupportAccess,
                subject: body.GrantId.ToString(),
                details: new { stage = body.Stage, module = body.Module, role = body.Role.ToClaimValue(), reason = body.Reason, @operator = body.Operator },
                actorId: body.ActorId,
                actorType: body.ActorType,
                cancellationToken: ct);
            return Results.NoContent();
        }).RequirePlatformService();
        // The first admin of a new tenant: an emailed invitation, never a default password.
        platform.MapPost("/invitations", async ([FromBody] FirstAdminBody body, IAccountService accounts, CancellationToken ct) =>
        {
            var invitation = await accounts.InviteAsync(body.Email, body.DisplayName, body.Modules.Select(m => (m, Saaskit.Core.Platform.Role.Admin)), ct);
            return new { invitation.PrincipalId, invitation.ExpiresOn, url = invitation.Url, invitation.EmailSent };
        }).RequirePlatformService();
        platform.MapGet("/principals/{id:guid}/contact", async (Guid id, IAccessManagement access, CancellationToken ct) =>
            await access.GetContactAsync(id, ct) is { } contact ? Results.Ok(contact) : Results.NotFound()).RequirePlatformService();
        platform.MapGet("/module-members", (string module, Saaskit.Core.Platform.Role? minRole, IAccessManagement access, CancellationToken ct) =>
            access.ListModuleMembersAsync(module, minRole ?? Saaskit.Core.Platform.Role.Manager, ct)).RequirePlatformService();
        platform.MapPut("/bff-redirect-uris", async ([FromBody] RedirectUrisBody body, ITenantSecurity security, CancellationToken ct) =>
        {
            await security.SetBffRedirectUrisAsync(body.Add ?? [], body.Remove ?? [], ct);
            return Results.NoContent();
        }).RequirePlatformService();
    }

    internal sealed record SupportTokenBody(Guid GrantId, string OperatorId, string OperatorName, string Module, Saaskit.Core.Platform.Role Role, string Audience, string Reason);

    internal sealed record SupportAccessEventBody(string Stage, Guid GrantId, string ActorId, string ActorType, string Operator, string Module, Saaskit.Core.Platform.Role Role, string Reason);

    internal sealed record FirstAdminBody(string Email, string DisplayName, IReadOnlyList<string> Modules);

    internal sealed record RedirectUrisBody(IReadOnlyCollection<string>? Add, IReadOnlyCollection<string>? Remove);
}

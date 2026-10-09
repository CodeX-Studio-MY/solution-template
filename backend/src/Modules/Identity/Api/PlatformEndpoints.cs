using Acme.Core.Authorization;
using Acme.Identity.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Acme.Identity.Api;

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
        // The first admin of a new tenant: an emailed invitation, never a default password.
        platform.MapPost("/invitations", async ([FromBody] FirstAdminBody body, IAccountService accounts, CancellationToken ct) =>
        {
            var invitation = await accounts.InviteAsync(body.Email, body.DisplayName, body.Modules.Select(m => (m, Acme.Core.Platform.Role.Admin)), ct);
            return new { invitation.PrincipalId, invitation.ExpiresOn, url = invitation.Url };
        }).RequirePlatformService();
        platform.MapGet("/principals/{id:guid}/contact", async (Guid id, IAccessManagement access, CancellationToken ct) =>
            await access.GetContactAsync(id, ct) is { } contact ? Results.Ok(contact) : Results.NotFound()).RequirePlatformService();
        platform.MapGet("/module-members", (string module, Acme.Core.Platform.Role? minRole, IAccessManagement access, CancellationToken ct) =>
            access.ListModuleMembersAsync(module, minRole ?? Acme.Core.Platform.Role.Manager, ct)).RequirePlatformService();
        platform.MapPut("/bff-redirect-uris", async ([FromBody] RedirectUrisBody body, ITenantSecurity security, CancellationToken ct) =>
        {
            await security.SetBffRedirectUrisAsync(body.Add ?? [], body.Remove ?? [], ct);
            return Results.NoContent();
        }).RequirePlatformService();
    }

    internal sealed record SupportTokenBody(Guid GrantId, string OperatorId, string OperatorName, string Module, Acme.Core.Platform.Role Role, string Audience, string Reason);

    internal sealed record FirstAdminBody(string Email, string DisplayName, IReadOnlyList<string> Modules);

    internal sealed record RedirectUrisBody(IReadOnlyCollection<string>? Add, IReadOnlyCollection<string>? Remove);
}

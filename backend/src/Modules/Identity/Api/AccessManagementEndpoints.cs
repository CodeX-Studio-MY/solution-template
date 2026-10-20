using Farm360.Core.Authorization;
using Farm360.Core.Platform;
using Farm360.Core.Security;
using Farm360.Identity.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Farm360.Identity.Api;

/// <summary>Users, machines, services, groups and module roles. Reads need <c>manager</c>, writes <c>admin</c> in access-management.</summary>
internal static class AccessManagementEndpoints
{
    private const string Module = Modules.AccessManagement;

    public static void Map(IEndpointRouteBuilder api)
    {
        var v1 = api.MapGroup("/v1");

        var principals = v1.MapGroup("/principals").WithTags("principals");
        principals.MapGet("", (IAccessManagement access, PrincipalType? type, CancellationToken ct) => access.ListPrincipalsAsync(type, ct))
            .RequireModuleRole(Module, Role.Manager);
        principals.MapGet("/{id:guid}", async (Guid id, IAccessManagement access, HttpResponse response, CancellationToken ct) =>
        {
            var principal = await access.GetPrincipalAsync(id, ct);
            response.Headers.ETag = principal.ETag;
            return principal;
        }).RequireModuleRole(Module, Role.Manager);
        principals.MapPut("/{id:guid}/active", async (Guid id, [FromBody] ActiveBody body, [FromHeader(Name = "If-Match")] string? ifMatch, IAccessManagement access, HttpResponse response, CancellationToken ct) =>
        {
            var principal = await access.SetPrincipalActiveAsync(id, body.Active, ifMatch, ct);
            response.Headers.ETag = principal.ETag;
            return principal;
        }).RequireModuleRole(Module, Role.Admin);
        principals.MapGet("/{id:guid}/access", (Guid id, IAccessManagement access, CancellationToken ct) => access.GetEffectiveAccessAsync(id, ct))
            .RequireModuleRole(Module, Role.Manager);
        principals.MapGet("/{id:guid}/assignments", (Guid id, IAccessManagement access, CancellationToken ct) => access.ListPrincipalAssignmentsAsync(id, ct))
            .RequireModuleRole(Module, Role.Manager);
        principals.MapPut("/{id:guid}/assignments/{module}", (Guid id, string module, [FromBody] RoleBody body, IAccessManagement access, CancellationToken ct) =>
            access.SetPrincipalRoleAsync(id, module, body.Role, ct)).RequireModuleRole(Module, Role.Admin);
        principals.MapDelete("/{id:guid}/assignments/{module}", async (Guid id, string module, IAccessManagement access, CancellationToken ct) =>
        {
            await access.RemovePrincipalRoleAsync(id, module, ct);
            return Results.NoContent();
        }).RequireModuleRole(Module, Role.Admin);
        principals.MapPost("/{id:guid}/keys", async (Guid id, [FromBody] KeyBody body, IAccessManagement access, CancellationToken ct) =>
        {
            await access.AddKeyAsync(id, body.PublicKeyJwk, ct);
            return Results.NoContent();
        }).RequireModuleRole(Module, Role.Admin);
        principals.MapDelete("/{id:guid}/keys/{keyId}", async (Guid id, string keyId, IAccessManagement access, CancellationToken ct) =>
        {
            await access.RemoveKeyAsync(id, keyId, ct);
            return Results.NoContent();
        }).RequireModuleRole(Module, Role.Admin);

        v1.MapPost("/users/invitations", async ([FromBody] InviteBody body, IAccountService accounts, CancellationToken ct) =>
        {
            var invitation = await accounts.InviteAsync(body.Email, body.DisplayName, body.Roles.Select(r => (r.Module, r.Role)), ct);
            return Results.Created($"{PlatformServices.Identity.RoutePrefix}/v1/principals/{invitation.PrincipalId}", new { invitation.PrincipalId, invitation.ExpiresOn });
        }).RequireModuleRole(Module, Role.Admin);

        var machines = v1.MapGroup("/machines").WithTags("machines");
        machines.MapPost("", async ([FromBody] NameBody body, IAccessManagement access, CancellationToken ct) =>
            Results.Created("", await access.CreateMachineAsync(body.DisplayName, ct))).RequireModuleRole(Module, Role.Admin);
        machines.MapPost("/{id:guid}/enrollment", (Guid id, IAccessManagement access, CancellationToken ct) => access.NewEnrollmentCodeAsync(id, ct))
            .RequireModuleRole(Module, Role.Admin);
        // The device redeems its one-time code; the code is the credential. Rate limited, anonymous.
        machines.MapPost("/enroll", ([FromBody] EnrollBody body, IAccessManagement access, CancellationToken ct) => access.EnrollAsync(body.Code, body.PublicKeyJwk, ct))
            .AllowAnonymous().WithMetadata(AuthRateLimited.Instance);

        v1.MapPost("/services", async ([FromBody] ServiceBody body, IAccessManagement access, CancellationToken ct) =>
            Results.Created("", await access.CreateServiceAsync(body.DisplayName, new ClientCredential(body.PublicKeyJwk, body.Secret), ct))).RequireModuleRole(Module, Role.Admin);

        var groups = v1.MapGroup("/groups").WithTags("groups");
        groups.MapGet("", (IAccessManagement access, CancellationToken ct) => access.ListGroupsAsync(ct)).RequireModuleRole(Module, Role.Manager);
        groups.MapPost("", async ([FromBody] NameBody body, IAccessManagement access, HttpResponse response, CancellationToken ct) =>
        {
            var group = await access.CreateGroupAsync(body.DisplayName, ct);
            response.Headers.ETag = group.ETag;
            return Results.Created($"{PlatformServices.Identity.RoutePrefix}/v1/groups/{group.Id}", group);
        }).RequireModuleRole(Module, Role.Admin);
        groups.MapPut("/{id:guid}", async (Guid id, [FromBody] NameBody body, [FromHeader(Name = "If-Match")] string? ifMatch, IAccessManagement access, HttpResponse response, CancellationToken ct) =>
        {
            var group = await access.RenameGroupAsync(id, body.DisplayName, ifMatch, ct);
            response.Headers.ETag = group.ETag;
            return group;
        }).RequireModuleRole(Module, Role.Admin);
        groups.MapDelete("/{id:guid}", async (Guid id, IAccessManagement access, CancellationToken ct) =>
        {
            await access.DeleteGroupAsync(id, ct);
            return Results.NoContent();
        }).RequireModuleRole(Module, Role.Admin);
        groups.MapPut("/{id:guid}/members/{principalId:guid}", async (Guid id, Guid principalId, IAccessManagement access, CancellationToken ct) =>
        {
            await access.AddMemberAsync(id, principalId, ct);
            return Results.NoContent();
        }).RequireModuleRole(Module, Role.Admin);
        groups.MapDelete("/{id:guid}/members/{principalId:guid}", async (Guid id, Guid principalId, IAccessManagement access, CancellationToken ct) =>
        {
            await access.RemoveMemberAsync(id, principalId, ct);
            return Results.NoContent();
        }).RequireModuleRole(Module, Role.Admin);
        groups.MapGet("/{id:guid}/assignments", (Guid id, IAccessManagement access, CancellationToken ct) => access.ListGroupAssignmentsAsync(id, ct))
            .RequireModuleRole(Module, Role.Manager);
        groups.MapPut("/{id:guid}/assignments/{module}", (Guid id, string module, [FromBody] RoleBody body, IAccessManagement access, CancellationToken ct) =>
            access.SetGroupRoleAsync(id, module, body.Role, ct)).RequireModuleRole(Module, Role.Admin);
        groups.MapDelete("/{id:guid}/assignments/{module}", async (Guid id, string module, IAccessManagement access, CancellationToken ct) =>
        {
            await access.RemoveGroupRoleAsync(id, module, ct);
            return Results.NoContent();
        }).RequireModuleRole(Module, Role.Admin);
    }

    internal sealed record ActiveBody(bool Active);

    internal sealed record RoleBody(Role Role);

    internal sealed record NameBody(string DisplayName);

    internal sealed record KeyBody(string PublicKeyJwk);

    internal sealed record EnrollBody(string Code, string PublicKeyJwk);

    internal sealed record ServiceBody(string DisplayName, string? PublicKeyJwk, string? Secret);

    internal sealed record InviteRole(string Module, Role Role);

    internal sealed record InviteBody(string Email, string DisplayName, IReadOnlyList<InviteRole> Roles);
}

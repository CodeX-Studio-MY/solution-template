using Farm360.Core.Errors;
using Farm360.Core.Security;
using Farm360.Core.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Farm360.Core.Authorization;

/// <summary>Handles <see cref="ModuleRoleRequirement"/>, <see cref="ModulePermission"/> and <see cref="PrincipalTypeRequirement"/>.</summary>
internal sealed class PlatformAuthorizationHandler(
    IAccessEvaluator access,
    ITenantContext tenant,
    IHttpContextAccessor http) : IAuthorizationHandler
{
    public Task HandleAsync(AuthorizationHandlerContext context)
    {
        foreach (var requirement in context.PendingRequirements.ToList())
        {
            switch (requirement)
            {
                case ModuleRoleRequirement moduleRole:
                    Check(context, requirement, moduleRole.Module, access.HasRole(moduleRole.Module, moduleRole.Role));
                    break;
                case ModulePermission permission:
                    Check(context, requirement, permission.Module, access.Can(permission));
                    break;
                case PlatformServiceRequirement:
                    if (context.User.GetPrincipalType() == PrincipalType.Service && context.User.HasPermission(PlatformPermissions.TenantAny))
                        context.Succeed(requirement);
                    else
                        context.Fail(new AuthorizationFailureReason(this, PlatformErrors.Forbidden.Code));
                    break;
                case PrincipalTypeRequirement types:
                    if (context.User.GetPrincipalType() is { } type && types.Types.Contains(type))
                        context.Succeed(requirement);
                    else
                        context.Fail(new AuthorizationFailureReason(this, PlatformErrors.PrincipalTypeNotAllowed.Code));
                    break;
            }
        }
        return Task.CompletedTask;
    }

    private void Check(AuthorizationHandlerContext context, IAuthorizationRequirement requirement, string module, bool allowed)
    {
        if (!tenant.HasTenant)
        {
            context.Fail(new AuthorizationFailureReason(this, PlatformErrors.TenantUnavailable.Code));
            return;
        }
        if (!tenant.Tenant.EnabledModules.Contains(module))
        {
            context.Fail(new AuthorizationFailureReason(this, PlatformErrors.ModuleNotEntitled.Code));
            return;
        }
        if (!allowed)
        {
            context.Fail(new AuthorizationFailureReason(this, PlatformErrors.Forbidden.Code));
            return;
        }
        var method = http.HttpContext?.Request.Method ?? HttpMethods.Get;
        var isWrite = !(HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method));
        if (isWrite && !tenant.Tenant.CanWrite)
        {
            context.Fail(new AuthorizationFailureReason(this, PlatformErrors.TenantReadOnly.Code));
            return;
        }
        context.Succeed(requirement);
    }
}

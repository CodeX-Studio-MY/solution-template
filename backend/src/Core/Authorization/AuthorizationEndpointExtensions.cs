using Acme.Core.Platform;
using Acme.Core.Security;
using Microsoft.AspNetCore.Builder;

namespace Acme.Core.Authorization;

public static class AuthorizationEndpointExtensions
{
    /// <summary>Minimal API form of <see cref="ModuleRoleAttribute"/>.</summary>
    public static TBuilder RequireModuleRole<TBuilder>(this TBuilder builder, string module, Role role)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new ModuleRoleAttribute(module, role));

    /// <summary>Requires a module permission (its minimum role in its module, plus entitlement and write checks).</summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, ModulePermission permission)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new PermissionRequirementData(permission));

    /// <summary>Minimal API form of <see cref="AllowPrincipalTypesAttribute"/>.</summary>
    public static TBuilder AllowPrincipalTypes<TBuilder>(this TBuilder builder, params PrincipalType[] types)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new AllowPrincipalTypesAttribute(types));

    /// <summary>Restricts the endpoint to platform service principals (our own services, acting for any tenant).</summary>
    public static TBuilder RequirePlatformService<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new PlatformServiceRequirementData());

    private sealed class PlatformServiceRequirementData : Microsoft.AspNetCore.Authorization.IAuthorizationRequirementData
    {
        public IEnumerable<Microsoft.AspNetCore.Authorization.IAuthorizationRequirement> GetRequirements() => [new PlatformServiceRequirement()];
    }

    private sealed class PermissionRequirementData(ModulePermission permission) : Microsoft.AspNetCore.Authorization.IAuthorizationRequirementData
    {
        public IEnumerable<Microsoft.AspNetCore.Authorization.IAuthorizationRequirement> GetRequirements() => [permission];
    }
}

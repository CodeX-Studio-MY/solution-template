using Saaskit.Core.Platform;
using Saaskit.Core.Security;
using Microsoft.AspNetCore.Authorization;

namespace Saaskit.Core.Authorization;

/// <summary>
/// "At least <paramref name="role"/> in <paramref name="module"/>". Also requires the tenant to be entitled to the module
/// (checked live, not only from the token) and refuses writes while the tenant is suspended.
/// Compares role levels; never use <c>User.IsInRole("module:role")</c>, which is an exact match.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class ModuleRoleAttribute(string module, Role role) : Attribute, IAuthorizationRequirementData
{
    public string Module { get; } = module;

    public Role Role { get; } = role;

    public IEnumerable<IAuthorizationRequirement> GetRequirements() => [new ModuleRoleRequirement(Module, Role)];
}

/// <summary>Restricts an endpoint to some principal types. Without it, any type with the required role may call.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AllowPrincipalTypesAttribute(params PrincipalType[] types) : Attribute, IAuthorizationRequirementData
{
    public IReadOnlyList<PrincipalType> Types { get; } = types;

    public IEnumerable<IAuthorizationRequirement> GetRequirements() => [new PrincipalTypeRequirement(Types)];
}

public sealed record ModuleRoleRequirement(string Module, Role Role) : IAuthorizationRequirement;

public sealed record PrincipalTypeRequirement(IReadOnlyList<PrincipalType> Types) : IAuthorizationRequirement;

/// <summary>
/// A finer rule declared in the owning module's code as a permission-to-minimum-role map
/// (for example <c>Approve => Role.Manager</c>). Permissions never go in the token.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711", Justification = "\"Permission\" is the domain term (section 4.5); this is not a code-access-security permission.")]
public sealed record ModulePermission(string Module, string Name, Role MinimumRole) : IAuthorizationRequirement
{
    public override string ToString() => $"{Module}.{Name}";
}

/// <summary>Only platform service principals (a service with the <c>tenant:any</c> permission) may call. Used by platform-to-platform endpoints.</summary>
public sealed record PlatformServiceRequirement : IAuthorizationRequirement;

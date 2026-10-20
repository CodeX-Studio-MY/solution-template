using System.Diagnostics.CodeAnalysis;

namespace Farm360.Core.Platform;

public static class RoleExtensions
{
    /// <summary>The lowercase name used in tokens, for example <c>manager</c>.</summary>
    public static string ToClaimValue(this Role role) => Roles.Name(role);

    /// <summary>"At least this role": a higher role includes everything a lower one can do.</summary>
    public static bool Includes(this Role held, Role required) => held >= required;

    /// <summary>Parses a <c>{module-key}:{role}</c> entry from the <c>roles</c> claim.</summary>
    public static bool TryParseModuleRole(string value, [NotNullWhen(true)] out string? moduleKey, out Role role)
    {
        moduleKey = null;
        role = default;
        var separator = value.LastIndexOf(':');
        if (separator <= 0 || !Roles.TryParse(value[(separator + 1)..], out role))
            return false;
        moduleKey = value[..separator];
        return true;
    }

    /// <summary>Formats a <c>{module-key}:{role}</c> entry for the <c>roles</c> claim.</summary>
    public static string ToModuleRole(string moduleKey, Role role) => $"{moduleKey}:{Roles.Name(role)}";
}

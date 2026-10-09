using System.Text.RegularExpressions;

namespace Acme.Core.Platform;

/// <summary>
/// The format of every key: modules, services and tenant slugs. The generator validates <c>platform.yaml</c> with these
/// rules and the catalog enforces the same pattern with a check constraint, so there is one definition.
/// </summary>
public static partial class KeyRules
{
    public const string PatternText = "^[a-z][a-z0-9-]{1,29}$";

    public static Regex Pattern { get; } = KeyPattern();

    /// <summary>Keys no module or service may use: they are routes or hostnames of the platform itself.</summary>
    public static IReadOnlySet<string> Reserved { get; } = new SortedSet<string>(StringComparer.Ordinal) { "admin", "api", "bff", "health", "login", "mfe", "static" };

    /// <summary>Tenant slugs are subdomains, so a few more names are taken.</summary>
    public static IReadOnlySet<string> ReservedTenantSlugs { get; } = new SortedSet<string>(Reserved, StringComparer.Ordinal) { "operator", "www" };

    [GeneratedRegex(PatternText)]
    private static partial Regex KeyPattern();
}

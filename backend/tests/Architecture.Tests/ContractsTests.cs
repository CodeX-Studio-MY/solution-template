using System.Reflection;
using System.Text.RegularExpressions;
using Saaskit.Contracts;
using Saaskit.Core.Platform;

namespace Saaskit.Architecture.Tests;

/// <summary>What a module puts in Contracts (<c>backend/src/Contracts/Modules/{Pascal}</c>, namespace <c>Contracts.{Pascal}</c>) follows the platform's conventions.</summary>
public sealed partial class ContractsTests
{
    [GeneratedRegex(@"^V\d+$")]
    private static partial Regex VersionName();

    /// <summary>
    /// A module's events are named after the module, so one module can't claim another's, and each version is a nested type
    /// (<c>ItemCreated.V1</c>, never a rename of a published one).
    /// </summary>
    [Fact]
    public void A_modules_events_carry_its_key_and_are_nested_versions()
    {
        var contracts = Solution.Assemblies.Single(a => a.GetName().Name == $"{Solution.Product}.Contracts");
        var prefix = $"{Solution.Product}.Contracts.";
        var offenders = new List<string>();

        foreach (var type in contracts.GetTypes().Where(t => t.Namespace is not null && t.Namespace.StartsWith(prefix, StringComparison.Ordinal)))
        {
            var folder = type.Namespace![prefix.Length..];
            var module = Modules.All.SingleOrDefault(m => Solution.Pascal(m.Key) == folder);
            if (module is null)
            {
                offenders.Add($"{type.FullName}: no module in platform.yaml is called {folder}");
                continue;
            }
            if (type.GetCustomAttribute<IntegrationEventAttribute>() is not { } attribute)
                continue;
            if (type.DeclaringType is null || !VersionName().IsMatch(type.Name))
                offenders.Add($"{type.FullName}: an event is a nested record named V1, V2, ... inside a static class named for what happened");
            if (!attribute.Name.StartsWith($"{module.Key}.", StringComparison.Ordinal))
                offenders.Add($"{type.FullName}: the event name '{attribute.Name}' must start with '{module.Key}.'");
        }

        offenders.ShouldBeEmpty();
    }
}

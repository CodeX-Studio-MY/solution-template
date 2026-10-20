using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Farm360.Architecture.Tests;

/// <summary>Rules about what compiled code calls, read from IL.</summary>
public sealed class IlRuleTests
{
    [Fact]
    public void Only_the_Migrator_applies_migrations()
    {
        var offenders = Calls(
                (type, method) => type == "Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions" && method.StartsWith("Migrate", StringComparison.Ordinal))
            .Where(call => !call.Assembly.EndsWith(".Migrator", StringComparison.Ordinal));
        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void The_static_clock_is_never_used()
    {
        string[] banned = ["get_Now", "get_Today", "get_UtcNow"];
        var offenders = Calls((type, method) => type is "System.DateTime" or "System.DateTimeOffset" && banned.Contains(method));
        offenders.ShouldBeEmpty();
    }

    private static List<Call> Calls(Func<string, string, bool> match)
    {
        var calls = new List<Call>();
        foreach (var assembly in Solution.Assemblies)
        {
            using var module = ModuleDefinition.ReadModule(assembly.Location);
            foreach (var type in module.GetTypes())
            {
                foreach (var method in type.Methods.Where(m => m.HasBody))
                {
                    foreach (var instruction in method.Body.Instructions)
                    {
                        if (instruction.OpCode.Code is Code.Call or Code.Callvirt
                            && instruction.Operand is MethodReference target
                            && match(target.DeclaringType.FullName, target.Name))
                        {
                            calls.Add(new Call(assembly.GetName().Name!, $"{type.FullName}.{method.Name} → {target.DeclaringType.Name}.{target.Name}"));
                        }
                    }
                }
            }
        }
        return calls;
    }

    private sealed record Call(string Assembly, string Site);
}

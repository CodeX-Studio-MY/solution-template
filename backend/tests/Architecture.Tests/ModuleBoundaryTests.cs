using System.Reflection;
using Farm360.Core.Platform;
using NetArchTest.Rules;

namespace Farm360.Architecture.Tests;

public sealed class ModuleBoundaryTests
{
    public static TheoryData<string> Modules() => [.. Solution.ModuleAssemblies.Select(a => a.GetName().Name!)];

    [Theory]
    [MemberData(nameof(Modules))]
    public void A_module_references_no_other_module(string module)
    {
        var others = Solution.ModuleAssemblies.Select(a => a.GetName().Name!).Where(n => n != module).ToHashSet();
        var references = Load(module).GetReferencedAssemblies().Select(r => r.Name!);
        references.Where(others.Contains).ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void A_module_references_only_the_kernel_and_no_database_provider(string module)
    {
        var forbidden = new[]
        {
            $"{Solution.Product}.Core.Hosting",
            $"{Solution.Product}.Core.Data.SqlServer",
            $"{Solution.Product}.Core.Data.PostgreSql",
            "Microsoft.EntityFrameworkCore.SqlServer",
            "Microsoft.Data.SqlClient",
            "Npgsql",
            "Npgsql.EntityFrameworkCore.PostgreSQL",
        };
        Load(module).GetReferencedAssemblies().Select(r => r.Name!).Intersect(forbidden).ShouldBeEmpty();
    }

    /// <summary>Only Tenancy owns the tenant directory in the catalog; no other module may reference it.</summary>
    [Fact]
    public void Only_Tenancy_references_the_catalog_project()
    {
        var tenancy = $"{Solution.Product}.{Solution.Pascal(PlatformServices.Tenancy.Key)}";
        var offenders = Solution.ModuleAssemblies
            .Where(a => a.GetName().Name != tenancy)
            .Where(a => a.GetReferencedAssemblies().Any(r => r.Name == $"{Solution.Product}.Core.Catalog"))
            .Select(a => a.GetName().Name);
        offenders.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Domain_depends_on_no_other_layer(string module)
    {
        var result = Types.InAssembly(Load(module))
            .That().ResideInNamespace($"{module}.Domain")
            .ShouldNot().HaveDependencyOnAny($"{module}.Application", $"{module}.Api", $"{module}.Infrastructure", "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore")
            .GetResult();
        result.FailingTypeNames.ShouldBeNull();
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Application_depends_only_on_domain(string module)
    {
        var result = Types.InAssembly(Load(module))
            .That().ResideInNamespace($"{module}.Application")
            .ShouldNot().HaveDependencyOnAny($"{module}.Api", $"{module}.Infrastructure")
            .GetResult();
        result.FailingTypeNames.ShouldBeNull();
    }

    [Fact]
    public void Only_the_Migrator_references_migrations_assemblies()
    {
        var offenders = Solution.Assemblies
            .Where(a => a.GetName().Name != $"{Solution.Product}.Migrator")
            .Where(a => a.GetReferencedAssemblies().Any(r => r.Name!.Contains(".Migrations.", StringComparison.Ordinal)))
            .Select(a => a.GetName().Name);
        offenders.ShouldBeEmpty();
    }

    private static Assembly Load(string name) => Solution.ModuleAssemblies.Single(a => a.GetName().Name == name);
}

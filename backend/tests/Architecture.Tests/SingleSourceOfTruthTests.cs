using System.Text.RegularExpressions;
using Acme.Core.Platform;

namespace Acme.Architecture.Tests;

/// <summary>Facts from platform.yaml are never typed by hand outside generated code (section 2.1).</summary>
public sealed partial class SingleSourceOfTruthTests
{
    /// <summary>The one declaration of the route convention.</summary>
    private static readonly string[] ApiPathAllowList = ["Core/Platform/PlatformService.cs"];

    [Fact]
    public void Module_and_service_keys_are_not_hard_coded()
    {
        var keys = Modules.All.Select(m => m.Key).Concat(PlatformServices.All.Select(s => s.Key)).Distinct().ToList();
        var offenders = Scan(line => keys.Any(key =>
            line.Contains($"\"{key}\"", StringComparison.Ordinal) || line.Contains($"\"{key}:", StringComparison.Ordinal)));
        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void Role_names_are_not_hard_coded()
    {
        var roles = Enum.GetValues<Role>().Select(Roles.Name).ToList();
        var offenders = Scan(line => roles.Any(role => line.Contains($"\"{role}\"", StringComparison.Ordinal)));
        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void Api_paths_are_not_hard_coded()
    {
        var offenders = Scan(line => line.Contains("\"/api/", StringComparison.Ordinal) || line.Contains("$\"/api/", StringComparison.Ordinal))
            .Where(o => !ApiPathAllowList.Any(allowed => o.StartsWith(allowed, StringComparison.Ordinal)));
        offenders.ShouldBeEmpty();
    }

    /// <summary>Only Core, Tenancy, Identity, the Gateway and the Migrator may compare tenant IDs or slugs (section 7.2).</summary>
    [Fact]
    public void Business_modules_never_branch_on_tenant_identity()
    {
        string[] allowedModules = [Solution.Pascal(PlatformServices.Tenancy.Key), Solution.Pascal(PlatformServices.Identity.Key)];
        var offenders = Scan(line => TenantComparison().IsMatch(line))
            .Where(o => o.StartsWith("Modules/", StringComparison.Ordinal))
            .Where(o => !allowedModules.Any(m => o.StartsWith($"Modules/{m}/", StringComparison.Ordinal)));
        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void Per_environment_settings_files_do_not_exist()
    {
        var files = Directory.EnumerateFiles(Solution.BackendSource, "appsettings.*.json", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
        files.ShouldBeEmpty();
    }

    private static List<string> Scan(Func<string, bool> isViolation) =>
        Solution.HandWrittenSources()
            .SelectMany(path => File.ReadLines(path).Select((line, i) => (path, line, number: i + 1)))
            .Where(x => !x.line.TrimStart().StartsWith("//", StringComparison.Ordinal) && isViolation(x.line))
            .Select(x => $"{Path.GetRelativePath(Solution.BackendSource, x.path).Replace('\\', '/')}:{x.number}: {x.line.Trim()}")
            .ToList();

    [Theory]
    [InlineData("if (tenant.TenantSlug == \"demo-plus\")")]
    [InlineData("if (context.TenantId != expected)")]
    [InlineData("if (expected == context.TenantId)")]
    [InlineData("if (tenants.Tenant.Slug == slug)")]
    [InlineData("if (tenants.Tenant.Id != id)")]
    [InlineData("if (string.Equals(slug, tenant.TenantSlug))")]
    [InlineData("if (tenant.TenantSlug.Equals(\"acme\"))")]
    [InlineData("switch (tenant.TenantSlug)")]
    public void Tenant_comparisons_are_detected(string line) => TenantComparison().IsMatch(line).ShouldBeTrue();

    [Theory]
    [InlineData("var id = tenant.TenantId;")]
    [InlineData("logger.Log(tenant.TenantSlug);")]
    [InlineData("if (request.Total < settings.AutoApproveBelow)")]
    public void Ordinary_tenant_use_is_allowed(string line) => TenantComparison().IsMatch(line).ShouldBeFalse();

    [GeneratedRegex(@"\b(TenantId|TenantSlug|Tenant\.Id|Tenant\.Slug)\s*(==|!=|\.Equals\()|(==|!=)\s*[\w.]*\b(TenantId|TenantSlug|Tenant\.Id|Tenant\.Slug)\b|Equals\([^)]*\b(TenantId|TenantSlug|Tenant\.Id|Tenant\.Slug)\b|switch\s*\([\w.]*\b(TenantId|TenantSlug|Tenant\.Id|Tenant\.Slug)\)")]
    private static partial Regex TenantComparison();
}

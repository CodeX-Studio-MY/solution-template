using Farm360.Core.Platform;
using Farm360.Identity.Domain;

namespace Farm360.Identity.Tests;

public sealed class EffectiveAccessResolverTests
{
    private static readonly Guid TenantSettings = Id(Modules.TenantSettings);
    private static readonly Guid Billing = Id(Modules.Billing);
    private static readonly Guid AccessManagement = Id(Modules.AccessManagement);

    /// <summary>A tenant entitled to every module.</summary>
    private static readonly IReadOnlySet<string> Everything = Modules.All.Select(m => m.Key).ToHashSet();

    /// <summary>A tenant whose plan leaves out billing.</summary>
    private static readonly IReadOnlySet<string> WithoutBilling = Everything.Where(k => k != Modules.Billing).ToHashSet();

    [Fact]
    public void Direct_assignments_only()
    {
        var access = Resolve([new(TenantSettings, Role.Manager)], []);
        access.Roles.ShouldBe(new Dictionary<string, Role> { [Modules.TenantSettings] = Role.Manager });
    }

    [Fact]
    public void Group_assignments_only()
    {
        var access = Resolve([], [Group([new(Billing, Role.User)])]);
        access.Roles.ShouldBe(new Dictionary<string, Role> { [Modules.Billing] = Role.User });
    }

    /// <summary>The Ali example (section 4.5): the highest role always wins, whether direct or from a group.</summary>
    [Fact]
    public void Direct_and_group_the_highest_wins()
    {
        var finance = Group([new(TenantSettings, Role.User), new(Billing, Role.Manager), new(AccessManagement, Role.User)]);
        var access = Resolve([new(TenantSettings, Role.Admin), new(Billing, Role.User)], [finance]);

        access.Roles.ShouldBe(new Dictionary<string, Role>
        {
            [Modules.TenantSettings] = Role.Admin,
            [Modules.Billing] = Role.Manager,
            [Modules.AccessManagement] = Role.User,
        });
        access.RoleClaims.ShouldBe(["access-management:user", "billing:manager", "tenant-settings:admin"]);
    }

    [Fact]
    public void Several_groups_merge_by_highest_role()
    {
        var access = Resolve([], [Group([new(TenantSettings, Role.User)]), Group([new(TenantSettings, Role.Manager)]), Group([new(TenantSettings, Role.User)])]);
        access.Roles[Modules.TenantSettings].ShouldBe(Role.Manager);
    }

    [Fact]
    public void Inactive_groups_and_memberships_are_ignored()
    {
        var access = Resolve([], [
            Group([new(TenantSettings, Role.Admin)], groupActive: false),
            Group([new(TenantSettings, Role.Manager)], membershipActive: false),
            Group([new(TenantSettings, Role.User)]),
        ]);
        access.Roles[Modules.TenantSettings].ShouldBe(Role.User);
    }

    /// <summary>A module outside the tenant's plan is removed: entitlement overrides assignment.</summary>
    [Fact]
    public void Modules_the_tenant_is_not_entitled_to_are_removed()
    {
        var access = Resolve([new(TenantSettings, Role.Admin), new(Billing, Role.Admin)], [], WithoutBilling);
        access.Roles.Keys.ShouldBe([Modules.TenantSettings]);
    }

    [Fact]
    public void Data_scopes_merge_as_a_union()
    {
        var access = EffectiveAccessResolver.Resolve(
            [], [new("site", "KL"), new("site", "PG")],
            [new GroupGrants(true, true, [], [new("site", "PG"), new("site", "JB")])],
            Everything);
        access.DataScopes["site"].ShouldBe(new HashSet<string> { "KL", "PG", "JB" }, ignoreOrder: true);
    }

    private static EffectiveAccess Resolve(ModuleGrant[] direct, GroupGrants[] groups, IReadOnlySet<string>? entitled = null) =>
        EffectiveAccessResolver.Resolve(direct, [], groups, entitled ?? Everything);

    private static GroupGrants Group(ModuleGrant[] grants, bool groupActive = true, bool membershipActive = true) =>
        new(groupActive, membershipActive, grants, []);

    private static Guid Id(string key) => Modules.All.Single(m => m.Key == key).Id;
}

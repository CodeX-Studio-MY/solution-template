using Acme.Core.Platform;
using Acme.Identity.Domain;

namespace Acme.Identity.Tests;

public sealed class EffectiveAccessResolverTests
{
    private static readonly Guid PurchaseRequest = Id(Modules.PurchaseRequest);
    private static readonly Guid CostCenterBudget = Id(Modules.CostCenterBudget);
    private static readonly Guid AccessManagement = Id(Modules.AccessManagement);

    private static readonly IReadOnlySet<string> Plus = Modules.All.Where(m => m.Plans.Contains(Plans.Plus)).Select(m => m.Key).ToHashSet();
    private static readonly IReadOnlySet<string> Standard = Modules.All.Where(m => m.Plans.Contains(Plans.Standard)).Select(m => m.Key).ToHashSet();

    [Fact]
    public void Direct_assignments_only()
    {
        var access = Resolve([new(PurchaseRequest, Role.Manager)], []);
        access.Roles.ShouldBe(new Dictionary<string, Role> { [Modules.PurchaseRequest] = Role.Manager });
    }

    [Fact]
    public void Group_assignments_only()
    {
        var access = Resolve([], [Group([new(CostCenterBudget, Role.User)])]);
        access.Roles.ShouldBe(new Dictionary<string, Role> { [Modules.CostCenterBudget] = Role.User });
    }

    /// <summary>The Ali example (section 4.5): the highest role always wins, whether direct or from a group.</summary>
    [Fact]
    public void Direct_and_group_the_highest_wins()
    {
        var finance = Group([new(PurchaseRequest, Role.User), new(CostCenterBudget, Role.Manager), new(AccessManagement, Role.User)]);
        var access = Resolve([new(PurchaseRequest, Role.Admin), new(CostCenterBudget, Role.User)], [finance]);

        access.Roles.ShouldBe(new Dictionary<string, Role>
        {
            [Modules.PurchaseRequest] = Role.Admin,
            [Modules.CostCenterBudget] = Role.Manager,
            [Modules.AccessManagement] = Role.User,
        });
        access.RoleClaims.ShouldBe(["access-management:user", "cost-center-budget:manager", "purchase-request:admin"]);
    }

    [Fact]
    public void Several_groups_merge_by_highest_role()
    {
        var access = Resolve([], [Group([new(PurchaseRequest, Role.User)]), Group([new(PurchaseRequest, Role.Manager)]), Group([new(PurchaseRequest, Role.User)])]);
        access.Roles[Modules.PurchaseRequest].ShouldBe(Role.Manager);
    }

    [Fact]
    public void Inactive_groups_and_memberships_are_ignored()
    {
        var access = Resolve([], [
            Group([new(PurchaseRequest, Role.Admin)], groupActive: false),
            Group([new(PurchaseRequest, Role.Manager)], membershipActive: false),
            Group([new(PurchaseRequest, Role.User)]),
        ]);
        access.Roles[Modules.PurchaseRequest].ShouldBe(Role.User);
    }

    /// <summary>On the Standard plan, cost-center-budget is removed: entitlement overrides assignment.</summary>
    [Fact]
    public void Modules_the_tenant_is_not_entitled_to_are_removed()
    {
        var access = Resolve([new(PurchaseRequest, Role.Admin), new(CostCenterBudget, Role.Admin)], [], Standard);
        access.Roles.Keys.ShouldBe([Modules.PurchaseRequest]);
    }

    [Fact]
    public void Data_scopes_merge_as_a_union()
    {
        var access = EffectiveAccessResolver.Resolve(
            [], [new("site", "KL"), new("site", "PG")],
            [new GroupGrants(true, true, [], [new("site", "PG"), new("site", "JB")])],
            Plus);
        access.DataScopes["site"].ShouldBe(new HashSet<string> { "KL", "PG", "JB" }, ignoreOrder: true);
    }

    private static EffectiveAccess Resolve(ModuleGrant[] direct, GroupGrants[] groups, IReadOnlySet<string>? entitled = null) =>
        EffectiveAccessResolver.Resolve(direct, [], groups, entitled ?? Plus);

    private static GroupGrants Group(ModuleGrant[] grants, bool groupActive = true, bool membershipActive = true) =>
        new(groupActive, membershipActive, grants, []);

    private static Guid Id(string key) => Modules.All.Single(m => m.Key == key).Id;
}

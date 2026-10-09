using Acme.Core.Data;
using Acme.Core.Platform;

namespace Acme.Core.Tests;

public sealed class RoleTests
{
    [Fact]
    public void A_higher_role_includes_a_lower_one()
    {
        Role.Admin.Includes(Role.Manager).ShouldBeTrue();
        Role.Manager.Includes(Role.Manager).ShouldBeTrue();
        Role.User.Includes(Role.Manager).ShouldBeFalse();
    }

    [Fact]
    public void Module_role_claims_round_trip()
    {
        var claim = RoleExtensions.ToModuleRole(Modules.PurchaseRequest, Role.Manager);
        claim.ShouldBe("purchase-request:manager");
        RoleExtensions.TryParseModuleRole(claim, out var module, out var role).ShouldBeTrue();
        module.ShouldBe(Modules.PurchaseRequest);
        role.ShouldBe(Role.Manager);
    }

    [Theory]
    [InlineData("purchase-request")]
    [InlineData("purchase-request:owner")]
    [InlineData(":manager")]
    public void Invalid_module_role_claims_are_rejected(string value) =>
        RoleExtensions.TryParseModuleRole(value, out _, out _).ShouldBeFalse();
}

public sealed class MoneyTests
{
    [Fact]
    public void Adds_and_compares_in_the_same_currency()
    {
        var total = new Money(100m, "MYR") + new Money(0.5m, "MYR");
        total.ShouldBe(new Money(100.5m, "MYR"));
        (total > new Money(100m, "MYR")).ShouldBeTrue();
    }

    [Fact]
    public void Refuses_to_mix_currencies() =>
        Should.Throw<InvalidOperationException>(() => new Money(1m, "MYR") + new Money(1m, "USD"));
}

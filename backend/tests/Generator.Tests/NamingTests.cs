namespace Saaskit.Generator.Tests;

public sealed class NamingTests
{
    [Theory]
    [InlineData("goods-receipt", "GoodsReceipt")]
    [InlineData("identity", "Identity")]
    [InlineData("cost-center", "CostCenter")]
    public void Pascal_case(string key, string expected) => Naming.Pascal(key).ShouldBe(expected);

    [Fact]
    public void Schema_names_replace_hyphens() => Naming.Schema("goods-receipt").ShouldBe("goods_receipt");

    [Fact]
    public void Stable_guids_are_stable_and_distinct()
    {
        Naming.StableGuid("module:a").ShouldBe(Naming.StableGuid("module:a"));
        Naming.StableGuid("module:a").ShouldNotBe(Naming.StableGuid("module:b"));
        Naming.StableGuid("module:a").ToString()[14].ShouldBe('5');
    }
}

namespace Farm360.Generator.Tests;

public sealed class PlatformDefinitionTests
{
    private static PlatformDefinition Valid() => new()
    {
        Product = "farm360",
        Org = "Org",
        Defaults = new DefaultsDefinition { Timezone = "Asia/Kuala_Lumpur", Currency = "MYR", Locales = ["en"] },
        Cloud = "azure",
        Database = new DatabaseDefinition { Providers = ["sqlserver", "postgresql"] },
        Roles = ["user", "manager", "admin"],
        Plans = ["standard", "plus"],
        Services =
        [
            new ServiceDefinition { Key = "goods-receipt", Port = 5101, Owns = ["goods-receipt"] },
            new ServiceDefinition { Key = "notification", Port = 5003 },
        ],
        Modules = [new ModuleDefinition { Key = "goods-receipt", Name = "Goods Receipts", Plans = ["standard"] }],
    };

    [Fact]
    public void A_valid_definition_passes() => Should.NotThrow(Valid().Validate);

    [Fact]
    public void The_repository_platform_yaml_is_valid()
    {
        var root = RepoRoot.Find();
        Should.NotThrow(() => PlatformDefinition.Load(Path.Combine(root, "platform.yaml")));
    }

    [Theory]
    [InlineData("GoodsReceipt")]
    [InlineData("1abc")]
    [InlineData("goods_receipt")]
    [InlineData("a-module-key-that-is-much-too-long")]
    public void Module_keys_must_match_the_pattern(string key)
    {
        var definition = Valid();
        definition.Modules[0].Key = key;
        definition.Services[0].Owns = [key];
        Should.Throw<PlatformDefinitionException>(definition.Validate).Message.ShouldContain(key);
    }

    [Theory]
    [InlineData("api")]
    [InlineData("bff")]
    [InlineData("mfe")]
    [InlineData("health")]
    [InlineData("login")]
    [InlineData("admin")]
    [InlineData("static")]
    public void Reserved_keys_are_rejected(string key)
    {
        var definition = Valid();
        definition.Services[1].Key = key;
        Should.Throw<PlatformDefinitionException>(definition.Validate).Message.ShouldContain("reserved");
    }

    [Fact]
    public void Duplicate_module_keys_are_rejected()
    {
        var definition = Valid();
        definition.Modules.Add(new ModuleDefinition { Key = "goods-receipt", Name = "Again", Plans = ["standard"] });
        Should.Throw<PlatformDefinitionException>(definition.Validate).Message.ShouldContain("more than once");
    }

    [Fact]
    public void Duplicate_ports_are_rejected()
    {
        var definition = Valid();
        definition.Services[1].Port = 5101;
        Should.Throw<PlatformDefinitionException>(definition.Validate).Message.ShouldContain("port 5101");
    }

    [Fact]
    public void A_module_owned_by_no_service_is_rejected()
    {
        var definition = Valid();
        definition.Services[0].Owns = [];
        Should.Throw<PlatformDefinitionException>(definition.Validate).Message.ShouldContain("not owned");
    }

    [Fact]
    public void A_module_in_an_undeclared_plan_is_rejected()
    {
        var definition = Valid();
        definition.Modules[0].Plans = ["enterprise"];
        Should.Throw<PlatformDefinitionException>(definition.Validate).Message.ShouldContain("enterprise");
    }

    [Fact]
    public void An_unknown_database_provider_is_rejected()
    {
        var definition = Valid();
        definition.Database.Providers = ["oracle"];
        Should.Throw<PlatformDefinitionException>(definition.Validate).Message.ShouldContain("oracle");
    }
}

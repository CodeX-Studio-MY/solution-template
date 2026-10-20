using Farm360.Core.Configuration;
using Farm360.Core.Data;
using Farm360.Core.Errors;
using Farm360.Core.Tenancy;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Farm360.Core.Tests;

public sealed class TenantConnectionResolverTests
{
    private readonly TenantConnectionResolver _resolver;

    public TenantConnectionResolverTests()
    {
        var provider = Substitute.For<IDatabaseProvider>();
        provider.Name.Returns("fake");
        provider.BuildConnectionString(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>())
            .Returns(call => $"{call.ArgAt<string>(0)};Database={call.ArgAt<string>(1)};Max={call.ArgAt<int>(2)}");
        var options = Options.Create(new DatabaseOptions
        {
            Provider = "fake",
            CatalogConnectionString = "catalog",
            Servers = new Dictionary<string, string> { ["default"] = "Server=pool" },
            MaxPoolSize = 7,
        });
        var providers = Substitute.For<IDatabaseProviderAccessor>();
        providers.ForName("fake").Returns(provider);
        _resolver = new TenantConnectionResolver(options, providers);
    }

    [Fact]
    public void Builds_the_connection_from_the_pool_and_placement() =>
        _resolver.Resolve(TestTenants.Create("demo")).ShouldBe("Server=pool;Database=tenant_demo;Max=7");

    [Fact]
    public void Suspended_tenants_can_read_but_not_write()
    {
        var suspended = TestTenants.Create(status: TenantStatus.Suspended);
        Should.NotThrow(() => _resolver.Resolve(suspended, TenantDataAccess.Read));
        Should.Throw<ForbiddenException>(() => _resolver.Resolve(suspended, TenantDataAccess.Write))
            .Error.ShouldBe(PlatformErrors.TenantReadOnly);
    }

    [Theory]
    [InlineData(TenantStatus.Provisioning)]
    [InlineData(TenantStatus.PendingDeletion)]
    [InlineData(TenantStatus.Deleted)]
    public void Unavailable_tenants_are_refused(TenantStatus status) =>
        Should.Throw<ForbiddenException>(() => _resolver.Resolve(TestTenants.Create(status: status)))
            .Error.ShouldBe(PlatformErrors.TenantUnavailable);

    [Fact]
    public void Maintenance_allows_provisioning_but_not_deleted_tenants()
    {
        Should.NotThrow(() => _resolver.ResolveForMaintenance(TestTenants.Create(status: TenantStatus.Provisioning)));
        Should.Throw<ForbiddenException>(() => _resolver.ResolveForMaintenance(TestTenants.Create(status: TenantStatus.Deleted)));
    }

    [Fact]
    public void An_unconfigured_pool_fails_clearly()
    {
        var tenant = TestTenants.Create() with { Placement = new TenantPlacement("fake", "eu-pool", "db", null) };
        Should.Throw<InvalidOperationException>(() => _resolver.Resolve(tenant)).Message.ShouldContain("Database:Servers:eu-pool");
    }
}

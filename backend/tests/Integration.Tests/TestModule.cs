using System.Security.Claims;
using Saaskit.Core;
using Saaskit.Core.Data;
using Saaskit.Core.Security;
using Saaskit.Core.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NetTopologySuite.Geometries;

namespace Saaskit.Integration.Tests;

/// <summary>A module-shaped context used to test the platform's data conventions on both providers.</summary>
public sealed class TestModuleDbContext(DbContextOptions<TestModuleDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "test_module";

    public DbSet<TestItem> Items => Set<TestItem>();

    public DbSet<TestPlace> Places => Set<TestPlace>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TestItem>(item =>
        {
            item.ToTable("Items");
            item.Property(i => i.Code).HasMaxLength(20);
            item.HasIndex(i => i.Code).IsUniqueAmongNotDeleted();
        });
        modelBuilder.Entity<TestPlace>(place =>
        {
            place.ToTable("Places");
            place.Property(p => p.Name).HasMaxLength(50);
        });
    }
}

/// <summary>A spatial column: the provider maps it to geography (SRID 4326) and, on PostgreSQL, enables PostGIS.</summary>
public sealed class TestPlace : BaseEntity
{
    private TestPlace()
    {
    }

    public TestPlace(string name, Point location)
    {
        Name = name;
        Location = location;
    }

    public string Name { get; private set; } = "";

    public Point Location { get; private set; } = null!;
}

public sealed class TestItem : BaseEntity, IAggregateRoot, ISoftDeletable
{
    private TestItem()
    {
    }

    public TestItem(string code, Money price)
    {
        Code = code;
        Price = price;
    }

    public string Code { get; private set; } = "";

    public Money Price { get; private set; }

    public bool IsDeleted { get; private set; }

    public DateTime? DeletedOn { get; private set; }

    public void Reprice(Money price) => Price = price;

    public void MarkDeleted(DateTime utcNow)
    {
        IsDeleted = true;
        DeletedOn = utcNow;
    }
}

/// <summary>Builds contexts the way a host does: kernel services, interceptors, a tenant and a principal.</summary>
internal sealed class TestModuleHost : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    public TestModuleHost(string provider, string connectionString, TenantStatus status = TenantStatus.Active)
    {
        Provider = Databases.Provider(provider);
        ConnectionString = connectionString;
        Time = new FakeTimeProvider(new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(Time);
        services.AddScoped<ICurrentPrincipal>(_ => new TestPrincipal("user-1"));
        services.AddPlatformKernel(new ConfigurationBuilder().Build());
        Provider.AddServices(services, connectionString);
        _services = services.BuildServiceProvider();

        Tenant = new TenantInfo(Guid.NewGuid(), "test", "Test", status, "standard", "Asia/Kuala_Lumpur", "MYR", "en",
            new HashSet<string>(), new TenantPlacement(provider, "default", "test", null));
    }

    public IDatabaseProvider Provider { get; }

    public string ConnectionString { get; }

    public FakeTimeProvider Time { get; }

    public TenantInfo Tenant { get; }

    public T Get<T>() where T : notnull => _services.GetRequiredService<T>();

    /// <summary>A new scope and context, like one request.</summary>
    public (AsyncServiceScope Scope, TestModuleDbContext Db) NewContext()
    {
        var scope = _services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(Tenant);
        var db = DbContextRegistration.Create<TestModuleDbContext>(scope.ServiceProvider, Provider, ConnectionString, TestModuleDbContext.SchemaName);
        return (scope, db);
    }

    public async Task CreateDatabaseAsync()
    {
        var (scope, db) = NewContext();
        await using (scope)
        await using (db)
        {
            await db.Database.EnsureCreatedAsync();
        }
    }

    public ValueTask DisposeAsync() => _services.DisposeAsync();

    private sealed class TestPrincipal(string id) : ICurrentPrincipal
    {
        public bool IsAuthenticated => true;

        public string? Id => id;

        public PrincipalType? Type => PrincipalType.User;

        public string? ActorId => null;

        public ClaimsPrincipal? Principal => null;
    }
}

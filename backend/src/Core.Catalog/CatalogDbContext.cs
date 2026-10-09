using Acme.Core.Catalog.Entities;
using Acme.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Acme.Core.Catalog;

/// <summary>The tenant directory in the catalog database (schema <see cref="SchemaName"/>).</summary>
public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : PlatformDbContext(options)
{
    public const string SchemaName = "catalog";

    /// <summary>Module key format, also enforced by a check constraint.</summary>
    public const string ModuleKeyPattern = Acme.Core.Platform.KeyRules.PatternText;

    public DbSet<CatalogTenant> Tenants => Set<CatalogTenant>();

    public DbSet<CatalogModule> Modules => Set<CatalogModule>();

    public DbSet<PlanModule> PlanModules => Set<PlanModule>();

    public DbSet<TenantModule> TenantModules => Set<TenantModule>();

    public DbSet<TenantMigrationState> MigrationStates => Set<TenantMigrationState>();

    public DbSet<PlanLimit> PlanLimits => Set<PlanLimit>();

    public DbSet<TenantSetting> TenantSettings => Set<TenantSetting>();

    public DbSet<TenantFeature> TenantFeatures => Set<TenantFeature>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CatalogTenant>(tenant =>
        {
            tenant.ToTable("Tenants");
            tenant.Property(t => t.Slug).HasMaxLength(CatalogTenant.MaxSlugLength);
            tenant.HasIndex(t => t.Slug).IsUnique();
            tenant.Property(t => t.DisplayName).HasMaxLength(200);
            tenant.Property(t => t.Plan).HasMaxLength(30);
            tenant.Property(t => t.TimeZone).HasMaxLength(64);
            tenant.Property(t => t.Currency).HasMaxLength(3).IsFixedLength();
            tenant.Property(t => t.Locale).HasMaxLength(16);
            tenant.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
            tenant.Property(t => t.RequireSupportApproval).HasDefaultValue(true);
            tenant.ComplexProperty(t => t.Placement, placement =>
            {
                placement.Property(p => p.Provider).HasMaxLength(20);
                placement.Property(p => p.ServerPool).HasMaxLength(64);
                placement.Property(p => p.DatabaseName).HasMaxLength(128);
                placement.Property(p => p.SecretReference).HasMaxLength(512);
            });
        });

        modelBuilder.Entity<CatalogModule>(module =>
        {
            module.ToTable("Modules", t => t.HasCheckConstraint(
                "CK_Modules_ModuleKey",
                MatchesPatternSql(nameof(CatalogModule.ModuleKey), ModuleKeyPattern)));
            module.HasKey(m => m.ModuleId);
            module.Property(m => m.ModuleId).ValueGeneratedNever();
            module.Property(m => m.ModuleKey).HasMaxLength(CatalogModule.MaxKeyLength).IsUnicode(false);
            module.HasIndex(m => m.ModuleKey).IsUnique();
            module.Property(m => m.DisplayName).HasMaxLength(200);
        });

        modelBuilder.Entity<PlanModule>(planModule =>
        {
            planModule.ToTable("PlanModules");
            planModule.HasKey(p => new { p.Plan, p.ModuleId });
            planModule.Property(p => p.Plan).HasMaxLength(30);
            planModule.HasOne<CatalogModule>().WithMany().HasForeignKey(p => p.ModuleId);
        });

        modelBuilder.Entity<TenantModule>(tenantModule =>
        {
            tenantModule.ToTable("TenantModules");
            tenantModule.HasKey(t => new { t.TenantId, t.ModuleId });
            tenantModule.Property(t => t.FrontendVersion).HasMaxLength(64);
            tenantModule.HasOne<CatalogTenant>().WithMany().HasForeignKey(t => t.TenantId);
            tenantModule.HasOne<CatalogModule>().WithMany().HasForeignKey(t => t.ModuleId);
        });

        modelBuilder.Entity<TenantMigrationState>(state =>
        {
            state.ToTable("TenantMigrationStates");
            state.HasKey(s => new { s.TenantId, s.Target });
            state.Property(s => s.Target).HasMaxLength(64);
            state.Property(s => s.LastMigration).HasMaxLength(256);
            state.Property(s => s.Outcome).HasConversion<string>().HasMaxLength(20);
            state.HasOne<CatalogTenant>().WithMany().HasForeignKey(s => s.TenantId);
        });

        modelBuilder.Entity<PlanLimit>(limit =>
        {
            limit.ToTable("PlanLimits");
            limit.HasKey(l => l.Plan);
            limit.Property(l => l.Plan).HasMaxLength(30);
        });

        modelBuilder.Entity<TenantSetting>(setting =>
        {
            setting.ToTable("TenantSettings");
            setting.HasKey(s => new { s.TenantId, s.ModuleKey });
            setting.Property(s => s.ModuleKey).HasMaxLength(CatalogModule.MaxKeyLength).IsUnicode(false);
            setting.Property(s => s.Json).HasMaxLength(TenantSetting.MaxJsonLength);
            setting.Property(s => s.UpdatedBy).HasMaxLength(200);
            setting.HasOne<CatalogTenant>().WithMany().HasForeignKey(s => s.TenantId);
        });

        modelBuilder.Entity<TenantFeature>(feature =>
        {
            feature.ToTable("TenantFeatures");
            feature.HasKey(f => new { f.TenantId, f.FeatureKey });
            feature.Property(f => f.FeatureKey).HasMaxLength(TenantFeature.MaxKeyLength).IsUnicode(false);
            feature.Property(f => f.UpdatedBy).HasMaxLength(200);
            feature.HasOne<CatalogTenant>().WithMany().HasForeignKey(f => f.TenantId);
        });

        SeedPlatformData(modelBuilder);
    }

    /// <summary>Modules and plan contents, from the constants generated from platform.yaml. Reference data only.</summary>
    private static void SeedPlatformData(ModelBuilder modelBuilder)
    {
        var modules = global::Acme.Core.Platform.Modules.All;
        modelBuilder.Entity<CatalogModule>().HasData(modules.Select(m => new CatalogModule
        {
            ModuleId = m.Id,
            ModuleKey = m.Key,
            DisplayName = m.DisplayName,
        }));
        modelBuilder.Entity<PlanModule>().HasData(modules.SelectMany(m => m.Plans.Select(plan => new PlanModule
        {
            Plan = plan,
            ModuleId = m.Id,
        })));
    }
}

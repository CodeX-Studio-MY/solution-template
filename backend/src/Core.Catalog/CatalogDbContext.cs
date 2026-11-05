using Saaskit.Core.Catalog.Entities;
using Saaskit.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Saaskit.Core.Catalog;

/// <summary>The tenant directory in the catalog database (schema <see cref="SchemaName"/>).</summary>
public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : PlatformDbContext(options)
{
    public const string SchemaName = "catalog";

    /// <summary>Module key format, also enforced by a check constraint.</summary>
    public const string ModuleKeyPattern = Saaskit.Core.Platform.KeyRules.PatternText;

    public DbSet<CatalogTenant> Tenants => Set<CatalogTenant>();

    public DbSet<CatalogModule> Modules => Set<CatalogModule>();

    public DbSet<Plan> Plans => Set<Plan>();

    public DbSet<PlanModuleAccess> PlanModuleAccess => Set<PlanModuleAccess>();

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
            tenant.HasOne<Plan>().WithMany().HasForeignKey(t => t.PlanId).OnDelete(DeleteBehavior.Restrict);
            tenant.HasIndex(t => t.PlanId);
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

        modelBuilder.Entity<Plan>(plan =>
        {
            plan.ToTable("Plans", t => t.HasCheckConstraint(
                "CK_Plans_PlanKey",
                MatchesPatternSql(nameof(Plan.PlanKey), ModuleKeyPattern)));
            plan.HasKey(p => p.PlanId);
            plan.Property(p => p.PlanKey).HasMaxLength(CatalogModule.MaxKeyLength).IsUnicode(false);
            plan.HasIndex(p => p.PlanKey).IsUnique();
            plan.Property(p => p.DisplayName).HasMaxLength(Plan.MaxDisplayNameLength);
            plan.Property(p => p.Description).HasMaxLength(Plan.MaxDescriptionLength);
            plan.Property(p => p.Currency).HasMaxLength(3).IsUnicode(false).IsFixedLength();
            plan.Property(p => p.CreatedBy).HasMaxLength(50).IsUnicode(false);
            plan.Property(p => p.UpdatedBy).HasMaxLength(50).IsUnicode(false);
        });

        modelBuilder.Entity<PlanModuleAccess>(planModule =>
        {
            planModule.ToTable("PlanModuleAccess");
            planModule.HasKey(p => new { p.PlanId, p.ModuleId });
            planModule.HasOne<Plan>().WithMany().HasForeignKey(p => p.PlanId).OnDelete(DeleteBehavior.Restrict);
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
            limit.HasKey(l => l.PlanId);
            limit.Property(l => l.PlanId).ValueGeneratedNever();
            limit.HasOne<Plan>().WithOne().HasForeignKey<PlanLimit>(l => l.PlanId).OnDelete(DeleteBehavior.Restrict);
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

    /// <summary>
    /// The first plans and modules, from the constants generated from platform.yaml. Reference data seeded once: after that the
    /// operator console owns the plans (names, prices, modules), so a changed platform.yaml never rewrites them.
    /// </summary>
    private static void SeedPlatformData(ModelBuilder modelBuilder)
    {
        var modules = global::Saaskit.Core.Platform.Modules.All;
        var plans = global::Saaskit.Core.Platform.Plans.All;
        modelBuilder.Entity<CatalogModule>().HasData(modules.Select(m => new CatalogModule
        {
            ModuleId = m.Id,
            ModuleKey = m.Key,
            DisplayName = m.DisplayName,
        }));
        var seededOn = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        modelBuilder.Entity<Plan>().HasData(plans.Select((key, index) => new
        {
            PlanId = (short)(index + 1),
            PlanKey = key,
            DisplayName = TitleCase(key),
            Currency = global::Saaskit.Core.Platform.PlatformInfo.DefaultCurrency,
            SortOrder = (byte)(index + 1),
            IsActive = true,
            CreatedOn = seededOn,
            CreatedBy = "system",
        }));
        modelBuilder.Entity<PlanModuleAccess>().HasData(modules.SelectMany(m => m.Plans.Select(plan => new PlanModuleAccess
        {
            PlanId = (short)(IndexOf(plans, plan) + 1),
            ModuleId = m.Id,
        })));
    }

    private static int IndexOf(IReadOnlyList<string> plans, string plan)
    {
        for (var i = 0; i < plans.Count; i++)
            if (plans[i] == plan)
                return i;
        throw new InvalidOperationException($"Plan '{plan}' is not in platform.yaml.");
    }

    /// <summary>"lite" is "Lite", "pay-as-you-go" is "Pay As You Go".</summary>
    private static string TitleCase(string key) =>
        string.Join(' ', key.Split('-', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
}

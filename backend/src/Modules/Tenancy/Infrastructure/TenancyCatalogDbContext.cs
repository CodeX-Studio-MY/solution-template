using Saaskit.Core.Data;
using Saaskit.Core.Platform;
using Saaskit.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;

namespace Saaskit.Tenancy.Infrastructure;

/// <summary>Tenancy's own data in the catalog database: onboarding runs, operators, support access, the platform audit log, and principals with their tenant access.</summary>
public sealed class TenancyCatalogDbContext(DbContextOptions<TenancyCatalogDbContext> options) : PlatformDbContext(options)
{
    public DbSet<OnboardingRun> OnboardingRuns => Set<OnboardingRun>();

    public DbSet<PlatformOperator> Operators => Set<PlatformOperator>();

    public DbSet<SupportAccessGrant> SupportAccessGrants => Set<SupportAccessGrant>();

    public DbSet<TenancyAuditEntry> AuditLog => Set<TenancyAuditEntry>();

    public DbSet<PlatformPrincipal> Principals => Set<PlatformPrincipal>();

    public DbSet<PrincipalUser> PrincipalUsers => Set<PrincipalUser>();

    public DbSet<PrincipalUserProvider> PrincipalUserProviders => Set<PrincipalUserProvider>();

    public DbSet<PrincipalDevice> PrincipalDevices => Set<PrincipalDevice>();

    public DbSet<PrincipalApplication> PrincipalApplications => Set<PrincipalApplication>();

    public DbSet<TenantPrincipalAccess> TenantPrincipalAccess => Set<TenantPrincipalAccess>();

    public DbSet<TenantPrincipalAccessAudit> TenantPrincipalAccessAudit => Set<TenantPrincipalAccessAudit>();

    public DbSet<TenantPrincipalLogin> TenantPrincipalLogins => Set<TenantPrincipalLogin>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        ConfigurePrincipals(modelBuilder);

        modelBuilder.Entity<OnboardingRun>(run =>
        {
            run.ToTable("OnboardingRuns");
            run.Property(r => r.Slug).HasMaxLength(30);
            run.HasIndex(r => r.Slug).IsUnique();
            run.Property(r => r.DisplayName).HasMaxLength(200);
            run.Property(r => r.Plan).HasMaxLength(30);
            run.Property(r => r.AdminEmail).HasMaxLength(256);
            run.Property(r => r.AdminName).HasMaxLength(200);
            run.Property(r => r.ServerPool).HasMaxLength(64);
            run.Property(r => r.NextStep).HasConversion<string>().HasMaxLength(30);
            run.Property(r => r.Error).HasMaxLength(2000);
        });

        modelBuilder.Entity<PlatformOperator>(op =>
        {
            op.ToTable("Operators");
            op.Property(o => o.Email).HasMaxLength(256);
            op.Property(o => o.NormalizedEmail).HasMaxLength(256);
            op.HasIndex(o => o.NormalizedEmail).IsUnique();
            op.Property(o => o.DisplayName).HasMaxLength(200);
            op.Property(o => o.PasswordHash).HasMaxLength(500);
            op.Property(o => o.TotpSecret).HasMaxLength(500);
        });

        modelBuilder.Entity<SupportAccessGrant>(grant =>
        {
            grant.ToTable("SupportAccessGrants");
            grant.Property(g => g.Reason).HasMaxLength(500);
            grant.Property(g => g.Role).HasConversion(r => Roles.Name(r), name => ParseRole(name)).HasMaxLength(20);
            grant.Property(g => g.Module).HasMaxLength(30);
            grant.Property(g => g.Status).HasConversion<string>().HasMaxLength(20);
            grant.Property(g => g.DecidedBy).HasMaxLength(100);
            grant.HasIndex(g => new { g.TenantId, g.Status });
            grant.HasIndex(g => g.OperatorId);
        });

        modelBuilder.Entity<TenancyAuditEntry>(audit =>
        {
            audit.ToTable("AuditLog");
            audit.HasKey(a => a.Id);
            audit.Property(a => a.Kind).HasMaxLength(50);
            audit.Property(a => a.Actor).HasMaxLength(100);
            audit.Property(a => a.Details).HasMaxLength(4000);
            audit.HasIndex(a => new { a.TenantId, a.OccurredOn });
        });
    }

    /// <summary>Primary keys are named after the table, code columns are one byte, and ASCII values aren't Unicode: these tables are built for low storage.</summary>
    private static void ConfigurePrincipals(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PlatformPrincipal>(principal =>
        {
            principal.ToTable("Principals");
            principal.Property(p => p.DisplayName).HasMaxLength(PlatformPrincipal.MaxDisplayNameLength);
            AuditColumns(principal);
            principal.HasIndex(p => new { p.Type, p.DisplayName });
        });

        modelBuilder.Entity<PrincipalUser>(user =>
        {
            user.ToTable("PrincipalUsers");
            user.HasKey(u => u.PrincipalUserId);
            user.Property(u => u.PrincipalUserId).ValueGeneratedNever();
            user.Property(u => u.Email).HasMaxLength(PrincipalUser.MaxEmailLength).IsUnicode(false);
            user.HasIndex(u => u.Email).IsUnique();
            user.HasOne<PlatformPrincipal>().WithOne().HasForeignKey<PrincipalUser>(u => u.PrincipalUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PrincipalUserProvider>(provider =>
        {
            provider.ToTable("PrincipalUserProviders");
            provider.HasKey(p => p.PrincipalUserProviderId);
            provider.Property(p => p.ProviderSubject).HasMaxLength(255).IsUnicode(false);
            provider.Property(p => p.ProviderEmail).HasMaxLength(PrincipalUser.MaxEmailLength).IsUnicode(false);
            provider.HasIndex(p => new { p.Provider, p.ProviderSubject }).IsUnique();
            provider.HasIndex(p => p.PrincipalUserId);
            provider.HasOne<PrincipalUser>().WithMany().HasForeignKey(p => p.PrincipalUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PrincipalDevice>(device =>
        {
            device.ToTable("PrincipalDevices");
            device.HasKey(d => d.PrincipalDeviceId);
            device.Property(d => d.PrincipalDeviceId).ValueGeneratedNever();
            device.Property(d => d.Identifier).HasMaxLength(PrincipalDevice.MaxIdentifierLength).IsUnicode(false);
            device.Property(d => d.Manufacturer).HasMaxLength(PrincipalDevice.MaxMakerLength);
            device.Property(d => d.Model).HasMaxLength(PrincipalDevice.MaxMakerLength);
            device.HasIndex(d => new { d.DeviceType, d.Identifier }).IsUnique();
            device.HasOne<PlatformPrincipal>().WithOne().HasForeignKey<PrincipalDevice>(d => d.PrincipalDeviceId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PrincipalApplication>(application =>
        {
            application.ToTable("PrincipalApplications");
            application.HasKey(a => a.PrincipalApplicationId);
            application.Property(a => a.PrincipalApplicationId).ValueGeneratedNever();
            application.Property(a => a.OwnerName).HasMaxLength(PrincipalApplication.MaxOwnerNameLength);
            application.Property(a => a.ContactEmail).HasMaxLength(PrincipalUser.MaxEmailLength).IsUnicode(false);
            application.HasOne<PlatformPrincipal>().WithOne().HasForeignKey<PrincipalApplication>(a => a.PrincipalApplicationId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TenantPrincipalAccess>(access =>
        {
            access.ToTable("TenantPrincipalAccess");
            AuditColumns(access);
            access.HasIndex(a => new { a.TenantId, a.PrincipalId }).IsUnique();
            access.HasIndex(a => a.PrincipalId);
            access.HasOne<PlatformPrincipal>().WithMany().HasForeignKey(a => a.PrincipalId).OnDelete(DeleteBehavior.Restrict);
        });

        // No foreign keys on the two logs: history outlives what it describes, and writing a log row must never fail on a reference.
        modelBuilder.Entity<TenantPrincipalAccessAudit>(audit =>
        {
            audit.ToTable("TenantPrincipalAccessAudit");
            audit.HasKey(a => a.TenantPrincipalAccessAuditId);
            audit.Property(a => a.Reason).HasMaxLength(Domain.TenantPrincipalAccessAudit.MaxReasonLength);
            audit.Property(a => a.RemoteAddress).HasMaxLength(45).IsUnicode(false);
            audit.Property(a => a.TraceId).HasMaxLength(32).IsUnicode(false).IsFixedLength();
            audit.HasIndex(a => new { a.TenantId, a.OccurredOn });
            audit.HasIndex(a => new { a.PrincipalId, a.OccurredOn });
        });

        modelBuilder.Entity<TenantPrincipalLogin>(login =>
        {
            login.ToTable("TenantPrincipalLogins");
            login.HasKey(l => l.TenantPrincipalLoginId);
            login.Property(l => l.RemoteAddress).HasMaxLength(45).IsUnicode(false);
            login.Property(l => l.UserAgent).HasMaxLength(256).IsUnicode(false);
            login.Property(l => l.TraceId).HasMaxLength(32).IsUnicode(false).IsFixedLength();
            login.HasIndex(l => new { l.TenantId, l.OccurredOn });
            login.HasIndex(l => new { l.PrincipalId, l.OccurredOn });
        });
    }

    private static void AuditColumns<T>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T> entity)
        where T : BaseEntity
    {
        entity.Property(e => e.CreatedBy).HasMaxLength(50).IsUnicode(false);
        entity.Property(e => e.UpdatedBy).HasMaxLength(50).IsUnicode(false);
    }

    private static Role ParseRole(string name) =>
        Roles.TryParse(name, out var role) ? role : throw new InvalidOperationException($"Unknown role '{name}'.");
}

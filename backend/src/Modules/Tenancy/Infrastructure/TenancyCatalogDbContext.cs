using Acme.Core.Data;
using Acme.Core.Platform;
using Acme.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;

namespace Acme.Tenancy.Infrastructure;

/// <summary>Tenancy's own data in the catalog database: onboarding runs, operators, support access and the platform audit log.</summary>
public sealed class TenancyCatalogDbContext(DbContextOptions<TenancyCatalogDbContext> options) : PlatformDbContext(options)
{
    public DbSet<OnboardingRun> OnboardingRuns => Set<OnboardingRun>();

    public DbSet<PlatformOperator> Operators => Set<PlatformOperator>();

    public DbSet<SupportAccessGrant> SupportAccessGrants => Set<SupportAccessGrant>();

    public DbSet<TenancyAuditEntry> AuditLog => Set<TenancyAuditEntry>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
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

    private static Role ParseRole(string name) =>
        Roles.TryParse(name, out var role) ? role : throw new InvalidOperationException($"Unknown role '{name}'.");
}

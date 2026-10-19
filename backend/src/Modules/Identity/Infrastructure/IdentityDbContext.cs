using Saaskit.Core.Data;
using Saaskit.Core.Platform;
using Saaskit.Identity.Domain;
using Saaskit.Identity.Infrastructure.OpenIddict;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Saaskit.Identity.Infrastructure;

/// <summary>
/// Identity's data in each tenant's database: principals and their sign-in details, groups, assignments, data scopes,
/// invitations, machine enrollments, the audit log, and OpenIddict authorizations and tokens for this tenant's principals.
/// </summary>
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : ModuleDbContext(options)
{
    public DbSet<Principal> Principals => Set<Principal>();

    public DbSet<PlatformUser> Users => Set<PlatformUser>();

    public DbSet<Group> Groups => Set<Group>();

    public DbSet<GroupMembership> GroupMemberships => Set<GroupMembership>();

    public DbSet<AccessAssignment> AccessAssignments => Set<AccessAssignment>();

    public DbSet<DataScopeAssignment> DataScopeAssignments => Set<DataScopeAssignment>();

    public DbSet<AuditEntry> AuditLog => Set<AuditEntry>();

    public DbSet<Invitation> Invitations => Set<Invitation>();

    public DbSet<MachineEnrollment> MachineEnrollments => Set<MachineEnrollment>();

    public DbSet<OidcAuthorization> OidcAuthorizations => Set<OidcAuthorization>();

    public DbSet<OidcToken> OidcTokens => Set<OidcToken>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Principal>(principal =>
        {
            principal.ToTable("Principals");
            principal.Property(p => p.Type).HasConversion<string>().HasMaxLength(20);
            principal.Property(p => p.DisplayName).HasMaxLength(Principal.MaxDisplayNameLength);
            principal.Property(p => p.ClientId).HasMaxLength(100);
            principal.HasIndex(p => p.ClientId).IsUniqueWhereNotNull(nameof(Principal.ClientId));
        });

        ConfigureUserStore(modelBuilder);

        modelBuilder.Entity<Group>(group =>
        {
            group.ToTable("Groups");
            group.Property(g => g.Name).HasMaxLength(Group.MaxNameLength);
            group.Property(g => g.NormalizedName).HasMaxLength(Group.MaxNameLength);
            group.HasIndex(g => g.NormalizedName).IsUniqueAmongNotDeleted();
        });

        modelBuilder.Entity<GroupMembership>(membership =>
        {
            membership.ToTable("GroupMemberships");
            membership.HasIndex(m => new { m.GroupId, m.PrincipalId }).IsUnique();
            membership.HasIndex(m => m.PrincipalId);
            membership.HasOne<Group>().WithMany().HasForeignKey(m => m.GroupId);
            membership.HasOne<Principal>().WithMany().HasForeignKey(m => m.PrincipalId);
        });

        modelBuilder.Entity<AccessAssignment>(assignment =>
        {
            assignment.ToTable("AccessAssignments", t => t.HasCheckConstraint(
                "CK_AccessAssignments_PrincipalOrGroup",
                ExactlyOneSetSql(nameof(AccessAssignment.PrincipalId), nameof(AccessAssignment.GroupId))));
            assignment.Property(a => a.Role).HasConversion(r => Roles.Name(r), name => RoleNames.Parse(name)).HasMaxLength(20);
            // One role per module per assignee. Filtered, so NULLs are not treated as duplicates.
            assignment.HasIndex(a => new { a.PrincipalId, a.ModuleId }).IsUniqueWhereNotNull(nameof(AccessAssignment.PrincipalId));
            assignment.HasIndex(a => new { a.GroupId, a.ModuleId }).IsUniqueWhereNotNull(nameof(AccessAssignment.GroupId));
            assignment.HasOne<Principal>().WithMany().HasForeignKey(a => a.PrincipalId);
            assignment.HasOne<Group>().WithMany().HasForeignKey(a => a.GroupId);
        });

        modelBuilder.Entity<DataScopeAssignment>(scope =>
        {
            scope.ToTable("DataScopeAssignments", t => t.HasCheckConstraint(
                "CK_DataScopeAssignments_PrincipalOrGroup",
                ExactlyOneSetSql(nameof(DataScopeAssignment.PrincipalId), nameof(DataScopeAssignment.GroupId))));
            scope.Property(s => s.Scope).HasMaxLength(50);
            scope.Property(s => s.Value).HasMaxLength(DataScopeAssignment.MaxLength);
            scope.HasIndex(s => new { s.PrincipalId, s.Scope });
            scope.HasIndex(s => new { s.GroupId, s.Scope });
            scope.HasOne<Principal>().WithMany().HasForeignKey(s => s.PrincipalId);
            scope.HasOne<Group>().WithMany().HasForeignKey(s => s.GroupId);
        });

        modelBuilder.Entity<AuditEntry>(audit =>
        {
            audit.ToTable("AuditLog");
            audit.HasKey(a => a.Id);
            audit.Property(a => a.Kind).HasMaxLength(50);
            audit.Property(a => a.ActorId).HasMaxLength(100);
            audit.Property(a => a.ActorType).HasMaxLength(20);
            audit.Property(a => a.OnBehalfOf).HasMaxLength(100);
            audit.Property(a => a.Subject).HasMaxLength(100);
            audit.Property(a => a.Details).HasMaxLength(4000);
            audit.Property(a => a.RemoteAddress).HasMaxLength(64);
            audit.Property(a => a.TraceId).HasMaxLength(64);
            audit.HasIndex(a => a.OccurredOn);
            audit.HasIndex(a => a.ActorId);
        });

        modelBuilder.Entity<Invitation>(invitation =>
        {
            invitation.ToTable("Invitations");
            invitation.Property(i => i.Email).HasMaxLength(256);
            invitation.Property(i => i.NormalizedEmail).HasMaxLength(256);
            invitation.Property(i => i.TokenHash).HasMaxLength(100);
            invitation.HasIndex(i => i.TokenHash).IsUnique();
            invitation.HasIndex(i => i.NormalizedEmail);
        });

        modelBuilder.Entity<MachineEnrollment>(enrollment =>
        {
            enrollment.ToTable("MachineEnrollments");
            enrollment.Property(e => e.CodeHash).HasMaxLength(100);
            enrollment.HasIndex(e => e.CodeHash).IsUnique();
            enrollment.HasOne<Principal>().WithMany().HasForeignKey(e => e.PrincipalId);
        });

        OidcModel.ConfigureAuthorizationsAndTokens(modelBuilder);
    }

    /// <summary>ASP.NET Core Identity's user tables, mapped into this module's schema.</summary>
    private static void ConfigureUserStore(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PlatformUser>(user =>
        {
            user.ToTable("Users");
            user.HasKey(u => u.Id);
            user.Property(u => u.Id).ValueGeneratedNever();
            user.HasOne<Principal>().WithOne().HasForeignKey<PlatformUser>(u => u.Id);
            user.Property(u => u.UserName).HasMaxLength(256);
            user.Property(u => u.NormalizedUserName).HasMaxLength(256);
            user.Property(u => u.Email).HasMaxLength(256);
            user.Property(u => u.NormalizedEmail).HasMaxLength(256);
            user.Property(u => u.ConcurrencyStamp).IsConcurrencyToken();
            user.Property(u => u.PhoneNumber).HasMaxLength(50);
            user.Property(u => u.SecurityStamp).HasMaxLength(100);
            user.Property(u => u.PasswordHash).HasMaxLength(500);
            user.HasIndex(u => u.NormalizedUserName).IsUnique();
            user.HasIndex(u => u.NormalizedEmail);
            user.HasMany<IdentityUserClaim<Guid>>().WithOne().HasForeignKey(c => c.UserId).IsRequired();
            user.HasMany<IdentityUserLogin<Guid>>().WithOne().HasForeignKey(l => l.UserId).IsRequired();
            user.HasMany<IdentityUserToken<Guid>>().WithOne().HasForeignKey(t => t.UserId).IsRequired();
            user.HasMany<IdentityUserPasskey<Guid>>().WithOne().HasForeignKey(p => p.UserId).IsRequired();
        });

        modelBuilder.Entity<IdentityUserClaim<Guid>>(claim =>
        {
            claim.ToTable("UserClaims");
            claim.HasKey(c => c.Id);
            claim.Property(c => c.ClaimType).HasMaxLength(256);
            claim.Property(c => c.ClaimValue).HasMaxLength(1000);
        });

        modelBuilder.Entity<IdentityUserLogin<Guid>>(login =>
        {
            login.ToTable("UserLogins");
            login.HasKey(l => new { l.LoginProvider, l.ProviderKey });
            login.Property(l => l.LoginProvider).HasMaxLength(128);
            login.Property(l => l.ProviderKey).HasMaxLength(128);
            login.Property(l => l.ProviderDisplayName).HasMaxLength(256);
        });

        modelBuilder.Entity<IdentityUserToken<Guid>>(token =>
        {
            token.ToTable("UserTokens");
            token.HasKey(t => new { t.UserId, t.LoginProvider, t.Name });
            token.Property(t => t.LoginProvider).HasMaxLength(128);
            token.Property(t => t.Name).HasMaxLength(128);
            token.Property(t => t.Value).HasMaxLength(2000);
        });

        modelBuilder.Entity<IdentityUserPasskey<Guid>>(passkey =>
        {
            passkey.ToTable("UserPasskeys");
            passkey.HasKey(p => p.CredentialId);
            passkey.Property(p => p.CredentialId).HasMaxLength(1024);
            passkey.OwnsOne(p => p.Data).ToJson();
        });
    }
}

/// <summary>Converts stored role names back to <see cref="Role"/>.</summary>
internal static class RoleNames
{
    public static Role Parse(string name) =>
        Roles.TryParse(name, out var role) ? role : throw new InvalidOperationException($"Unknown role '{name}'.");
}

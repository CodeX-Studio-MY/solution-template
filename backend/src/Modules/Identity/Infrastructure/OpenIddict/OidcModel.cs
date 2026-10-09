using Acme.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Acme.Identity.Infrastructure.OpenIddict;

/// <summary>The one mapping of the OpenIddict tables, used by both Identity contexts.</summary>
internal static class OidcModel
{
    public static void ConfigureApplications(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<OidcApplication>(application =>
        {
            application.ToTable("OidcApplications");
            application.Property(a => a.ClientId).HasMaxLength(100);
            application.HasIndex(a => a.ClientId).IsUnique();
            application.HasIndex(a => a.TenantId);
            application.Property(a => a.ClientType).HasMaxLength(50);
            application.Property(a => a.ApplicationType).HasMaxLength(50);
            application.Property(a => a.ConsentType).HasMaxLength(50);
            application.Property(a => a.ConcurrencyToken).HasMaxLength(50).IsConcurrencyToken();
        });

    public static void ConfigureAuthorizationsAndTokens(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OidcAuthorization>(authorization =>
        {
            authorization.ToTable("OidcAuthorizations");
            authorization.Property(a => a.ApplicationId).HasMaxLength(100);
            authorization.Property(a => a.Subject).HasMaxLength(400);
            authorization.Property(a => a.Status).HasMaxLength(50);
            authorization.Property(a => a.Type).HasMaxLength(50);
            authorization.Property(a => a.ConcurrencyToken).HasMaxLength(50).IsConcurrencyToken();
            authorization.HasIndex(a => new { a.ApplicationId, a.Status, a.Subject, a.Type });
        });

        modelBuilder.Entity<OidcToken>(token =>
        {
            token.ToTable("OidcTokens");
            token.Property(t => t.ApplicationId).HasMaxLength(100);
            token.Property(t => t.AuthorizationId).HasMaxLength(100);
            token.Property(t => t.Subject).HasMaxLength(400);
            token.Property(t => t.Status).HasMaxLength(50);
            token.Property(t => t.Type).HasMaxLength(150);
            token.Property(t => t.ReferenceId).HasMaxLength(100);
            token.Property(t => t.ConcurrencyToken).HasMaxLength(50).IsConcurrencyToken();
            token.HasIndex(t => t.ReferenceId).IsUniqueWhereNotNull(nameof(OidcToken.ReferenceId));
            token.HasIndex(t => new { t.ApplicationId, t.Status, t.Subject, t.Type });
            token.HasIndex(t => t.AuthorizationId);
        });
    }
}

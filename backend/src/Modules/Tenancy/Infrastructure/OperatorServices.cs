using System.Security.Cryptography;
using System.Text.Json;
using Farm360.Core.Catalog;
using Farm360.Core.Configuration;
using Farm360.Core.Errors;
using Farm360.Core.Platform;
using Farm360.Core.Tenancy;
using Farm360.Tenancy.Application;
using Farm360.Tenancy.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Farm360.Tenancy.Infrastructure;

internal sealed class OperatorAccounts(
    TenancyCatalogDbContext db,
    IDataProtectionProvider protection,
    TimeProvider time) : IOperatorAccounts
{
    public async Task<NewOperator> CreateAsync(string email, string displayName, CancellationToken cancellationToken = default)
    {
        var normalised = email.Trim().ToUpperInvariant();
        if (await db.Operators.AnyAsync(o => o.NormalizedEmail == normalised, cancellationToken))
            throw new ConflictException(TenancyErrors.OperatorExists);

        // A long random passphrase, shown once. The operator changes nothing about it: it is only the first factor.
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        var secret = Totp.NewSecret();
        var hasher = new PasswordHasher<object>();
        var op = new PlatformOperator(email, displayName, hasher.HashPassword(new object(), password), protection.CreateProtector(OperatorSessions.SecretPurpose).Protect(secret));
        db.Operators.Add(op);
        db.AuditLog.Add(new TenancyAuditEntry(time.GetUtcNow().UtcDateTime, null, "operator-created", "system", JsonSerializer.Serialize(new { op.Email })));
        await db.SaveChangesAsync(cancellationToken);
        return new NewOperator(op.Id, op.Email, password, secret, Totp.Uri(PlatformInfo.Product + " operators", op.Email, secret));
    }
}

internal sealed class OperatorSessions(
    TenancyCatalogDbContext db,
    IDataProtectionProvider protection,
    IOptions<OperatorOptions> options,
    TimeProvider time) : IOperatorSessions
{
    public const string SecretPurpose = "tenancy.operator.totp";
    private const string SessionPurpose = "tenancy.operator.session";

    public async Task<SessionDto> SignInAsync(string email, string password, string totpCode, CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow();
        var normalised = email.Trim().ToUpperInvariant();
        var op = await db.Operators.SingleOrDefaultAsync(o => o.NormalizedEmail == normalised, cancellationToken);

        // The same answer for every failure: unknown account, wrong password, wrong code, locked, disabled.
        if (op is null || !op.IsActive || (op.LockedUntil is { } until && until > now.UtcDateTime))
            throw new ForbiddenException(TenancyErrors.OperatorLoginFailed);

        var passwordOk = new PasswordHasher<object>().VerifyHashedPassword(new object(), op.PasswordHash, password) != PasswordVerificationResult.Failed;
        var secret = protection.CreateProtector(SecretPurpose).Unprotect(op.TotpSecret);
        var codeOk = Totp.Verify(secret, totpCode, now);
        if (!passwordOk || !codeOk)
        {
            op.RecordFailure(now.UtcDateTime);
            db.AuditLog.Add(new TenancyAuditEntry(now.UtcDateTime, null, "operator-sign-in-failed", op.Id.ToString(), null));
            await db.SaveChangesAsync(cancellationToken);
            throw new ForbiddenException(TenancyErrors.OperatorLoginFailed);
        }

        op.RecordSuccess();
        db.AuditLog.Add(new TenancyAuditEntry(now.UtcDateTime, null, "operator-sign-in", op.Id.ToString(), null));
        await db.SaveChangesAsync(cancellationToken);

        var expires = now.UtcDateTime.AddMinutes(options.Value.SessionMinutes);
        var protector = protection.CreateProtector(SessionPurpose).ToTimeLimitedDataProtector();
        return new SessionDto(protector.Protect(op.Id.ToString(), expires), expires);
    }

    public async Task<OperatorIdentity?> ValidateAsync(string token, CancellationToken cancellationToken = default)
    {
        string id;
        try
        {
            id = protection.CreateProtector(SessionPurpose).ToTimeLimitedDataProtector().Unprotect(token);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;   // tampered or expired
        }
        if (!Guid.TryParse(id, out var operatorId))
            return null;
        var op = await db.Operators.AsNoTracking().SingleOrDefaultAsync(o => o.Id == operatorId && o.IsActive, cancellationToken);
        return op is null ? null : new OperatorIdentity(op.Id, op.Email, op.DisplayName);
    }
}

internal sealed class SupportAccess(
    TenancyCatalogDbContext db,
    CatalogDbContext catalog,
    IIdentityClient identity,
    TimeProvider time) : ISupportAccess
{
    /// <summary>Operators never get more than manager, and only in one module at a time.</summary>
    private static readonly Role MaxRole = Role.Manager;

    public async Task<SupportAccessDto> RequestAsync(OperatorIdentity operatorIdentity, SupportAccessRequest request, CancellationToken cancellationToken = default)
    {
        var slug = request.TenantSlug.ToLowerInvariant();
        var tenant = await catalog.Tenants.SingleOrDefaultAsync(t => t.Slug == slug, cancellationToken)
            ?? throw new NotFoundException(TenancyErrors.TenantNotFound);
        if (tenant.Status is TenantStatus.Deleted or TenantStatus.Provisioning)
            throw new BusinessRuleException(TenancyErrors.SupportAccessInvalid, "The tenant is not available.");
        if (request.Role > MaxRole || request.Reason.Trim().Length < 10 || Modules.All.All(m => m.Key != request.Module))
            throw new BusinessRuleException(TenancyErrors.SupportAccessInvalid, "Give a reason (10+ characters), a module, and a role of manager or lower.");
        var duration = TimeSpan.FromMinutes(Math.Clamp(request.Minutes, 5, (int)SupportAccessGrant.MaxDuration.TotalMinutes));

        var now = time.GetUtcNow().UtcDateTime;
        var grant = new SupportAccessGrant(tenant.Id, operatorIdentity.Id, request.Reason.Trim(), request.Role, request.Module, now.Add(duration), autoApprove: !tenant.RequireSupportApproval);
        db.SupportAccessGrants.Add(grant);
        db.AuditLog.Add(new TenancyAuditEntry(now, tenant.Id, "support-access-requested", operatorIdentity.Id.ToString(),
            JsonSerializer.Serialize(new { request.Module, role = request.Role.ToClaimValue(), request.Reason, status = grant.Status.ToString() })));
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(grant, tenant.Slug, operatorIdentity.DisplayName);
    }

    public async Task<IReadOnlyList<SupportAccessDto>> ListForTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var slug = (await catalog.Tenants.AsNoTracking().SingleAsync(t => t.Id == tenantId, cancellationToken)).Slug;
        var grants = await db.SupportAccessGrants.AsNoTracking().Where(g => g.TenantId == tenantId).OrderByDescending(g => g.CreatedOn).ToListAsync(cancellationToken);
        var names = await db.Operators.AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.DisplayName, cancellationToken);
        return [.. grants.Select(g => ToDto(g, slug, names.GetValueOrDefault(g.OperatorId, "operator")))];
    }

    public async Task<SupportAccessDto> DecideAsync(Guid tenantId, Guid grantId, SupportAccessStatus decision, string decidedBy, CancellationToken cancellationToken = default)
    {
        var grant = await db.SupportAccessGrants.SingleOrDefaultAsync(g => g.Id == grantId && g.TenantId == tenantId, cancellationToken)
            ?? throw new NotFoundException(TenancyErrors.GrantNotFound);
        switch (decision)
        {
            case SupportAccessStatus.Approved when grant.Status == SupportAccessStatus.Pending:
                grant.Approve(decidedBy);
                break;
            case SupportAccessStatus.Denied when grant.Status == SupportAccessStatus.Pending:
                grant.Deny(decidedBy);
                break;
            case SupportAccessStatus.Revoked when grant.Status is SupportAccessStatus.Approved or SupportAccessStatus.Pending:
                grant.Revoke(decidedBy);
                break;
            default:
                throw new BusinessRuleException(TenancyErrors.SupportAccessInvalid, $"A {grant.Status} grant can't become {decision}.");
        }
        db.AuditLog.Add(new TenancyAuditEntry(time.GetUtcNow().UtcDateTime, tenantId, $"support-access-{decision.ToString().ToLowerInvariant()}", decidedBy, JsonSerializer.Serialize(new { grantId })));
        await db.SaveChangesAsync(cancellationToken);
        var slug = (await catalog.Tenants.AsNoTracking().SingleAsync(t => t.Id == tenantId, cancellationToken)).Slug;
        var op = await db.Operators.AsNoTracking().SingleAsync(o => o.Id == grant.OperatorId, cancellationToken);
        return ToDto(grant, slug, op.DisplayName);
    }

    public async Task<SupportTokenDto> IssueTokenAsync(OperatorIdentity operatorIdentity, Guid grantId, CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var grant = await db.SupportAccessGrants.AsNoTracking().SingleOrDefaultAsync(g => g.Id == grantId && g.OperatorId == operatorIdentity.Id, cancellationToken)
            ?? throw new NotFoundException(TenancyErrors.GrantNotFound);
        if (!grant.IsUsable(now))
            throw new ForbiddenException(TenancyErrors.SupportAccessNotUsable);
        var tenant = await catalog.Tenants.AsNoTracking().SingleAsync(t => t.Id == grant.TenantId, cancellationToken);
        if (tenant.Status == TenantStatus.Deleted)
            throw new ForbiddenException(TenancyErrors.SupportAccessNotUsable);

        // The token is for the one service that owns the granted module, never for others.
        var audience = PlatformServices.All.First(s => s.OwnedModules.Contains(grant.Module)).Audience;
        var token = await identity.IssueSupportTokenAsync(
            grant.TenantId,
            new SupportTokenCall(grant.Id, operatorIdentity.Id.ToString(), operatorIdentity.DisplayName, grant.Module, grant.Role, audience, grant.Reason),
            cancellationToken);
        db.AuditLog.Add(new TenancyAuditEntry(now, grant.TenantId, "support-token-issued", operatorIdentity.Id.ToString(), JsonSerializer.Serialize(new { grantId, audience })));
        await db.SaveChangesAsync(cancellationToken);
        return token;
    }

    private static SupportAccessDto ToDto(SupportAccessGrant g, string slug, string operatorName) =>
        new(g.Id, g.TenantId, slug, g.OperatorId, operatorName, g.Reason, g.Role, g.Module, g.ExpiresOn, g.Status);
}

internal sealed class ModuleManifestProvider(
    ITenantContext tenant,
    CatalogDbContext catalog,
    IOptions<FrontendOptions> options) : IModuleManifestProvider
{
    public async Task<ModuleManifest> ForCurrentTenantAsync(CancellationToken cancellationToken = default)
    {
        // Tenant manifests are data: module keys and versions from the catalog; the base URL comes from configuration.
        var rows = await (from tm in catalog.TenantModules
                          join m in catalog.Modules on tm.ModuleId equals m.ModuleId
                          where tm.TenantId == tenant.Tenant.Id
                          orderby m.ModuleKey
                          select new { m.ModuleKey, tm.FrontendVersion }).ToListAsync(cancellationToken);
        var baseUrl = options.Value.ModuleBaseUrl.TrimEnd('/');
        // Platform modules (access-management, tenant-settings, billing) have no remote of their own yet; only business modules do.
        var withRemotes = Modules.All.Where(m => PlatformServices.All.Any(s => s.Key == m.Key && s.OwnedModules.Contains(m.Key))).Select(m => m.Key).ToHashSet();
        return new ModuleManifest([.. rows
            .Where(r => withRemotes.Contains(r.ModuleKey))
            .Select(r => new ModuleManifestEntry(r.ModuleKey, $"{baseUrl}/{r.ModuleKey}/{r.FrontendVersion ?? options.Value.DefaultModuleVersion}/mf-manifest.json"))]);
    }
}

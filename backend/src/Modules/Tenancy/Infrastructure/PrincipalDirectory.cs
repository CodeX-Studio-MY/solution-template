using System.Diagnostics;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using Saaskit.Core.Catalog;
using Saaskit.Core.Catalog.Entities;
using Saaskit.Core.Data;
using Saaskit.Core.Errors;
using Saaskit.Core.Http;
using Saaskit.Core.Tenancy;
using Saaskit.Tenancy.Application;
using Saaskit.Tenancy.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Saaskit.Tenancy.Infrastructure;

internal sealed class PrincipalDirectory(
    TenancyCatalogDbContext db,
    CatalogDbContext catalog,
    IHttpContextAccessor http,
    TimeProvider time) : IPrincipalDirectory
{
    private const int MaxRemoteAddressLength = 45;

    // ---- Principals -----------------------------------------------------------------------------

    public async Task<Page<PrincipalSummary>> ListAsync(PlatformPrincipalType? type, string? search, PageRequest page, CancellationToken cancellationToken = default)
    {
        IQueryable<PlatformPrincipal> query = db.Principals.AsNoTracking();
        if (type is not null)
            query = query.Where(p => p.Type == type);
        if (search?.Trim() is { Length: > 0 } term)
        {
            query = query.Where(p => p.DisplayName.Contains(term)
                || db.PrincipalUsers.Any(u => u.PrincipalUserId == p.Id && u.Email.Contains(term))
                || db.PrincipalDevices.Any(d => d.PrincipalDeviceId == p.Id && d.Identifier.Contains(term))
                || db.PrincipalApplications.Any(a => a.PrincipalApplicationId == p.Id && a.OwnerName.Contains(term)));
        }

        var found = await query.OrderBy(p => p.DisplayName).ThenBy(p => p.Id).ToPageAsync(page, cancellationToken);
        var ids = found.Items.Select(p => p.Id).ToList();
        var details = await DetailsAsync(ids, cancellationToken);
        // Revoked access is history, not reach.
        var counts = await db.TenantPrincipalAccess.AsNoTracking()
            .Where(a => ids.Contains(a.PrincipalId) && a.Status != TenantAccessStatus.Revoked)
            .GroupBy(a => a.PrincipalId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        return found.Map(p =>
        {
            var detail = details.GetValueOrDefault(p.Id);
            return new PrincipalSummary(p.Id, p.Type, p.DisplayName, p.IsActive, detail?.Identifier, detail?.DeviceType, detail?.IsExternal, counts.GetValueOrDefault(p.Id));
        });
    }

    public async Task<PrincipalDetail> GetAsync(Guid principalId, CancellationToken cancellationToken = default) =>
        await ToDetailAsync(await FindAsync(principalId, cancellationToken), cancellationToken);

    public async Task<PrincipalDetail> CreateAsync(PrincipalInput input, Guid actor, CancellationToken cancellationToken = default)
    {
        var clean = Validate(input);
        await RefuseDuplicateAsync(clean, null, cancellationToken);

        var principal = new PlatformPrincipal(clean.Type, clean.DisplayName);
        db.Principals.Add(principal);   // the provider generates the key here, so the details row can share it
        switch (clean.Type)
        {
            case PlatformPrincipalType.User:
                db.PrincipalUsers.Add(new PrincipalUser(principal.Id, clean.Email!));
                break;
            case PlatformPrincipalType.Device:
                db.PrincipalDevices.Add(new PrincipalDevice(principal.Id, clean.DeviceType!.Value, clean.Identifier!, clean.Manufacturer, clean.Model));
                break;
            default:
                db.PrincipalApplications.Add(new PrincipalApplication(principal.Id, clean.IsExternal!.Value, clean.OwnerName!, clean.ContactEmail));
                break;
        }

        Log("principal-created", actor, new { principalId = principal.Id, clean.Type });
        await SaveDetailsAsync(cancellationToken);
        return await ToDetailAsync(principal, cancellationToken);
    }

    public async Task<PrincipalDetail> UpdateAsync(Guid principalId, PrincipalInput input, string? ifMatch, Guid actor, CancellationToken cancellationToken = default)
    {
        var clean = Validate(input);
        var principal = await FindAsync(principalId, cancellationToken);
        if (clean.Type != principal.Type)
            throw new ValidationException([new FieldError("type", "validation.invalid", ["type"])]);
        db.ApplyIfMatch(principal, ifMatch);
        await RefuseDuplicateAsync(clean, principalId, cancellationToken);

        principal.Rename(clean.DisplayName);
        switch (principal.Type)
        {
            case PlatformPrincipalType.User:
                (await db.PrincipalUsers.SingleAsync(u => u.PrincipalUserId == principalId, cancellationToken)).Change(clean.Email!);
                break;
            case PlatformPrincipalType.Device:
                (await db.PrincipalDevices.SingleAsync(d => d.PrincipalDeviceId == principalId, cancellationToken)).Change(clean.DeviceType!.Value, clean.Identifier!, clean.Manufacturer, clean.Model);
                break;
            default:
                (await db.PrincipalApplications.SingleAsync(a => a.PrincipalApplicationId == principalId, cancellationToken)).Change(clean.IsExternal!.Value, clean.OwnerName!, clean.ContactEmail);
                break;
        }

        // The details have no audit columns: changing them still counts as changing the principal.
        db.Entry(principal).State = EntityState.Modified;
        Log("principal-changed", actor, new { principalId });
        await SaveDetailsAsync(cancellationToken);
        return await ToDetailAsync(principal, cancellationToken);
    }

    public async Task<PrincipalDetail> SetActiveAsync(Guid principalId, bool active, string? ifMatch, Guid actor, CancellationToken cancellationToken = default)
    {
        var principal = await FindAsync(principalId, cancellationToken);
        db.ApplyIfMatch(principal, ifMatch);
        principal.SetActive(active);
        Log(active ? "principal-activated" : "principal-deactivated", actor, new { principalId });
        await db.SaveChangesAsync(cancellationToken);
        return await ToDetailAsync(principal, cancellationToken);
    }

    // ---- Tenant access --------------------------------------------------------------------------

    public async Task<Page<TenantPrincipalRow>> ListForTenantAsync(Guid tenantId, PageRequest page, CancellationToken cancellationToken = default)
    {
        await FindTenantAsync(tenantId, cancellationToken);
        var found = await (from access in db.TenantPrincipalAccess.AsNoTracking()
                           join principal in db.Principals.AsNoTracking() on access.PrincipalId equals principal.Id
                           where access.TenantId == tenantId
                           orderby principal.DisplayName, principal.Id
                           select new { access, principal }).ToPageAsync(page, cancellationToken);
        var details = await DetailsAsync([.. found.Items.Select(r => r.principal.Id)], cancellationToken);
        var operators = await OperatorNamesAsync(found.Items.Select(r => r.access.GrantedBy), cancellationToken);
        return found.Map(r => ToRow(r.access, r.principal, details.GetValueOrDefault(r.principal.Id), operators.GetValueOrDefault(r.access.GrantedBy)));
    }

    public async Task<TenantPrincipalRow> GrantAsync(Guid tenantId, Guid principalId, string? reason, Guid actor, CancellationToken cancellationToken = default)
    {
        var note = string.IsNullOrWhiteSpace(reason) ? null : ValidateReason(reason);
        var tenant = await FindTenantAsync(tenantId, cancellationToken);
        if (tenant.Status == TenantStatus.Deleted)
            throw new BusinessRuleException(TenancyErrors.TenantAccessInvalid);
        var principal = await FindAsync(principalId, cancellationToken);
        if (!principal.IsActive)
            throw new BusinessRuleException(TenancyErrors.PrincipalInactive);

        var now = time.GetUtcNow().UtcDateTime;
        var access = await db.TenantPrincipalAccess.SingleOrDefaultAsync(a => a.TenantId == tenantId && a.PrincipalId == principalId, cancellationToken);
        switch (access?.Status)
        {
            case TenantAccessStatus.Active:
                return await RowAsync(access, principal, cancellationToken);
            case TenantAccessStatus.Suspended:
                throw new BusinessRuleException(TenancyErrors.TenantAccessInvalid);
            case TenantAccessStatus.Revoked:
                access.Grant(now, actor);
                Audit(now, tenantId, principalId, TenantAccessAction.Granted, TenantAccessStatus.Revoked, TenantAccessStatus.Active, actor, note);
                break;
            default:
                access = new TenantPrincipalAccess(tenantId, principalId, now, actor);
                db.TenantPrincipalAccess.Add(access);
                Audit(now, tenantId, principalId, TenantAccessAction.Granted, null, TenantAccessStatus.Active, actor, note);
                break;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException e) when (e is not DbUpdateConcurrencyException)
        {
            throw new ConflictException(TenancyErrors.TenantAccessInvalid);   // another operator granted it first (the unique index)
        }

        return await RowAsync(access, principal, cancellationToken);
    }

    public async Task<TenantPrincipalRow> SetStatusAsync(Guid tenantId, Guid principalId, TenantAccessStatus status, string reason, Guid actor, CancellationToken cancellationToken = default)
    {
        var note = ValidateReason(reason);
        var access = await db.TenantPrincipalAccess.SingleOrDefaultAsync(a => a.TenantId == tenantId && a.PrincipalId == principalId, cancellationToken)
            ?? throw new NotFoundException(TenancyErrors.TenantAccessNotFound);
        var principal = await FindAsync(principalId, cancellationToken);
        var action = access.ActionFor(status) ?? throw new BusinessRuleException(TenancyErrors.TenantAccessInvalid);
        if (action == TenantAccessAction.Resumed && !principal.IsActive)
            throw new BusinessRuleException(TenancyErrors.PrincipalInactive);

        var from = access.Status;
        access.MoveTo(status);
        Audit(time.GetUtcNow().UtcDateTime, tenantId, principalId, action, from, status, actor, note);
        await db.SaveChangesAsync(cancellationToken);   // the change and its audit row together
        return await RowAsync(access, principal, cancellationToken);
    }

    // ---- History --------------------------------------------------------------------------------

    public async Task<Page<AccessAuditRow>> ListAuditForTenantAsync(Guid tenantId, PageRequest page, CancellationToken cancellationToken = default)
    {
        await FindTenantAsync(tenantId, cancellationToken);
        return await AuditPageAsync(db.TenantPrincipalAccessAudit.AsNoTracking().Where(a => a.TenantId == tenantId), page, cancellationToken);
    }

    public async Task<Page<AccessAuditRow>> ListAuditForPrincipalAsync(Guid principalId, Guid? tenantId, PageRequest page, CancellationToken cancellationToken = default)
    {
        await FindAsync(principalId, cancellationToken);
        var query = db.TenantPrincipalAccessAudit.AsNoTracking().Where(a => a.PrincipalId == principalId);
        if (tenantId is not null)
            query = query.Where(a => a.TenantId == tenantId);
        return await AuditPageAsync(query, page, cancellationToken);
    }

    public async Task UnlinkProviderAsync(Guid principalId, long principalUserProviderId, Guid actor, CancellationToken cancellationToken = default)
    {
        await FindAsync(principalId, cancellationToken);
        var link = await db.PrincipalUserProviders.SingleOrDefaultAsync(p => p.PrincipalUserProviderId == principalUserProviderId && p.PrincipalUserId == principalId, cancellationToken)
            ?? throw new NotFoundException(TenancyErrors.LinkedAccountNotFound);
        var providerKey = await db.IdentityProviders.AsNoTracking().Where(p => p.IdentityProviderId == link.IdentityProviderId).Select(p => p.ProviderKey).SingleAsync(cancellationToken);
        db.PrincipalUserProviders.Remove(link);
        Log("principal-provider-unlinked", actor, new { principalId, providerKey });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<Page<LoginRow>> ListLoginsAsync(Guid tenantId, PageRequest page, CancellationToken cancellationToken = default)
    {
        await FindTenantAsync(tenantId, cancellationToken);
        var found = await db.TenantPrincipalLogins.AsNoTracking().Where(l => l.TenantId == tenantId).OrderByDescending(l => l.TenantPrincipalLoginId).ToPageAsync(page, cancellationToken);
        var names = await PrincipalNamesAsync(found.Items.Select(l => l.PrincipalId).OfType<Guid>(), cancellationToken);
        return found.Map(l => new LoginRow(l.TenantPrincipalLoginId, l.OccurredOn, l.PrincipalId, l.PrincipalId is { } id ? names.GetValueOrDefault(id) : null, l.PrincipalType, l.Succeeded, l.Method, l.FailureReason, l.RemoteAddress, l.UserAgent));
    }

    private async Task<Page<AccessAuditRow>> AuditPageAsync(IQueryable<TenantPrincipalAccessAudit> query, PageRequest page, CancellationToken cancellationToken)
    {
        var found = await query.OrderByDescending(a => a.TenantPrincipalAccessAuditId).ToPageAsync(page, cancellationToken);
        var operators = await OperatorNamesAsync(found.Items.Select(a => a.ActorId), cancellationToken);
        var principals = await PrincipalNamesAsync(found.Items.Select(a => a.PrincipalId), cancellationToken);
        var tenantIds = found.Items.Select(a => a.TenantId).Distinct().ToList();
        var tenants = await catalog.Tenants.AsNoTracking().Where(t => tenantIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.DisplayName, cancellationToken);
        return found.Map(a => new AccessAuditRow(
            a.TenantPrincipalAccessAuditId, a.OccurredOn, a.TenantId, tenants.GetValueOrDefault(a.TenantId), a.PrincipalId, principals.GetValueOrDefault(a.PrincipalId),
            a.Action, a.FromStatus, a.ToStatus, a.ActorId, operators.GetValueOrDefault(a.ActorId), a.Reason, a.RemoteAddress));
    }

    // ---- Helpers --------------------------------------------------------------------------------

    private sealed record Details(string? Identifier, DeviceType? DeviceType, bool? IsExternal);

    private async Task<Dictionary<Guid, Details>> DetailsAsync(List<Guid> ids, CancellationToken cancellationToken)
    {
        var users = await db.PrincipalUsers.AsNoTracking().Where(u => ids.Contains(u.PrincipalUserId)).ToListAsync(cancellationToken);
        var devices = await db.PrincipalDevices.AsNoTracking().Where(d => ids.Contains(d.PrincipalDeviceId)).ToListAsync(cancellationToken);
        var applications = await db.PrincipalApplications.AsNoTracking().Where(a => ids.Contains(a.PrincipalApplicationId)).ToListAsync(cancellationToken);
        return users.Select(u => (u.PrincipalUserId, new Details(u.Email, null, null)))
            .Concat(devices.Select(d => (d.PrincipalDeviceId, new Details(d.Identifier, d.DeviceType, null))))
            .Concat(applications.Select(a => (a.PrincipalApplicationId, new Details(a.OwnerName, null, a.IsExternal))))
            .ToDictionary(x => x.Item1, x => x.Item2);
    }

    private async Task<PrincipalDetail> ToDetailAsync(PlatformPrincipal principal, CancellationToken cancellationToken)
    {
        var id = principal.Id;
        var user = await db.PrincipalUsers.AsNoTracking().SingleOrDefaultAsync(u => u.PrincipalUserId == id, cancellationToken);
        var device = await db.PrincipalDevices.AsNoTracking().SingleOrDefaultAsync(d => d.PrincipalDeviceId == id, cancellationToken);
        var application = await db.PrincipalApplications.AsNoTracking().SingleOrDefaultAsync(a => a.PrincipalApplicationId == id, cancellationToken);
        var access = await db.TenantPrincipalAccess.AsNoTracking().Where(a => a.PrincipalId == id).ToListAsync(cancellationToken);
        var tenantIds = access.Select(a => a.TenantId).ToList();
        var tenants = await catalog.Tenants.AsNoTracking().Where(t => tenantIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, cancellationToken);
        var providers = await (from link in db.PrincipalUserProviders.AsNoTracking()
                               join settings in db.IdentityProviders.AsNoTracking() on link.IdentityProviderId equals settings.IdentityProviderId
                               where link.PrincipalUserId == id
                               orderby settings.ProviderType, settings.DisplayName, link.PrincipalUserProviderId
                               select new LinkedProvider(link.PrincipalUserProviderId, settings.IdentityProviderId, settings.ProviderType, settings.ProviderKey, settings.DisplayName,
                                   link.ProviderEmail, link.ProviderEmailVerified, link.LinkedOn, link.LastUsedOn)).ToListAsync(cancellationToken);
        return new PrincipalDetail(
            id, principal.Type, principal.DisplayName, principal.IsActive,
            user?.Email,
            device?.DeviceType, device?.Identifier, device?.Manufacturer, device?.Model,
            application?.IsExternal, application?.OwnerName, application?.ContactEmail,
            [.. access.Where(a => tenants.ContainsKey(a.TenantId))
                .Select(a => new PrincipalTenantAccess(a.TenantId, tenants[a.TenantId].Slug, tenants[a.TenantId].DisplayName, a.Status, a.GrantedOn))
                .OrderBy(a => a.TenantName, StringComparer.CurrentCulture)],
            providers,
            db.GetETag(principal));
    }

    private async Task<TenantPrincipalRow> RowAsync(TenantPrincipalAccess access, PlatformPrincipal principal, CancellationToken cancellationToken)
    {
        var details = await DetailsAsync([principal.Id], cancellationToken);
        var operators = await OperatorNamesAsync([access.GrantedBy], cancellationToken);
        return ToRow(access, principal, details.GetValueOrDefault(principal.Id), operators.GetValueOrDefault(access.GrantedBy));
    }

    private static TenantPrincipalRow ToRow(TenantPrincipalAccess access, PlatformPrincipal principal, Details? details, string? grantedByName) =>
        new(principal.Id, principal.Type, principal.DisplayName, principal.IsActive, details?.Identifier, details?.DeviceType, details?.IsExternal, access.Status, access.GrantedOn, grantedByName);

    private async Task<Dictionary<Guid, string>> OperatorNamesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var distinct = ids.Distinct().ToList();
        return await db.Operators.AsNoTracking().Where(o => distinct.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.DisplayName, cancellationToken);
    }

    private async Task<Dictionary<Guid, string>> PrincipalNamesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var distinct = ids.Distinct().ToList();
        return await db.Principals.AsNoTracking().Where(p => distinct.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.DisplayName, cancellationToken);
    }

    private async Task<PlatformPrincipal> FindAsync(Guid principalId, CancellationToken cancellationToken) =>
        await db.Principals.SingleOrDefaultAsync(p => p.Id == principalId, cancellationToken)
        ?? throw new NotFoundException(TenancyErrors.PrincipalNotFound);

    private async Task<CatalogTenant> FindTenantAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await catalog.Tenants.AsNoTracking().SingleOrDefaultAsync(t => t.Id == tenantId, cancellationToken)
        ?? throw new NotFoundException(TenancyErrors.TenantNotFound);

    private void Audit(DateTime now, Guid tenantId, Guid principalId, TenantAccessAction action, TenantAccessStatus? from, TenantAccessStatus to, Guid actor, string? reason)
    {
        var address = http.HttpContext?.Connection.RemoteIpAddress?.ToString();
        db.TenantPrincipalAccessAudit.Add(new TenantPrincipalAccessAudit(
            now, tenantId, principalId, action, from, to, actor, reason,
            address?[..Math.Min(address.Length, MaxRemoteAddressLength)],
            Activity.Current?.TraceId.ToString()));
    }

    private void Log(string kind, Guid actor, object details) =>
        db.AuditLog.Add(new TenancyAuditEntry(time.GetUtcNow().UtcDateTime, null, kind, actor.ToString(), JsonSerializer.Serialize(details)));

    private async Task SaveDetailsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException e) when (e is not DbUpdateConcurrencyException)
        {
            throw new ConflictException(TenancyErrors.PrincipalExists);   // lost a race the check before saving can't see (the unique index)
        }
    }

    private async Task RefuseDuplicateAsync(PrincipalInput clean, Guid? self, CancellationToken cancellationToken)
    {
        var exists = clean.Type switch
        {
            PlatformPrincipalType.User => await db.PrincipalUsers.AnyAsync(u => u.Email == clean.Email && u.PrincipalUserId != self, cancellationToken),
            PlatformPrincipalType.Device => await db.PrincipalDevices.AnyAsync(d => d.DeviceType == clean.DeviceType && d.Identifier == clean.Identifier && d.PrincipalDeviceId != self, cancellationToken),
            _ => false,
        };
        if (exists)
            throw new ConflictException(TenancyErrors.PrincipalExists);
    }

    private static string ValidateReason(string? reason)
    {
        var clean = reason?.Trim() ?? "";
        if (clean.Length == 0)
            throw new ValidationException([new FieldError("reason", "validation.required", ["reason"])]);
        if (clean.Length > TenantPrincipalAccessAudit.MaxReasonLength)
            throw new ValidationException([new FieldError("reason", "validation.maxLength", ["reason", TenantPrincipalAccessAudit.MaxReasonLength])]);
        return clean;
    }

    private static PrincipalInput Validate(PrincipalInput input)
    {
        var errors = new List<FieldError>();
        var name = input.DisplayName?.Trim() ?? "";
        Required("displayName", name, PlatformPrincipal.MaxDisplayNameLength, errors);
        if (!Enum.IsDefined(input.Type))
            errors.Add(new FieldError("type", "validation.invalid", ["type"]));

        // Only the fields of this type are kept, so a principal never has another type's details.
        var clean = new PrincipalInput(input.Type, name);
        switch (input.Type)
        {
            case PlatformPrincipalType.User:
                clean = clean with { Email = ValidEmail("email", input.Email, required: true, errors) };
                break;
            case PlatformPrincipalType.Device:
                var identifier = input.Identifier?.Trim().ToUpperInvariant() ?? "";
                Required("identifier", identifier, PrincipalDevice.MaxIdentifierLength, errors);
                if (!Ascii.IsValid(identifier))
                    errors.Add(new FieldError("identifier", "validation.invalid", ["identifier"]));
                if (input.DeviceType is not { } deviceType || !Enum.IsDefined(deviceType))
                    errors.Add(new FieldError("deviceType", "validation.required", ["deviceType"]));
                clean = clean with
                {
                    DeviceType = input.DeviceType,
                    Identifier = identifier,
                    Manufacturer = Optional("manufacturer", input.Manufacturer, PrincipalDevice.MaxMakerLength, errors),
                    Model = Optional("model", input.Model, PrincipalDevice.MaxMakerLength, errors),
                };
                break;
            case PlatformPrincipalType.Application:
                var owner = input.OwnerName?.Trim() ?? "";
                Required("ownerName", owner, PrincipalApplication.MaxOwnerNameLength, errors);
                if (input.IsExternal is null)
                    errors.Add(new FieldError("isExternal", "validation.required", ["isExternal"]));
                clean = clean with { IsExternal = input.IsExternal, OwnerName = owner, ContactEmail = ValidEmail("contactEmail", input.ContactEmail, required: false, errors) };
                break;
        }

        return errors.Count > 0 ? throw new ValidationException(errors) : clean;
    }

    private static void Required(string field, string value, int maxLength, List<FieldError> errors)
    {
        if (value.Length == 0)
            errors.Add(new FieldError(field, "validation.required", [field]));
        else if (value.Length > maxLength)
            errors.Add(new FieldError(field, "validation.maxLength", [field, maxLength]));
    }

    private static string? Optional(string field, string? value, int maxLength, List<FieldError> errors)
    {
        var clean = value?.Trim();
        if (clean?.Length > maxLength)
            errors.Add(new FieldError(field, "validation.maxLength", [field, maxLength]));
        return string.IsNullOrEmpty(clean) ? null : clean;
    }

    /// <summary>Lowercase ASCII: the column is varchar, and an exact match on it uses the unique index.</summary>
    private static string? ValidEmail(string field, string? value, bool required, List<FieldError> errors)
    {
        var clean = value?.Trim().ToLowerInvariant() ?? "";
        if (clean.Length == 0)
        {
            if (required)
                errors.Add(new FieldError(field, "validation.required", [field]));
            return null;
        }

        if (clean.Length > PrincipalUser.MaxEmailLength)
            errors.Add(new FieldError(field, "validation.maxLength", [field, PrincipalUser.MaxEmailLength]));
        else if (!Ascii.IsValid(clean) || !MailAddress.TryCreate(clean, out var parsed) || parsed.Address != clean)
            errors.Add(new FieldError(field, "validation.invalid", [field]));
        return clean;
    }
}

using System.Text.Json;
using Saaskit.Core.Configuration;
using Saaskit.Core.Data;
using Saaskit.Core.Errors;
using Saaskit.Core.Platform;
using Saaskit.Tenancy.Application;
using Saaskit.Tenancy.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Saaskit.Tenancy.Infrastructure;

internal sealed partial class IdentityProviderCatalog(
    TenancyCatalogDbContext db,
    IDataProtectionProvider protection,
    IOptions<TenancyOptions> tenancyOptions,
    TimeProvider time) : IIdentityProviderCatalog
{
    /// <summary>The protector purpose of the stored client secrets.</summary>
    public const string SecretPurpose = "tenancy.identity-provider.secret";

    public async Task<IReadOnlyList<IdentityProviderRow>> ListAsync(CancellationToken cancellationToken = default)
    {
        // Tracked, because the ETag is read from the row version.
        var providers = await db.IdentityProviders.OrderBy(p => p.ProviderType).ThenBy(p => p.IdentityProviderId).ToListAsync(cancellationToken);
        return await RowsAsync(providers, cancellationToken);
    }

    public async Task<IdentityProviderRow> CreateAsync(IdentityProviderCreate input, Guid actor, CancellationToken cancellationToken = default)
    {
        var errors = new List<FieldError>();
        if (!Enum.IsDefined(input.ProviderType))
            errors.Add(new FieldError("providerType", "validation.invalid", ["providerType"]));
        var key = input.ProviderKey?.Trim().ToLowerInvariant() ?? "";
        if (!KeyRules.Pattern.IsMatch(key))
            errors.Add(new FieldError("providerKey", "validation.invalid", ["providerKey"]));
        var fields = Validate(input.ProviderType, input.DisplayName, input.ClientId, input.ClientSecret, input.Authority, input.AppleTeamId, input.AppleKeyId, errors);
        if (await db.IdentityProviders.AnyAsync(p => p.ProviderKey == key, cancellationToken))
            throw new ConflictException(TenancyErrors.ProviderKeyInUse);

        var provider = new IdentityProvider(input.ProviderType, key, fields.DisplayName);
        provider.Change(fields.DisplayName, fields.ClientId, Protect(fields.ClientSecret), fields.Authority, fields.AppleTeamId, fields.AppleKeyId);
        db.IdentityProviders.Add(provider);
        Log("identity-provider-created", actor, new { providerKey = key, type = input.ProviderType, secretSet = fields.ClientSecret is not null });
        await SaveAsync(TenancyErrors.ProviderKeyInUse, cancellationToken);
        return (await RowsAsync([provider], cancellationToken))[0];
    }

    public async Task<IdentityProviderRow> UpdateAsync(short id, IdentityProviderUpdate input, string? ifMatch, Guid actor, CancellationToken cancellationToken = default)
    {
        var provider = await FindAsync(id, cancellationToken);
        var fields = Validate(provider.ProviderType, input.DisplayName, input.ClientId, input.ClientSecret, input.Authority, input.AppleTeamId, input.AppleKeyId, []);
        db.ApplyIfMatch(provider, ifMatch);
        provider.Change(fields.DisplayName, fields.ClientId, Protect(fields.ClientSecret), fields.Authority, fields.AppleTeamId, fields.AppleKeyId);
        // A provider that is on must stay complete: blanking a setting would break its login button.
        if (provider.IsEnabled && !provider.IsComplete)
            throw new BusinessRuleException(TenancyErrors.ProviderIncomplete, string.Join(", ", provider.MissingSettings()));
        Log("identity-provider-changed", actor, new { providerKey = provider.ProviderKey, secretChanged = fields.ClientSecret is not null });
        await SaveAsync(null, cancellationToken);
        return (await RowsAsync([provider], cancellationToken))[0];
    }

    public async Task<IdentityProviderRow> SetEnabledAsync(short id, bool enabled, string? ifMatch, Guid actor, CancellationToken cancellationToken = default)
    {
        var provider = await FindAsync(id, cancellationToken);
        db.ApplyIfMatch(provider, ifMatch);
        if (enabled && !provider.IsComplete)
            throw new BusinessRuleException(TenancyErrors.ProviderIncomplete, string.Join(", ", provider.MissingSettings()));
        provider.SetEnabled(enabled);
        Log(enabled ? "identity-provider-enabled" : "identity-provider-disabled", actor, new { providerKey = provider.ProviderKey });
        await SaveAsync(null, cancellationToken);
        return (await RowsAsync([provider], cancellationToken))[0];
    }

    public async Task DeleteAsync(short id, Guid actor, CancellationToken cancellationToken = default)
    {
        var provider = await FindAsync(id, cancellationToken);
        if (await db.PrincipalUserProviders.AnyAsync(p => p.IdentityProviderId == id, cancellationToken))
            throw new ConflictException(TenancyErrors.ProviderHasAccounts);
        db.IdentityProviders.Remove(provider);
        Log("identity-provider-deleted", actor, new { providerKey = provider.ProviderKey });
        await SaveAsync(TenancyErrors.ProviderHasAccounts, cancellationToken);   // an account linked meanwhile trips the foreign key
    }

    // ---- Helpers --------------------------------------------------------------------------------

    private async Task<List<IdentityProviderRow>> RowsAsync(IReadOnlyCollection<IdentityProvider> providers, CancellationToken cancellationToken)
    {
        var ids = providers.Select(p => p.IdentityProviderId).ToList();
        var counts = await db.PrincipalUserProviders.AsNoTracking()
            .Where(p => ids.Contains(p.IdentityProviderId))
            .GroupBy(p => p.IdentityProviderId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        return [.. providers.Select(p => new IdentityProviderRow(
            p.IdentityProviderId, p.ProviderType, p.ProviderKey, p.DisplayName, p.IsEnabled, p.ClientId,
            !string.IsNullOrEmpty(p.ClientSecretProtected), p.Authority, p.AppleTeamId, p.AppleKeyId,
            CallbackUrl(p.ProviderKey), counts.GetValueOrDefault(p.IdentityProviderId), db.GetETag(p)))];
    }

    /// <summary>The address to give the provider: on the sign-in host, so it is the same for every tenant. The issuer ends with a slash.</summary>
    private string CallbackUrl(string providerKey) => $"{tenancyOptions.Value.Issuer}account/external/{providerKey}/callback";

    private string? Protect(string? secret) => secret is null ? null : protection.CreateProtector(SecretPurpose).Protect(secret);

    private async Task<IdentityProvider> FindAsync(short id, CancellationToken cancellationToken) =>
        await db.IdentityProviders.SingleOrDefaultAsync(p => p.IdentityProviderId == id, cancellationToken)
        ?? throw new NotFoundException(TenancyErrors.ProviderNotFound);

    private void Log(string kind, Guid actor, object details) =>
        db.AuditLog.Add(new TenancyAuditEntry(time.GetUtcNow().UtcDateTime, null, kind, actor.ToString(), JsonSerializer.Serialize(details)));

    private async Task SaveAsync(ErrorCode? conflict, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException e) when (conflict is not null && e is not DbUpdateConcurrencyException)
        {
            throw new ConflictException(conflict);   // lost a race the check before saving can't see (the unique index or the foreign key)
        }
    }

    private sealed record Fields(string DisplayName, string? ClientId, string? ClientSecret, string? Authority, string? AppleTeamId, string? AppleKeyId);

    /// <summary>Settings of another kind are dropped, so a row never holds a Microsoft authority on a Google app. A blank secret is null (keep).</summary>
    private static Fields Validate(LoginProvider type, string? displayName, string? clientId, string? clientSecret, string? authority, string? teamId, string? keyId, List<FieldError> errors)
    {
        var name = displayName?.Trim() ?? "";
        if (name.Length == 0)
            errors.Add(new FieldError("displayName", "validation.required", ["displayName"]));
        else if (name.Length > IdentityProvider.MaxDisplayNameLength)
            errors.Add(new FieldError("displayName", "validation.maxLength", ["displayName", IdentityProvider.MaxDisplayNameLength]));

        var client = Blank(clientId);
        if (client is not null && (client.Length > IdentityProvider.MaxClientIdLength || !AsciiPrintable().IsMatch(client)))
            errors.Add(new FieldError("clientId", "validation.invalid", ["clientId"]));

        var secret = Blank(clientSecret);
        if (secret is not null && (secret.Length > IdentityProvider.MaxSecretLength || !System.Text.Ascii.IsValid(secret)))
            errors.Add(new FieldError("clientSecret", "validation.invalid", ["clientSecret"]));

        var tenant = type == LoginProvider.Microsoft ? Blank(authority) : null;
        if (tenant is not null && (tenant.Length > IdentityProvider.MaxAuthorityLength || !AuthorityPattern().IsMatch(tenant)))
            errors.Add(new FieldError("authority", "validation.invalid", ["authority"]));

        var team = type == LoginProvider.Apple ? Blank(teamId)?.ToUpperInvariant() : null;
        var key = type == LoginProvider.Apple ? Blank(keyId)?.ToUpperInvariant() : null;
        if (team is not null && !AppleIdPattern().IsMatch(team))
            errors.Add(new FieldError("appleTeamId", "validation.invalid", ["appleTeamId"]));
        if (key is not null && !AppleIdPattern().IsMatch(key))
            errors.Add(new FieldError("appleKeyId", "validation.invalid", ["appleKeyId"]));

        return errors.Count > 0 ? throw new ValidationException(errors) : new Fields(name, client, secret, tenant, team, key);
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [System.Text.RegularExpressions.GeneratedRegex(@"^[\x21-\x7E]+$")]
    private static partial System.Text.RegularExpressions.Regex AsciiPrintable();

    [System.Text.RegularExpressions.GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9.-]*$")]
    private static partial System.Text.RegularExpressions.Regex AuthorityPattern();

    [System.Text.RegularExpressions.GeneratedRegex(@"^[A-Z0-9]{10}$")]
    private static partial System.Text.RegularExpressions.Regex AppleIdPattern();
}

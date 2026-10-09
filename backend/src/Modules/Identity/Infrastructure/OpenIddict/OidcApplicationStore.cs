using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;

namespace Acme.Identity.Infrastructure.OpenIddict;

/// <summary>Client registrations, in the catalog database.</summary>
internal sealed class OidcApplicationStore(IdentityCatalogDbContext db) : IOpenIddictApplicationStore<OidcApplication>
{
    private DbSet<OidcApplication> Applications => db.OidcApplications;

    public async ValueTask<long> CountAsync(CancellationToken cancellationToken) => await Applications.LongCountAsync(cancellationToken);

    public async ValueTask<long> CountAsync<TResult>(Func<IQueryable<OidcApplication>, IQueryable<TResult>> query, CancellationToken cancellationToken) =>
        await query(Applications.AsQueryable()).LongCountAsync(cancellationToken);

    public async ValueTask CreateAsync(OidcApplication application, CancellationToken cancellationToken)
    {
        if (application.Id == Guid.Empty)
            application.Id = Guid.CreateVersion7();
        Applications.Add(application);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async ValueTask DeleteAsync(OidcApplication application, CancellationToken cancellationToken)
    {
        Applications.Remove(application);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async ValueTask<OidcApplication?> FindByIdAsync(string identifier, CancellationToken cancellationToken)
    {
        var id = OidcJson.ParseId(identifier);
        return await Applications.SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public async ValueTask<OidcApplication?> FindByClientIdAsync(string identifier, CancellationToken cancellationToken) =>
        await Applications.SingleOrDefaultAsync(a => a.ClientId == identifier, cancellationToken);

    public async IAsyncEnumerable<OidcApplication> FindByPostLogoutRedirectUriAsync(string uri, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Matched exactly after loading; never by wildcard.
        var candidates = await Applications.Where(a => a.PostLogoutRedirectUris != null && a.PostLogoutRedirectUris.Contains(uri)).ToListAsync(cancellationToken);
        foreach (var application in candidates.Where(a => OidcJson.Strings(a.PostLogoutRedirectUris).Contains(uri, StringComparer.Ordinal)))
            yield return application;
    }

    public async IAsyncEnumerable<OidcApplication> FindByRedirectUriAsync(string uri, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var candidates = await Applications.Where(a => a.RedirectUris != null && a.RedirectUris.Contains(uri)).ToListAsync(cancellationToken);
        foreach (var application in candidates.Where(a => OidcJson.Strings(a.RedirectUris).Contains(uri, StringComparer.Ordinal)))
            yield return application;
    }

    public ValueTask<string?> GetApplicationTypeAsync(OidcApplication application, CancellationToken cancellationToken) => new(application.ApplicationType);

    public async ValueTask<TResult?> GetAsync<TState, TResult>(Func<IQueryable<OidcApplication>, TState, IQueryable<TResult>> query, TState state, CancellationToken cancellationToken) =>
        await query(Applications.AsQueryable(), state).FirstOrDefaultAsync(cancellationToken);

    public ValueTask<string?> GetClientIdAsync(OidcApplication application, CancellationToken cancellationToken) => new(application.ClientId);

    public ValueTask<string?> GetClientSecretAsync(OidcApplication application, CancellationToken cancellationToken) => new(application.ClientSecret);

    public ValueTask<string?> GetClientTypeAsync(OidcApplication application, CancellationToken cancellationToken) => new(application.ClientType);

    public ValueTask<string?> GetConsentTypeAsync(OidcApplication application, CancellationToken cancellationToken) => new(application.ConsentType);

    public ValueTask<string?> GetDisplayNameAsync(OidcApplication application, CancellationToken cancellationToken) => new(application.DisplayName);

    public ValueTask<ImmutableDictionary<CultureInfo, string>> GetDisplayNamesAsync(OidcApplication application, CancellationToken cancellationToken) =>
        new(OidcJson.DisplayNames(application.DisplayNames));

    public ValueTask<string?> GetIdAsync(OidcApplication application, CancellationToken cancellationToken) => new(application.Id.ToString());

    public ValueTask<JsonWebKeySet?> GetJsonWebKeySetAsync(OidcApplication application, CancellationToken cancellationToken) =>
        new(OidcJson.KeySet(application.JsonWebKeySet));

    public ValueTask<ImmutableArray<string>> GetPermissionsAsync(OidcApplication application, CancellationToken cancellationToken) =>
        new(OidcJson.Strings(application.Permissions));

    public ValueTask<ImmutableArray<string>> GetPostLogoutRedirectUrisAsync(OidcApplication application, CancellationToken cancellationToken) =>
        new(OidcJson.Strings(application.PostLogoutRedirectUris));

    public ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(OidcApplication application, CancellationToken cancellationToken) =>
        new(OidcJson.Properties(application.Properties));

    public ValueTask<ImmutableArray<string>> GetRedirectUrisAsync(OidcApplication application, CancellationToken cancellationToken) =>
        new(OidcJson.Strings(application.RedirectUris));

    public ValueTask<ImmutableArray<string>> GetRequirementsAsync(OidcApplication application, CancellationToken cancellationToken) =>
        new(OidcJson.Strings(application.Requirements));

    public ValueTask<ImmutableDictionary<string, string>> GetSettingsAsync(OidcApplication application, CancellationToken cancellationToken) =>
        new(OidcJson.Settings(application.Settings));

    public ValueTask<OidcApplication> InstantiateAsync(CancellationToken cancellationToken) => new(new OidcApplication());

    public async IAsyncEnumerable<OidcApplication> ListAsync(int? count, int? offset, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IQueryable<OidcApplication> query = Applications.OrderBy(a => a.Id);
        if (offset.HasValue)
            query = query.Skip(offset.Value);
        if (count.HasValue)
            query = query.Take(count.Value);
        await foreach (var application in query.AsAsyncEnumerable().WithCancellation(cancellationToken))
            yield return application;
    }

    public IAsyncEnumerable<TResult> ListAsync<TState, TResult>(Func<IQueryable<OidcApplication>, TState, IQueryable<TResult>> query, TState state, CancellationToken cancellationToken) =>
        query(Applications.AsQueryable(), state).AsAsyncEnumerable();

    public ValueTask SetApplicationTypeAsync(OidcApplication application, string? type, CancellationToken cancellationToken) => Set(() => application.ApplicationType = type);

    public ValueTask SetClientIdAsync(OidcApplication application, string? identifier, CancellationToken cancellationToken) => Set(() => application.ClientId = identifier);

    public ValueTask SetClientSecretAsync(OidcApplication application, string? secret, CancellationToken cancellationToken) => Set(() => application.ClientSecret = secret);

    public ValueTask SetClientTypeAsync(OidcApplication application, string? type, CancellationToken cancellationToken) => Set(() => application.ClientType = type);

    public ValueTask SetConsentTypeAsync(OidcApplication application, string? type, CancellationToken cancellationToken) => Set(() => application.ConsentType = type);

    public ValueTask SetDisplayNameAsync(OidcApplication application, string? name, CancellationToken cancellationToken) => Set(() => application.DisplayName = name);

    public ValueTask SetDisplayNamesAsync(OidcApplication application, ImmutableDictionary<CultureInfo, string> names, CancellationToken cancellationToken) =>
        Set(() => application.DisplayNames = OidcJson.DisplayNames(names));

    public ValueTask SetJsonWebKeySetAsync(OidcApplication application, JsonWebKeySet? set, CancellationToken cancellationToken) =>
        Set(() => application.JsonWebKeySet = OidcJson.KeySet(set));

    public ValueTask SetPermissionsAsync(OidcApplication application, ImmutableArray<string> permissions, CancellationToken cancellationToken) =>
        Set(() => application.Permissions = OidcJson.Strings(permissions));

    public ValueTask SetPostLogoutRedirectUrisAsync(OidcApplication application, ImmutableArray<string> uris, CancellationToken cancellationToken) =>
        Set(() => application.PostLogoutRedirectUris = OidcJson.Strings(uris));

    public ValueTask SetPropertiesAsync(OidcApplication application, ImmutableDictionary<string, JsonElement> properties, CancellationToken cancellationToken) =>
        Set(() => application.Properties = OidcJson.Properties(properties));

    public ValueTask SetRedirectUrisAsync(OidcApplication application, ImmutableArray<string> uris, CancellationToken cancellationToken) =>
        Set(() => application.RedirectUris = OidcJson.Strings(uris));

    public ValueTask SetRequirementsAsync(OidcApplication application, ImmutableArray<string> requirements, CancellationToken cancellationToken) =>
        Set(() => application.Requirements = OidcJson.Strings(requirements));

    public ValueTask SetSettingsAsync(OidcApplication application, ImmutableDictionary<string, string> settings, CancellationToken cancellationToken) =>
        Set(() => application.Settings = OidcJson.Settings(settings));

    public async ValueTask UpdateAsync(OidcApplication application, CancellationToken cancellationToken)
    {
        if (db.Entry(application).State == EntityState.Detached)
            Applications.Attach(application);
        application.ConcurrencyToken = Guid.NewGuid().ToString();
        Applications.Update(application);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new OpenIddictExceptions.ConcurrencyException("The application was changed by another request.", ex);
        }
    }

    private static ValueTask Set(Action set)
    {
        set();
        return ValueTask.CompletedTask;
    }
}

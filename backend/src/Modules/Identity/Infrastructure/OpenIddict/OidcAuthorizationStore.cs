using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;

namespace Farm360.Identity.Infrastructure.OpenIddict;

/// <summary>Authorizations, in the current tenant's database (or the catalog for platform principals).</summary>
internal sealed class OidcAuthorizationStore(OidcStoreContext stores) : IOpenIddictAuthorizationStore<OidcAuthorization>
{
    private DbContext Db => stores.Current;

    private IQueryable<OidcAuthorization> Query => Db.Set<OidcAuthorization>();

    public async ValueTask<long> CountAsync(CancellationToken cancellationToken) => await Query.LongCountAsync(cancellationToken);

    public async ValueTask<long> CountAsync<TResult>(Func<IQueryable<OidcAuthorization>, IQueryable<TResult>> query, CancellationToken cancellationToken) =>
        await query(Query).LongCountAsync(cancellationToken);

    public async ValueTask CreateAsync(OidcAuthorization authorization, CancellationToken cancellationToken)
    {
        var db = Db;
        if (authorization.Id == Guid.Empty)
            authorization.Id = Guid.CreateVersion7();
        db.Add(authorization);
        await db.SaveChangesAsync(cancellationToken);
        stores.Track(authorization, db);
    }

    public async ValueTask DeleteAsync(OidcAuthorization authorization, CancellationToken cancellationToken)
    {
        var db = stores.OriginOf(authorization);
        var id = authorization.Id.ToString();
        await db.Set<OidcToken>().Where(t => t.AuthorizationId == id).ExecuteDeleteAsync(cancellationToken);
        db.Remove(authorization);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async IAsyncEnumerable<OidcAuthorization> FindAsync(
        string? subject, string? client, string? status, string? type, ImmutableArray<string>? scopes,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var db = Db;
        var query = db.Set<OidcAuthorization>().AsQueryable();
        if (subject is not null)
            query = query.Where(a => a.Subject == subject);
        if (client is not null)
            query = query.Where(a => a.ApplicationId == client);
        if (status is not null)
            query = query.Where(a => a.Status == status);
        if (type is not null)
            query = query.Where(a => a.Type == type);
        foreach (var authorization in await query.ToListAsync(cancellationToken))
        {
            if (scopes is { } required && !required.All(OidcJson.Strings(authorization.Scopes).Contains))
                continue;
            yield return stores.Track(authorization, db);
        }
    }

    public IAsyncEnumerable<OidcAuthorization> FindByApplicationIdAsync(string identifier, CancellationToken cancellationToken) =>
        Track(Query.Where(a => a.ApplicationId == identifier), cancellationToken);

    public async ValueTask<OidcAuthorization?> FindByIdAsync(string identifier, CancellationToken cancellationToken)
    {
        var db = Db;
        var id = OidcJson.ParseId(identifier);
        var authorization = await db.Set<OidcAuthorization>().SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        return authorization is null ? null : stores.Track(authorization, db);
    }

    public IAsyncEnumerable<OidcAuthorization> FindBySubjectAsync(string subject, CancellationToken cancellationToken) =>
        Track(Query.Where(a => a.Subject == subject), cancellationToken);

    public ValueTask<string?> GetApplicationIdAsync(OidcAuthorization authorization, CancellationToken cancellationToken) => new(authorization.ApplicationId);

    public async ValueTask<TResult?> GetAsync<TState, TResult>(Func<IQueryable<OidcAuthorization>, TState, IQueryable<TResult>> query, TState state, CancellationToken cancellationToken) =>
        await query(Query, state).FirstOrDefaultAsync(cancellationToken);

    public ValueTask<DateTimeOffset?> GetCreationDateAsync(OidcAuthorization authorization, CancellationToken cancellationToken) => new(OidcJson.Offset(authorization.CreationDate));

    public ValueTask<string?> GetIdAsync(OidcAuthorization authorization, CancellationToken cancellationToken) => new(authorization.Id.ToString());

    public ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(OidcAuthorization authorization, CancellationToken cancellationToken) =>
        new(OidcJson.Properties(authorization.Properties));

    public ValueTask<ImmutableArray<string>> GetScopesAsync(OidcAuthorization authorization, CancellationToken cancellationToken) => new(OidcJson.Strings(authorization.Scopes));

    public ValueTask<string?> GetStatusAsync(OidcAuthorization authorization, CancellationToken cancellationToken) => new(authorization.Status);

    public ValueTask<string?> GetSubjectAsync(OidcAuthorization authorization, CancellationToken cancellationToken) => new(authorization.Subject);

    public ValueTask<string?> GetTypeAsync(OidcAuthorization authorization, CancellationToken cancellationToken) => new(authorization.Type);

    public ValueTask<OidcAuthorization> InstantiateAsync(CancellationToken cancellationToken) => new(new OidcAuthorization());

    public IAsyncEnumerable<OidcAuthorization> ListAsync(int? count, int? offset, CancellationToken cancellationToken)
    {
        var query = Query.OrderBy(a => a.Id).AsQueryable();
        if (offset.HasValue)
            query = query.Skip(offset.Value);
        if (count.HasValue)
            query = query.Take(count.Value);
        return Track(query, cancellationToken);
    }

    public IAsyncEnumerable<TResult> ListAsync<TState, TResult>(Func<IQueryable<OidcAuthorization>, TState, IQueryable<TResult>> query, TState state, CancellationToken cancellationToken) =>
        query(Query, state).AsAsyncEnumerable();

    public async ValueTask<long> PruneAsync(DateTimeOffset threshold, CancellationToken cancellationToken)
    {
        var db = Db;
        var before = threshold.UtcDateTime;
        // Authorizations that are no longer valid, or ad-hoc ones without valid tokens, created before the threshold.
        var stale = await db.Set<OidcAuthorization>()
            .Where(a => a.CreationDate < before)
            .Where(a => a.Status != OpenIddictConstants.Statuses.Valid
                || (a.Type == OpenIddictConstants.AuthorizationTypes.AdHoc
                    && !db.Set<OidcToken>().Any(t => t.AuthorizationId == a.Id.ToString() && t.Status == OpenIddictConstants.Statuses.Valid)))
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);
        var ids = stale.Select(i => i.ToString()).ToList();
        await db.Set<OidcToken>().Where(t => t.AuthorizationId != null && ids.Contains(t.AuthorizationId)).ExecuteDeleteAsync(cancellationToken);
        return await db.Set<OidcAuthorization>().Where(a => stale.Contains(a.Id)).ExecuteDeleteAsync(cancellationToken);
    }

    public async ValueTask<long> RevokeAsync(string? subject, string? client, string? status, string? type, CancellationToken cancellationToken)
    {
        var query = Query;
        if (subject is not null)
            query = query.Where(a => a.Subject == subject);
        if (client is not null)
            query = query.Where(a => a.ApplicationId == client);
        if (status is not null)
            query = query.Where(a => a.Status == status);
        if (type is not null)
            query = query.Where(a => a.Type == type);
        return await Revoke(query, cancellationToken);
    }

    public ValueTask<long> RevokeByApplicationIdAsync(string identifier, CancellationToken cancellationToken) =>
        Revoke(Query.Where(a => a.ApplicationId == identifier), cancellationToken);

    public ValueTask<long> RevokeBySubjectAsync(string subject, CancellationToken cancellationToken) =>
        Revoke(Query.Where(a => a.Subject == subject), cancellationToken);

    public ValueTask SetApplicationIdAsync(OidcAuthorization authorization, string? identifier, CancellationToken cancellationToken) => Set(() => authorization.ApplicationId = identifier);

    public ValueTask SetCreationDateAsync(OidcAuthorization authorization, DateTimeOffset? date, CancellationToken cancellationToken) => Set(() => authorization.CreationDate = OidcJson.Utc(date));

    public ValueTask SetPropertiesAsync(OidcAuthorization authorization, ImmutableDictionary<string, JsonElement> properties, CancellationToken cancellationToken) =>
        Set(() => authorization.Properties = OidcJson.Properties(properties));

    public ValueTask SetScopesAsync(OidcAuthorization authorization, ImmutableArray<string> scopes, CancellationToken cancellationToken) => Set(() => authorization.Scopes = OidcJson.Strings(scopes));

    public ValueTask SetStatusAsync(OidcAuthorization authorization, string? status, CancellationToken cancellationToken) => Set(() => authorization.Status = status);

    public ValueTask SetSubjectAsync(OidcAuthorization authorization, string? subject, CancellationToken cancellationToken) => Set(() => authorization.Subject = subject);

    public ValueTask SetTypeAsync(OidcAuthorization authorization, string? type, CancellationToken cancellationToken) => Set(() => authorization.Type = type);

    public async ValueTask UpdateAsync(OidcAuthorization authorization, CancellationToken cancellationToken)
    {
        var db = stores.OriginOf(authorization);
        if (db.Entry(authorization).State == EntityState.Detached)
            db.Attach(authorization);
        authorization.ConcurrencyToken = Guid.NewGuid().ToString();
        db.Update(authorization);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new OpenIddictExceptions.ConcurrencyException("The authorization was changed by another request.", ex);
        }
    }

    private async IAsyncEnumerable<OidcAuthorization> Track(IQueryable<OidcAuthorization> query, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var db = Db;
        await foreach (var authorization in query.AsAsyncEnumerable().WithCancellation(cancellationToken))
            yield return stores.Track(authorization, db);
    }

    private static async ValueTask<long> Revoke(IQueryable<OidcAuthorization> query, CancellationToken cancellationToken) =>
        await query.ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, OpenIddictConstants.Statuses.Revoked), cancellationToken);

    private static ValueTask Set(Action set)
    {
        set();
        return ValueTask.CompletedTask;
    }
}

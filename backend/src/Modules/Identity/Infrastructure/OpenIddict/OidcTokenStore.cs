using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;

namespace Farm360.Identity.Infrastructure.OpenIddict;

/// <summary>
/// Token entries (refresh tokens are stored hashed by OpenIddict), in the current tenant's database
/// (or the catalog for platform principals). Revoking a tenant's tokens never touches another tenant's database.
/// </summary>
internal sealed class OidcTokenStore(OidcStoreContext stores) : IOpenIddictTokenStore<OidcToken>
{
    private DbContext Db => stores.Current;

    private IQueryable<OidcToken> Query => Db.Set<OidcToken>();

    public async ValueTask<long> CountAsync(CancellationToken cancellationToken) => await Query.LongCountAsync(cancellationToken);

    public async ValueTask<long> CountAsync<TResult>(Func<IQueryable<OidcToken>, IQueryable<TResult>> query, CancellationToken cancellationToken) =>
        await query(Query).LongCountAsync(cancellationToken);

    public async ValueTask CreateAsync(OidcToken token, CancellationToken cancellationToken)
    {
        var db = Db;
        if (token.Id == Guid.Empty)
            token.Id = Guid.CreateVersion7();
        db.Add(token);
        await db.SaveChangesAsync(cancellationToken);
        stores.Track(token, db);
    }

    public async ValueTask DeleteAsync(OidcToken token, CancellationToken cancellationToken)
    {
        var db = stores.OriginOf(token);
        db.Remove(token);
        await db.SaveChangesAsync(cancellationToken);
    }

    public IAsyncEnumerable<OidcToken> FindAsync(string? subject, string? client, string? status, string? type, CancellationToken cancellationToken) =>
        Track(Filter(Query, subject, client, status, type), cancellationToken);

    public IAsyncEnumerable<OidcToken> FindByApplicationIdAsync(string identifier, CancellationToken cancellationToken) =>
        Track(Query.Where(t => t.ApplicationId == identifier), cancellationToken);

    public IAsyncEnumerable<OidcToken> FindByAuthorizationIdAsync(string identifier, CancellationToken cancellationToken) =>
        Track(Query.Where(t => t.AuthorizationId == identifier), cancellationToken);

    public async ValueTask<OidcToken?> FindByIdAsync(string identifier, CancellationToken cancellationToken)
    {
        var db = Db;
        var id = OidcJson.ParseId(identifier);
        var token = await db.Set<OidcToken>().SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        return token is null ? null : stores.Track(token, db);
    }

    public async ValueTask<OidcToken?> FindByReferenceIdAsync(string identifier, CancellationToken cancellationToken)
    {
        var db = Db;
        var token = await db.Set<OidcToken>().SingleOrDefaultAsync(t => t.ReferenceId == identifier, cancellationToken);
        return token is null ? null : stores.Track(token, db);
    }

    public IAsyncEnumerable<OidcToken> FindBySubjectAsync(string subject, CancellationToken cancellationToken) =>
        Track(Query.Where(t => t.Subject == subject), cancellationToken);

    public ValueTask<string?> GetApplicationIdAsync(OidcToken token, CancellationToken cancellationToken) => new(token.ApplicationId);

    public async ValueTask<TResult?> GetAsync<TState, TResult>(Func<IQueryable<OidcToken>, TState, IQueryable<TResult>> query, TState state, CancellationToken cancellationToken) =>
        await query(Query, state).FirstOrDefaultAsync(cancellationToken);

    public ValueTask<string?> GetAuthorizationIdAsync(OidcToken token, CancellationToken cancellationToken) => new(token.AuthorizationId);

    public ValueTask<DateTimeOffset?> GetCreationDateAsync(OidcToken token, CancellationToken cancellationToken) => new(OidcJson.Offset(token.CreationDate));

    public ValueTask<DateTimeOffset?> GetExpirationDateAsync(OidcToken token, CancellationToken cancellationToken) => new(OidcJson.Offset(token.ExpirationDate));

    public ValueTask<string?> GetIdAsync(OidcToken token, CancellationToken cancellationToken) => new(token.Id.ToString());

    public ValueTask<string?> GetPayloadAsync(OidcToken token, CancellationToken cancellationToken) => new(token.Payload);

    public ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(OidcToken token, CancellationToken cancellationToken) => new(OidcJson.Properties(token.Properties));

    public ValueTask<DateTimeOffset?> GetRedemptionDateAsync(OidcToken token, CancellationToken cancellationToken) => new(OidcJson.Offset(token.RedemptionDate));

    public ValueTask<string?> GetReferenceIdAsync(OidcToken token, CancellationToken cancellationToken) => new(token.ReferenceId);

    public ValueTask<string?> GetStatusAsync(OidcToken token, CancellationToken cancellationToken) => new(token.Status);

    public ValueTask<string?> GetSubjectAsync(OidcToken token, CancellationToken cancellationToken) => new(token.Subject);

    public ValueTask<string?> GetTypeAsync(OidcToken token, CancellationToken cancellationToken) => new(token.Type);

    public ValueTask<OidcToken> InstantiateAsync(CancellationToken cancellationToken) => new(new OidcToken());

    public IAsyncEnumerable<OidcToken> ListAsync(int? count, int? offset, CancellationToken cancellationToken)
    {
        var query = Query.OrderBy(t => t.Id).AsQueryable();
        if (offset.HasValue)
            query = query.Skip(offset.Value);
        if (count.HasValue)
            query = query.Take(count.Value);
        return Track(query, cancellationToken);
    }

    public IAsyncEnumerable<TResult> ListAsync<TState, TResult>(Func<IQueryable<OidcToken>, TState, IQueryable<TResult>> query, TState state, CancellationToken cancellationToken) =>
        query(Query, state).AsAsyncEnumerable();

    public async ValueTask<long> PruneAsync(DateTimeOffset threshold, CancellationToken cancellationToken)
    {
        var before = threshold.UtcDateTime;
        return await Query
            .Where(t => t.CreationDate < before)
            .Where(t => (t.Status != OpenIddictConstants.Statuses.Inactive && t.Status != OpenIddictConstants.Statuses.Valid)
                || t.ExpirationDate < before)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public ValueTask<long> RevokeAsync(string? subject, string? client, string? status, string? type, CancellationToken cancellationToken) =>
        Revoke(Filter(Query, subject, client, status, type), cancellationToken);

    public ValueTask<long> RevokeByApplicationIdAsync(string identifier, CancellationToken cancellationToken) =>
        Revoke(Query.Where(t => t.ApplicationId == identifier), cancellationToken);

    public ValueTask<long> RevokeByAuthorizationIdAsync(string identifier, CancellationToken cancellationToken) =>
        Revoke(Query.Where(t => t.AuthorizationId == identifier), cancellationToken);

    public ValueTask<long> RevokeBySubjectAsync(string subject, CancellationToken cancellationToken) =>
        Revoke(Query.Where(t => t.Subject == subject), cancellationToken);

    public ValueTask SetApplicationIdAsync(OidcToken token, string? identifier, CancellationToken cancellationToken) => Set(() => token.ApplicationId = identifier);

    public ValueTask SetAuthorizationIdAsync(OidcToken token, string? identifier, CancellationToken cancellationToken) => Set(() => token.AuthorizationId = identifier);

    public ValueTask SetCreationDateAsync(OidcToken token, DateTimeOffset? date, CancellationToken cancellationToken) => Set(() => token.CreationDate = OidcJson.Utc(date));

    public ValueTask SetExpirationDateAsync(OidcToken token, DateTimeOffset? date, CancellationToken cancellationToken) => Set(() => token.ExpirationDate = OidcJson.Utc(date));

    public ValueTask SetPayloadAsync(OidcToken token, string? payload, CancellationToken cancellationToken) => Set(() => token.Payload = payload);

    public ValueTask SetPropertiesAsync(OidcToken token, ImmutableDictionary<string, JsonElement> properties, CancellationToken cancellationToken) =>
        Set(() => token.Properties = OidcJson.Properties(properties));

    public ValueTask SetRedemptionDateAsync(OidcToken token, DateTimeOffset? date, CancellationToken cancellationToken) => Set(() => token.RedemptionDate = OidcJson.Utc(date));

    public ValueTask SetReferenceIdAsync(OidcToken token, string? identifier, CancellationToken cancellationToken) => Set(() => token.ReferenceId = identifier);

    public ValueTask SetStatusAsync(OidcToken token, string? status, CancellationToken cancellationToken) => Set(() => token.Status = status);

    public ValueTask SetSubjectAsync(OidcToken token, string? subject, CancellationToken cancellationToken) => Set(() => token.Subject = subject);

    public ValueTask SetTypeAsync(OidcToken token, string? type, CancellationToken cancellationToken) => Set(() => token.Type = type);

    public async ValueTask UpdateAsync(OidcToken token, CancellationToken cancellationToken)
    {
        var db = stores.OriginOf(token);
        if (db.Entry(token).State == EntityState.Detached)
            db.Attach(token);
        token.ConcurrencyToken = Guid.NewGuid().ToString();
        db.Update(token);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new OpenIddictExceptions.ConcurrencyException("The token was changed by another request.", ex);
        }
    }

    private static IQueryable<OidcToken> Filter(IQueryable<OidcToken> query, string? subject, string? client, string? status, string? type)
    {
        if (subject is not null)
            query = query.Where(t => t.Subject == subject);
        if (client is not null)
            query = query.Where(t => t.ApplicationId == client);
        if (status is not null)
            query = query.Where(t => t.Status == status);
        if (type is not null)
            query = query.Where(t => t.Type == type);
        return query;
    }

    private async IAsyncEnumerable<OidcToken> Track(IQueryable<OidcToken> query, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var db = Db;
        await foreach (var token in query.AsAsyncEnumerable().WithCancellation(cancellationToken))
            yield return stores.Track(token, db);
    }

    private static async ValueTask<long> Revoke(IQueryable<OidcToken> query, CancellationToken cancellationToken) =>
        await query.ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, OpenIddictConstants.Statuses.Revoked), cancellationToken);

    private static ValueTask Set(Action set)
    {
        set();
        return ValueTask.CompletedTask;
    }
}

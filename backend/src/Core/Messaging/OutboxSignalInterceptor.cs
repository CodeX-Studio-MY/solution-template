using System.Data.Common;
using System.Runtime.CompilerServices;
using Saaskit.Core.Data;
using Saaskit.Core.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Saaskit.Core.Messaging;

/// <summary>
/// After a commit that wrote outbox rows, tells the relay "tenant X has pending rows in this schema". With an explicit
/// transaction the signal waits for the commit, because the relay can't see uncommitted rows. A lost signal is harmless:
/// the relay's slow sweep finds the rows.
/// </summary>
internal sealed partial class OutboxSignalInterceptor(IOutboxSignal signal, ITenantContext tenant, ILogger<OutboxSignalInterceptor> logger)
    : ISaveChangesInterceptor, IDbTransactionInterceptor
{
    private readonly ConditionalWeakTable<DbContext, PendingSignal> _pending = [];

    public ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Remember(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Remember(eventData.Context);
        return result;
    }

    public async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        // No explicit transaction: SaveChanges committed its own, so the rows are visible now.
        if (eventData.Context is { } context && context.Database.CurrentTransaction is null)
            await SendAsync(context, cancellationToken);
        return result;
    }

    public int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (eventData.Context is { } context && context.Database.CurrentTransaction is null)
            SendAsync(context, CancellationToken.None).GetAwaiter().GetResult();
        return result;
    }

    public async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
            await SendAsync(context, cancellationToken);
    }

    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
    {
        if (eventData.Context is { } context)
            SendAsync(context, CancellationToken.None).GetAwaiter().GetResult();
    }

    public Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
            _pending.Remove(context);
        return Task.CompletedTask;
    }

    private void Remember(DbContext? context)
    {
        if (context is ModuleDbContext && context.ChangeTracker.Entries<OutboxMessage>().Any(e => e.State == EntityState.Added))
            _pending.GetOrCreateValue(context).Wanted = true;
    }

    private async Task SendAsync(DbContext context, CancellationToken cancellationToken)
    {
        if (context is not ModuleDbContext module || !_pending.TryGetValue(context, out var pending) || !pending.Wanted || !tenant.HasTenant)
            return;
        pending.Wanted = false;
        try
        {
            await signal.SignalAsync(tenant.Tenant.Id, module.Schema, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSignalFailed(logger, ex, module.Schema);   // the sweep will find the rows
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not signal the outbox relay for {Schema}; the sweep will pick the rows up")]
    private static partial void LogSignalFailed(ILogger logger, Exception exception, string schema);

    private sealed class PendingSignal
    {
        public bool Wanted { get; set; }
    }
}

/// <summary>The default signal does nothing (hosts without a relay). Core.Hosting replaces it with the Redis stream.</summary>
internal sealed class NoOutboxSignal : IOutboxSignal
{
    public Task SignalAsync(Guid tenantId, string schema, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

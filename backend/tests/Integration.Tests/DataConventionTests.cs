using Farm360.Core.Data;
using Farm360.Core.Errors;
using Farm360.Core.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Farm360.Integration.Tests;

public sealed class DataConventionTests(Databases databases)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task Sequence_numbers_are_unique_under_concurrency(string provider)
    {
        await using var host = await CreateAsync(provider);
        const int requests = 40;

        var numbers = await Task.WhenAll(Enumerable.Range(0, requests).Select(async _ =>
        {
            var (scope, db) = host.NewContext();
            await using (scope)
            await using (db)
            {
                await using var transaction = await db.Database.BeginTransactionAsync(Ct);
                var next = await host.Get<ISequenceService>().NextAsync(db, "fixture-number", Ct);
                await transaction.CommitAsync(Ct);
                return next;
            }
        }));

        numbers.Order().ShouldBe(Enumerable.Range(1, requests).Select(i => (long)i));
    }

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task Audit_fields_keys_money_and_utc_are_applied(string provider)
    {
        await using var host = await CreateAsync(provider);
        Guid id;
        var (scope, db) = host.NewContext();
        await using (scope)
        await using (db)
        {
            var item = new TestItem("A-1", new Money(1234.5678m, "MYR"));
            db.Items.Add(item);
            await db.SaveChangesAsync(Ct);
            id = item.Id;
        }

        id.ShouldNotBe(Guid.Empty);
        (scope, db) = host.NewContext();
        await using (scope)
        await using (db)
        {
            var item = await db.Items.SingleAsync(i => i.Id == id, Ct);
            item.CreatedBy.ShouldBe("user-1");
            item.CreatedOn.ShouldBe(host.Time.GetUtcNow().UtcDateTime);
            item.CreatedOn.Kind.ShouldBe(DateTimeKind.Utc);
            item.Price.ShouldBe(new Money(1234.5678m, "MYR"));
        }
    }

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task A_stale_ETag_is_a_conflict_and_a_missing_one_is_required(string provider)
    {
        await using var host = await CreateAsync(provider);
        Guid id;
        string staleETag;
        var (scope, db) = host.NewContext();
        await using (scope)
        await using (db)
        {
            var item = new TestItem("E-1", new Money(10m, "MYR"));
            db.Items.Add(item);
            await db.SaveChangesAsync(Ct);
            id = item.Id;
            staleETag = db.GetETag(item);
            staleETag.ShouldStartWith("\"");
        }

        // Someone else updates the item.
        (scope, db) = host.NewContext();
        await using (scope)
        await using (db)
        {
            var item = await db.Items.SingleAsync(i => i.Id == id, Ct);
            db.ApplyIfMatch(item, staleETag);
            item.Reprice(new Money(11m, "MYR"));
            await db.SaveChangesAsync(Ct);
            db.GetETag(item).ShouldNotBe(staleETag);
        }

        // An update with the old ETag fails (412); one without If-Match is refused (428).
        (scope, db) = host.NewContext();
        await using (scope)
        await using (db)
        {
            var item = await db.Items.SingleAsync(i => i.Id == id, Ct);
            Should.Throw<PreconditionRequiredException>(() => db.ApplyIfMatch(item, null));
            db.ApplyIfMatch(item, staleETag);
            item.Reprice(new Money(12m, "MYR"));
            await Should.ThrowAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync(Ct));
        }
    }

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task Soft_deleted_rows_are_hidden_and_free_their_unique_value(string provider)
    {
        await using var host = await CreateAsync(provider);
        var (scope, db) = host.NewContext();
        await using (scope)
        await using (db)
        {
            var item = new TestItem("S-1", new Money(1m, "MYR"));
            db.Items.Add(item);
            await db.SaveChangesAsync(Ct);
            db.Items.Remove(item);
            await db.SaveChangesAsync(Ct);
        }

        (scope, db) = host.NewContext();
        await using (scope)
        await using (db)
        {
            (await db.Items.CountAsync(Ct)).ShouldBe(0);
            (await db.Items.IgnoreQueryFilters([QueryFilters.SoftDelete]).CountAsync(Ct)).ShouldBe(1);

            db.Items.Add(new TestItem("S-1", new Money(2m, "MYR")));
            await db.SaveChangesAsync(Ct);

            db.Items.Add(new TestItem("S-1", new Money(3m, "MYR")));
            await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        }
    }

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task Suspended_tenants_cannot_write(string provider)
    {
        var connectionString = databases.NewDatabase(provider);
        await using (var setup = new TestModuleHost(provider, connectionString))
            await setup.CreateDatabaseAsync();

        await using var host = new TestModuleHost(provider, connectionString, TenantStatus.Suspended);
        var (scope, db) = host.NewContext();
        await using (scope)
        await using (db)
        {
            (await db.Items.CountAsync(Ct)).ShouldBe(0);
            db.Items.Add(new TestItem("X-1", new Money(1m, "MYR")));
            var ex = await Should.ThrowAsync<ForbiddenException>(() => db.SaveChangesAsync(Ct));
            ex.Error.ShouldBe(PlatformErrors.TenantReadOnly);
        }
    }

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task The_distributed_lock_is_exclusive_until_the_transaction_ends(string provider)
    {
        await using var host = await CreateAsync(provider);
        var locks = host.Get<IDistributedLock>();

        var (scope1, first) = host.NewContext();
        var (scope2, second) = host.NewContext();
        await using (scope1)
        await using (scope2)
        await using (first)
        await using (second)
        {
            var held = await first.Database.BeginTransactionAsync(Ct);
            await locks.AcquireAsync(first, "job:reminders", Ct);

            await using (var waiting = await second.Database.BeginTransactionAsync(Ct))
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await Should.ThrowAsync<Exception>(() => locks.AcquireAsync(second, "job:reminders", timeout.Token));
            }

            await held.CommitAsync(Ct);
            await held.DisposeAsync();

            await using var next = await second.Database.BeginTransactionAsync(Ct);
            await locks.AcquireAsync(second, "job:reminders", Ct);
        }
    }

    private async Task<TestModuleHost> CreateAsync(string provider)
    {
        var host = new TestModuleHost(provider, databases.NewDatabase(provider));
        await host.CreateDatabaseAsync();
        return host;
    }
}

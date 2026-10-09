# Backup and restore

## What is backed up, and where it lives

| Data | Where | How it is protected |
|---|---|---|
| Each tenant's data | One database per tenant, on a server pool | The cloud's automated backups with point-in-time restore (Azure SQL / Azure Database for PostgreSQL, or Amazon RDS) |
| The catalog | One database | The same, with the same retention |
| Files | Object storage, keys start with `{tenantId}/` | Versioning and soft delete on the container or bucket |
| Valkey (sessions, caches, rate limits) | Not backed up | Disposable: a loss signs people out and refills caches. The Data Protection key ring in it is wrapped by `DataProtection:KeyEncryptionKey` (keep that in the secret store) |
| RabbitMQ | Quorum queues, replicated | Messages in flight are protected by the transactional outbox: anything not yet published is still in the module's database |

**Targets** (decide them with the business; these are the starting point the template assumes):

| | RPO | RTO |
|---|---|---|
| One tenant database | 5 minutes (point-in-time restore) | 1 hour |
| The catalog | 5 minutes | 30 minutes (nothing works without it) |
| Files | 0 (versioned) | 1 hour |

## Restoring one tenant, without touching anyone else

Because every tenant has its own database, a restore never involves the other tenants.

1. **Decide the time** to restore to, and tell the customer what will be lost (everything after it).
2. **Suspend the tenant** so nothing writes during the restore: `POST /api/tenancy/v1/operator/tenants/{id}/suspend`
   (reads still work; writes get `platform.tenant-read-only`).
3. **Restore the database** to a *new* database next to the old one, with your cloud's point-in-time restore
   (`<cloud restore command: source database, target name, time>`). Do not overwrite the live database yet.
4. **Check it**: run the Migrator against the restored copy to make sure the schema matches the code that is deployed
   (`Migrator --tenant {slug}` after pointing the tenant's placement at it in step 5, or against a scratch catalog).
5. **Switch the tenant to it**: set the tenant's `DatabaseName` in the catalog to the restored database
   (`catalog.Tenants`, placement columns), and wait for the tenant cache to expire (`Database:TenantCacheSeconds`) or restart the hosts.
6. **Resume the tenant**: `POST /api/tenancy/v1/operator/tenants/{id}/resume`.
7. **Verify**: sign in as the tenant's admin, open the latest records, check `Migrator` reports the tenant migrated.
8. **Keep the old database** for the retention period you agreed, then drop it.

Files are not part of the database: restore deleted objects from the container's versions if needed. A file row without its object
(or the reverse) is harmless to the app and is reconciled by the file service.

## Restoring the catalog

The catalog holds the tenant list, placement, entitlements, settings, flags and the job store. After a restore, **restart the hosts**
(they cache the tenant list for seconds, and the Scheduler re-reads its jobs). Operators' sessions and the Quartz cluster state
repair themselves.

## Testing the procedure

Do this on a schedule, not for the first time during an incident: restore a production backup of a throwaway tenant into a scratch
server pool every quarter, run `Migrator --tenant`, and time it against the RTO.

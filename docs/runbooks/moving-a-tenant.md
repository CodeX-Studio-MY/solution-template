# Moving a tenant, and data residency

A tenant is a database plus a row in the catalog that says where it lives (**placement**: provider, server pool, database name, and an
optional secret reference). Moving a tenant means moving the database and changing that row. No code or configuration of the
services changes.

## Moving to another server or pool

Plan for a short read-only window: the length of a copy of that tenant's database.

1. **Prepare the target**: a server pool entry for it in configuration (`Database:Servers:{pool}`), reachable from the hosts and the Migrator.
2. **Suspend the tenant**: `POST /api/tenancy/v1/operator/tenants/{id}/suspend` (people can still sign in and read).
3. **Copy the database** with the cloud's tools: backup and restore, or the provider's replication/export (`<copy command: source, target>`).
   Verify row counts for the largest tables.
4. **Create the target database's access** (login/role) the same way a new tenant's is created.
5. **Run the Migrator against the copy** to prove the schema is current: `Migrator --tenant {slug}` (after step 6, or with the placement
   pointed at the copy).
6. **Change the placement** in the catalog (`catalog.Tenants`: `Placement_ServerPool`, `Placement_DatabaseName`, and the secret reference if the
   target uses one). Within `Database:TenantCacheSeconds` every host uses the new location; restart them to be immediate.
7. **Resume**: `POST /api/tenancy/v1/operator/tenants/{id}/resume`. Verify by signing in and creating a harmless change.
8. **Keep the old database** read-only for a week, then drop it (or follow [offboarding](offboarding.md)'s deletion step).

There is no operator API for steps 3 and 6 yet; the change to a tenant's placement is a deliberate, audited manual step.

## Data residency (pinning a tenant to a region)

- Create a **server pool per region** (for example `eu`, `apac`) with servers physically in that region, and name them in `Database:Servers`.
- Onboard the tenant on the right pool: `OnboardRequest.ServerPool` (`POST /api/tenancy/v1/operator/tenants`, field `serverPool`).
  Its database, backups and point-in-time restore then stay in that region.
- **Files**: use a storage account/bucket in the same region (`Storage` settings are per deployment today; for residency by tenant, deploy a
  regional stack, or add a per-pool file provider behind `IFileStorage`). 
- **Messaging, caches and logs** carry tenant data in transit and in short-lived caches: keep the regional stack's broker and Valkey in the
  region, and make sure logs and traces (which carry tenant IDs, never personal data) go to a regional workspace if the policy requires it.
- Record the tenant's region and the reason in the contract file, not in code.

Moving a tenant to a *different region* is a move to another pool (above) plus the same for its files and a conversation with the customer
about the downtime.

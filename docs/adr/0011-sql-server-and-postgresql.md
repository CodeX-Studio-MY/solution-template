# 0011. Support SQL Server and PostgreSQL

Status: accepted, amended 2026-10-11 and 2026-11-05 (see the end)

## Context

Customers' clouds and policies differ; some products need SQL Server, others PostgreSQL, and the choice must not change the code.

## Decision

Two supported providers, chosen per environment by `Database:Provider`. Business code is provider-neutral EF Core LINQ; everything that differs (sequential GUIDs vs UUIDv7, row version vs `xmin`, filtered-index SQL, native JSON, sequence counters, locks, naming) is behind `IDatabaseProvider` in `Core.Data.SqlServer` and `Core.Data.PostgreSql`. Each module has a migrations project per provider generated from the same model, and every test that touches data runs on both (Testcontainers, SQL Server 2025 and PostgreSQL 18).

## Alternative rejected

A single provider with the other 'planned', or an abstraction like a repository per provider in each module. Rejected: a second provider added late means rewriting; per-module provider code multiplies work.

## Consequences

Case sensitivity differs, so uniqueness on user-entered text uses normalised columns. A third provider is a new `Core.Data.*` package, a migrations project per module and a line in `platform.yaml`.

## Amendment (2026-10-11): PostgreSQL migrations are opt-in

Maintaining two migration sets and running every data test twice cost more than it returned while only SQL Server ships. So `platform.yaml` lists
`providers: [sqlserver]` and the seven `*.Migrations.PostgreSql` projects were removed. The provider abstraction is unchanged: `Core.Data.PostgreSql`,
the Quartz PostgreSQL scripts and the AppHost's PostgreSQL option remain, and the tests follow `PlatformInfo.DatabaseProviders` (the PostgreSQL
container starts only when PostgreSQL is listed). `scripts/add-postgresql` re-enables it: it adds the provider, restores the migrations projects
from commit `17db99a`, adds a catch-up migration per context, warns about hand-written SQL in later SQL Server migrations (those must be ported by
hand) and runs `MigrationsConsistencyTests`. Until then the Migrator refuses `Database:Provider=postgresql`.

## Amendment (2026-11-05): PostgreSQL is not supported in this release

`scripts/add-postgresql` could not be relied on, so it no longer restores anything. It restored migrations from commit `17db99a`, whose
files use an earlier product namespace and cover modules that no longer exist, so they did not compile, and the schema has changed a lot
since. Making it work means regenerating every PostgreSQL migration with `dotnet ef`, including the Scheduler's hand-written Quartz SQL,
and running every data test against PostgreSQL. That is more than this release carries.

SQL Server is the supported database. The provider abstraction (`Core.Data.PostgreSql`, the Quartz PostgreSQL scripts, the AppHost
option) stays in the tree, but there are no PostgreSQL migrations, `platform.yaml` lists `sqlserver` only, `scripts/dev-up` refuses
`--provider postgresql`, and the Migrator refuses `Database:Provider=postgresql`. `scripts/add-postgresql` only prints that PostgreSQL is
not supported. The tests still follow `PlatformInfo.DatabaseProviders`, so they run on PostgreSQL once it is listed again.

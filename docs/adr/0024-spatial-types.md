# 0024. Spatial types with NetTopologySuite, as geography in SRID 4326

Status: accepted

## Context

Modules need to store locations, for example where a farm is. Both providers support spatial types: SQL Server natively, and PostgreSQL
through the PostGIS extension. In .NET, EF Core reads and writes them as NetTopologySuite types (BSD-3-Clause). Modules must stay
provider-neutral (ADR 0011) and reference no database provider (ADR 0009).

## Decision

Spatial support is switched on once, in the provider packages. `Core.Data.SqlServer` and `Core.Data.PostgreSql` call `UseNetTopologySuite()`.
That covers every module context, the Migrator and the design-time factories. A module only references `NetTopologySuite`, for the types.

- Values are **geography in SRID 4326** (WGS 84). SQL Server maps NetTopologySuite types to `geography`. PostgreSQL uses
  `geographyAsDefault`, and a `Point` property is a `geography (point, 4326)` column.
- On PostgreSQL, a context whose model has a spatial column declares the `postgis` extension (`IDatabaseProvider.ConfigureModel`), so its
  migration creates the extension in each tenant database. Contexts without spatial columns don't declare it.
- The PostgreSQL container is `imresamu/postgis` (PostgreSQL 18 with PostGIS 3.6). It is a multi-arch build: `postgis/postgis` is
  amd64-only, and Docker refuses to pull it on Apple Silicon without a platform pin that the generated compose file can't carry.
- Spatial types never reach the wire. Requests and responses use latitude and longitude numbers. `ModuleBoundaryTests` keeps
  NetTopologySuite out of `Api` and `Contracts`.

## Alternative rejected

- **Latitude and longitude as two decimal columns.** Simpler, but there are no spatial indexes or distance queries in the database.
  Rejected, because "farms within 5 km" is a normal question.
- **Declaring PostGIS in every module context.** Rejected: every module would need a migration for it, and every PostgreSQL server would
  need PostGIS even when no module uses it.

## Consequences

A managed PostgreSQL server must allow the `postgis` extension (on Azure, the `azure.extensions` parameter). `docs/adding-a-module.md` lists
the rules for modules: longitude first, validate the range, no `Point` in DTOs.

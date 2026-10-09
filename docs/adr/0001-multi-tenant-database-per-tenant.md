# 0001. Multi-tenant from day one: one database per tenant

Status: accepted

## Context

Every product on this platform serves several customers, and a data leak between them is the failure we most need to make impossible. The system must also work with exactly one tenant, so a product can go live single-tenant and add tenants later without code changes.

## Decision

Every request resolves a tenant (host name plus the token's tenant), and the tenant decides the database connection. Each tenant has its own database; a separate catalog database lists the tenants, their placement (provider, server pool, database name), plans and entitlements. Business code never filters by tenant: isolation comes from the connection. `ITenantConnectionResolver` is the only place that turns a tenant into a connection string.

## Alternative rejected

A row-level `TenantId` column with a shared database and query filters. Rejected: one forgotten filter, raw query or background job leaks data across customers; noisy neighbours share indexes and locks; per-customer restore, residency and deletion become row surgery. A schema per tenant was also rejected: migrations and connection management are as complex as database-per-tenant with weaker isolation.

## Consequences

Many databases to migrate (the Migrator does it in parallel and resumes), a connection pool per tenant (`Database:MaxPoolSize` bounds it), and per-tenant backup, restore, move and delete become simple and boring. Cross-tenant analytics need a separate pipeline.

# 0026. Principals and tenant access in the catalog

Status: accepted

## Context

A tenant is a company, and a customer with several companies has several tenants. One person, field gateway or integration may need to reach more than one of them. Each tenant database has its own `Principal` table, so today that takes one account per tenant, and nothing lists who can reach what. ADR 0001 and ADR 0016 keep identity in tenant databases and rejected a global user table with a tenant column.

## Decision

The catalog holds a shared `Principals` table for users, devices and applications, with one detail table per type. `TenantPrincipalAccess` links a principal to a tenant, and `TenantPrincipalAccessAudit` and `TenantPrincipalLogins` are append-only histories. Outside accounts (Google, Microsoft, Apple) are in `PrincipalUserProviders`, matched by the provider's permanent subject and never by email. Refresh tokens are not stored in the login history: a login row refers to OpenIddict's authorization, which groups the rotated tokens of one login.

Business data stays in tenant databases, so ADR 0001's isolation of customer data is unchanged. This qualifies ADR 0016 for who a principal is, not for where tenant data lives.

These tables do not control sign-in yet. Tokens and roles still come from each tenant's database, and the operator console says so. Connecting them is separate work: a shared credential store, a tenant picker, provider sign-in, and migrating existing accounts.

## Alternative rejected

Keeping one principal per tenant and showing each tenant's list in the console. Rejected: one person with several companies would keep several accounts, and a gateway or integration couldn't be managed as one thing. A `TenantId` column on principals was rejected for the same reason.

## Consequences

Tenant deletion has to clean up catalog rows as well as drop the database. Until sign-in is connected there are two records of who a user is, so nothing may treat the catalog as the authority for access. The login table will grow quickly once devices are logged, so it needs a retention rule before that happens.

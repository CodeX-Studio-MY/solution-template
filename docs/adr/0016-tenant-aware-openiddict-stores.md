# 0016. Tenant-aware OpenIddict stores

Status: accepted

## Context

The token server must keep clients, authorizations and tokens per tenant while presenting one issuer.

## Decision

Identity runs ASP.NET Core Identity and OpenIddict with stores that read and write the tenant's database (users, authorizations, tokens) and the catalog (platform clients and operator realm). The issuer is one constant for every tenant; a request names its tenant (`tenant` parameter, validated against the host the Gateway forwards), and a token carries `tenant_id`, which every service checks against the host.

## Alternative rejected

A realm (a separate token server instance) per tenant, or a single global user table with a tenant column. Rejected: operational cost for the first, and the row-level isolation we ruled out in ADR 0001 for the second.

## Consequences

A token cannot be used on another tenant's host (`platform.tenant-mismatch`). Revocation uses an `access_version` per principal that services compare.

# 0018. The customization ladder

Status: accepted

## Context

Every customer wants something different, and a fork or `if (tenant == ...)` per customer is how products die.

## Decision

Seven rungs, lowest that works: a setting, a feature flag, a strategy, custom fields, an event extension, a slot, a tenant-only module. Variations are data in the catalog with a default for each, so a new tenant works with zero configuration. Business code never branches on tenant identity, which a test and a lint rule enforce. See `docs/tenant-customization.md`.

## Alternative rejected

Per-customer branches or forks; a tenant-specific code path guarded by the slug; a single-company settings table. Rejected: they multiply the maintenance and the risk.

## Consequences

Replacing another module's whole page for one tenant is not supported; if it is ever needed, it requires an ADR.

# 0007. Modules can be switched on per tenant

Status: accepted

## Context

A subscription decides which capabilities a customer can use.

## Decision

Plans in `platform.yaml` list their modules; the catalog records each tenant's entitlements. The backend enforces them (a module's endpoints return 403 `platform.module-not-entitled` and its events are not delivered to tenants without it); the frontend only hides what the tenant doesn't have, from the module manifest endpoint. Changing a plan reconciles entitlements and reaches every service within seconds.

## Alternative rejected

Feature checks inside each module's code. Rejected: forgettable, and the frontend cannot be trusted to enforce anything.

## Consequences

An add-on module is simply entitled in some plans only (`plans: [plus]` in `platform.yaml`).

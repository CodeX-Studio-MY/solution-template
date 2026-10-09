# 0009. Boundaries are enforced by tests, not by convention

Status: accepted

## Context

Rules in a document decay as soon as someone is in a hurry.

## Decision

Architecture tests fail the build when a rule is broken: module isolation and layering, only the Migrator references migrations, no provider types in business code, no hard-coded module keys, roles, flag keys or `/api/` paths, no comparisons against `TenantId` or `TenantSlug` in business modules, banned APIs (the static clock), no per-environment settings files. The frontend has the same rules as lint with tests proving they fire.

## Alternative rejected

Code review alone, or a wiki of conventions. Rejected: neither catches the 50th violation.

## Consequences

A new rule is a test; a rule that cannot be tested is questioned. Tests are the specification's most precise form.

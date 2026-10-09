# 0002. A module is a vertical slice that owns its data

Status: accepted

## Context

A shared database model couples teams and releases; a change to one capability's tables can break another. We want capabilities that can be extracted, scaled and owned separately.

## Decision

Each business module has one backend part and one frontend part with the same module key. A module owns its schema and `DbContext` inside the tenant database, with `Domain`, `Application`, `Infrastructure` and `Api` folders and a dependency rule between them. No module reads another's tables, and there are no cross-module foreign keys. Modules talk through APIs and versioned integration events in `Contracts`. Architecture tests enforce all of it.

## Alternative rejected

One shared `DbContext` holding every module's entities, used by every service (the pattern that caused the problems this template exists to avoid). Rejected: it makes every deployment depend on one model and one migration history.

## Consequences

Cross-module reports go through events or APIs, not joins. Each module has its own migrations per database provider.

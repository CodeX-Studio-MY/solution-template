# 0004. A shared kernel provides the plumbing

Status: accepted

## Context

Every host needs authentication, tenancy, data access, messaging, storage, resilience, logging and error handling, and each rule must be applied the same way everywhere.

## Decision

`Core` (with `Core.Hosting`, `Core.Catalog` and the two `Core.Data.*` provider packages) implements the plumbing once. A host calls `AddPlatform(PlatformServices.X)` and `UsePlatform()`. Modules reference `Core` and `Contracts` only; the provider packages and the catalog are referenced only by hosts and Tenancy. `Core` is packable.

## Alternative rejected

Copying the plumbing into each service, or one framework per concern chosen by each team. Rejected: rules drift, and a security fix would need N edits.

## Consequences

`Core` changes affect everything, so they run the whole test suite (`backend-core.yml`).

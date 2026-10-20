# 0023. License policy

Status: accepted

## Context

Libraries have turned commercial after teams depended on them (AutoMapper and MediatR in 2025, MassTransit v9, FluentAssertions v8, Duende).

## Decision

The template depends on no library that needs a paid commercial license unless `docs/deviations.md` records the decision. `scripts/check-licenses` enforces it: a deny-list of known commercial libraries plus an allow-list of licenses, with `docs/license-exceptions.json` for reviewed exceptions. MUI X is used in its MIT edition only. Mapping is hand-written or Mapperly.

## Alternative rejected

Reviewing licences by hand at release time. Rejected: nobody does it, and the change is silent.

## Consequences

A new dependency with an unknown license fails `scripts/check-licenses` until someone decides and records why.

# 0010. Single source of truth and `scripts/generate`

Status: accepted

## Context

The same fact typed in several places (the module list in routes, the AppHost, compose, CI and constants; API types written by hand; per-environment config) drifts.

## Decision

Each fact is defined once: structure in `platform.yaml`, runtime configuration in typed options records, API contracts in the backend code (OpenAPI), design tokens in `tokens.ts`. `scripts/generate` writes everything else (constants, migrations projects, gateway routes, the AppHost, the compose file by `aspire publish`, CI workflows, API clients, TypeScript constants, `.env.example` and docs), and CI fails if the committed files differ (`scripts/generate --check`). The compose file comes from the AppHost, not a second generator.

## Alternative rejected

Hand-maintained lists with a checklist, or templates copied per module. Rejected: they work once.

## Consequences

Adding a module is one command (`scripts/new-module`). Generated files are committed so reviews see their effect.

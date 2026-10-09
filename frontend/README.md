# Frontend

React, Rsbuild, MUI and Module Federation 2.0 in a pnpm workspace (Turborepo for tasks).

```
apps/shell            the host: layout, theme, i18n, session, error boundaries, module loader
packages/ui-kit       tokens.ts (the only file with colours), MUI theme, shared components, CustomFieldsSection
packages/web-core     HTTP client, session and tenant stores, guards, event bus, slots, formatting, i18n
packages/api-clients  typed clients, GENERATED from each service's OpenAPI document
modules/*             federated modules; each exposes ./manifest and never imports another module
tooling               shared build config (the Module Federation shared singletons) and the lint-rule tests
e2e                   Playwright against the built app, served with the Gateway's CSP
```

## Everyday commands

```sh
corepack enable pnpm        # once
pnpm install
pnpm build                  # builds the shell and every module, then lays them out in dist/ the way the Gateway serves them
pnpm typecheck lint test    # all packages
pnpm e2e                    # needs `pnpm build` first
```

`pnpm dev` inside `apps/shell` or a module runs it with live reload and proxies `/api`, `/bff` and `/mfe` to a running Gateway
(`GATEWAY_URL`, default `https://localhost:7443`); set `RSBUILD_PROXY=1` for the shell.

## Rules the build enforces

- Colours live in `packages/ui-kit/src/theme/tokens.ts` only.
- No `/api/` string literals except the path of a typed client call; no host names; no hand-written API types.
- No branching on tenant identity (`tenant.slug === ...`): variations are settings, feature flags or modules.
- A module imports only `ui-kit`, `web-core` and `api-clients`; the shell never imports a module (it loads them at runtime).
- Everything under `generated/`, `openapi/` and `packages/api-clients/src` is produced by `scripts/generate` and checked for drift.

`tooling/tests/lint-rules.test.ts` proves each rule fails when it should.

## Adding a module

Copy `modules/purchase-request`, rename it to the module key from `platform.yaml`, and implement `src/manifest.ts`. (`scripts/new-module`
does this and the backend side together in milestone 6.)

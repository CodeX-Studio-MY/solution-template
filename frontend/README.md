# Frontend

React, Rsbuild, MUI and Module Federation 2.0 in a pnpm workspace (Turborepo for tasks).

```
apps/shell            the host: layout, theme, i18n, session, error boundaries, module loader, Settings
apps/operator         the operator console, served on operator.{BaseDomain}
packages/ui-kit       tokens.ts (the only file with colours), MUI theme, shared components, CustomFieldsSection
packages/web-core     HTTP client, query cache and hooks (useApiQuery, useMaster, useSnackbar, useDialog), session and tenant stores, guards, event bus, slots, formatting, i18n
packages/api-clients  typed clients, GENERATED from each service's OpenAPI document
modules/*             federated modules; each exposes ./manifest and never imports another module
tooling               shared build config (the Module Federation shared singletons) and the lint-rule tests
e2e                   Playwright against the built app, served with the Gateway's CSP
```

## Everyday commands

```sh
pnpm install
pnpm build                  # builds the shell and every module, then lays them out in dist/ the way the Gateway serves them
pnpm typecheck && pnpm lint && pnpm test    # all packages
pnpm e2e                    # needs `pnpm build` first (the mocked-API tests)
pnpm e2e:stack              # signs in against the running stack (scripts/dev-up.sh first)
```

`pnpm dev` inside `apps/shell` or a module runs it with live reload and proxies `/api`, `/bff` and `/mfe` to a running Gateway
(`GATEWAY_URL`, default `https://localhost:7443`); set `RSBUILD_PROXY=1` for the shell.

## Design system

Every visual decision (colour, type, radius, spacing, elevation, motion, density, layout) is in one file,
`packages/ui-kit/src/theme/tokens.ts`. The MUI theme reads it, and `scripts/generate.sh` derives the rest from it: `DesignTokens.g.cs` (Identity's
server-rendered sign-in pages) and [docs/design-system.md](../docs/design-system.md). Change a value there, run `scripts/generate.sh` (`scripts/generate.ps1` on Windows), and commit
the result. `scripts/generate.sh --check` fails if a derived file is out of date. Node 24 or later is needed, because the generator runs the TypeScript file directly.

## Rules the build enforces

- Colours live in `packages/ui-kit/src/theme/tokens.ts` only.
- No `/api/` string literals except the path of a typed client call; no host names; no hand-written API types.
- No branching on tenant identity (`tenant.slug === ...`): variations are settings, feature flags or modules.
- A module imports only `ui-kit`, `web-core` and `api-clients`; the shell never imports a module (it loads them at runtime).
- Everything under `generated/`, `openapi/` and `packages/api-clients/src` is produced by `scripts/generate.sh` and checked for drift by `scripts/generate.sh --check`.

`tooling/tests/lint-rules.test.ts` proves each rule fails when it should.

## Adding a module

Run `scripts/new-module.sh <key>` (`scripts/new-module.ps1` on Windows) from the repository root. It creates `modules/<key>` from `templates/frontend-module` together with the
backend side, the gateway route and the typed API client; then implement `src/manifest.ts` and the pages.

# Prompt: Scaffold a Reusable Multi-Tenant SaaS Template

You are scaffolding a **reusable SaaS starter template**. It will be the starting point for every new product we build, so it must be secure, multi-tenant, observable and runnable from day one. Build the **structure, platform plumbing and the reference scenario in section 7** (one standard module and one tenant-only add-on module, from backend to frontend). Do not build other business features.

**HARD RULE: single source of truth (section 2.1).** Every fact about the application is defined in exactly one place, and everything else is generated from it. This rule overrides convenience everywhere in the backend and the frontend.

Every requirement below is deliberate. If one can't be met as written (a library lacks a feature, a version changed), **stop and say so in `docs/deviations.md`**: what you changed and why. Never silently drop a requirement.

## 0. Parameters

Fill these in first. Use the defaults when a value isn't given. **They are written once into `platform.yaml` (2.1)**; nothing else may repeat them. Wherever this prompt shows `{{PRODUCT_NAME}}` or another parameter, the scaffold reads it from `platform.yaml` or generated code.

| Parameter | Default | Purpose |
|---|---|---|
| `{{PRODUCT_NAME}}` | `farm360` | Prefix for solution, namespaces, packages, containers (`Farm360.Core`, `@farm360/ui-kit`) |
| `{{ORG}}` | `CodeX Studio` | Copyright, package authors |
| `{{DEFAULT_TIMEZONE}}` | `Asia/Kuala_Lumpur` | Default tenant timezone (IANA ID) |
| `{{DEFAULT_CURRENCY}}` | `MYR` | Default tenant currency (ISO 4217) |
| `{{DEFAULT_LOCALES}}` | `en, ms, zh` | UI languages |
| `{{CLOUD}}` | `azure` | Target for storage, secrets and identity adapters (`azure` or `aws`) |

**Versions:** use the latest stable release of every framework and library at scaffold time, and pin exact versions in lock files and `Directory.Packages.props`. Versions named below are the minimum.

---

## 1. Tech stack

| Area | Choice |
|---|---|
| Backend runtime | .NET 10 (LTS), ASP.NET Core, EF Core 10 |
| Data | **Two supported database providers: SQL Server and PostgreSQL** (4.13). SQL Server 2025 / Azure SQL, or PostgreSQL 18 / Azure Database for PostgreSQL / Amazon RDS. One database per tenant plus one catalog database. Local dev and Testcontainers pin **SQL Server 2025** and **PostgreSQL 18** images |
| Messaging | RabbitMQ 4.x with quorum queues, through a **thin messaging layer in `Core` over `RabbitMQ.Client`** (publisher, consumer, outbox, inbox). No messaging framework; see the ADR |
| Cache, locks, rate limits, SignalR backplane | A Redis-protocol store through `StackExchange.Redis`: **Valkey** (BSD licence) locally and when self-hosted; Azure Managed Redis or ElastiCache in the cloud |
| Identity | ASP.NET Core Identity (user store, passwords, lockout, MFA, passkeys) with **OpenIddict 7.x or later** as the OAuth 2.0 / OpenID Connect server (uses its built-in RFC 8693 token exchange) |
| Gateway and BFF | YARP |
| Scheduling | Quartz.NET, clustered, with its job store in the catalog database |
| Mapping | Mapperly (compile-time source generator) or hand-written mapping |
| Local orchestration | Aspire AppHost (13.x or later) for local dev, plus a plain `docker-compose.yml` for anyone not using Aspire |
| Frontend | Latest stable React (19 or later), TypeScript (strict), **Rsbuild** (Rspack), **MUI** (Material UI with CSS theme variables, and the MIT edition of MUI X), Zustand, React Router (import from `react-router`, not `react-router-dom`), Module Federation 2.0 (`@module-federation/rsbuild-plugin`), i18next |
| Backend tests | xUnit v3, Shouldly or AwesomeAssertions (the Apache-2.0 community fork of FluentAssertions), NSubstitute, Testcontainers |
| Frontend tooling | pnpm workspaces and Turborepo; Vitest; Playwright for end-to-end tests |
| Observability | OpenTelemetry (traces, metrics, logs) exported over OTLP |
| CI | GitHub Actions |

**License policy:** the template must not depend on libraries that need a paid commercial license unless `docs/deviations.md` records the decision. At the time of writing that excludes AutoMapper and MediatR (commercial since 2025), MassTransit v9+, FluentAssertions v8+, and Duende IdentityServer or Duende BFF. CI runs a license check (section 9). **MUI X** comes in an MIT edition and commercial Pro and Premium editions; use only the MIT edition (`@mui/x-data-grid`, `@mui/x-date-pickers`) unless a Pro or Premium license is bought and recorded.

**Why Rsbuild and MUI, and not Vite or Tailwind:**
- **Rsbuild** is built on Rspack (Rust), and its team works directly with the Module Federation team, so Module Federation 2.0 support is first-class. It also uses the same bundler in development and production. Vite 8 (Rolldown) is comparably fast, but its Module Federation support is newer and comes through a plugin.
- **MUI** gives a business app the components it needs (data grid, date pickers, forms, dialogs, accessibility) and one theme object that can change per tenant at runtime. **Don't add Tailwind:** it's a styling utility, not a component library. In micro frontends it also causes known problems: each separately built module's classes can be missing from the host's CSS or clash with it. Mixing it with MUI would mean two styling systems.

## 2. Architecture principles (non-negotiable)

1. **Multi-tenant from day one, using one database per tenant.** Every request resolves a tenant, and the tenant decides the database connection. Business code never filters by tenant itself; isolation comes from the connection. The system must work with exactly one tenant, so we can go live single-tenant first and add tenants later without code changes.
2. **A module is a vertical slice that owns its data.** Each business module has one backend part and one frontend part with the same module key. A module owns its own **schema and `DbContext`** inside the tenant database. **No module reads another module's tables, and there are no cross-module foreign keys.** Modules talk through APIs and integration events.
3. **One service per module by default, but hosting is a deployment choice.** Each module is a class library plus a thin host project. By default every module runs as its own service. The same modules can also run together in one process (for local development or a small customer), without code changes.
4. **A shared kernel provides the plumbing.** Hosts get authentication, tenancy, data access, messaging, storage, resilience, logging and error handling from one call to `Core`. A host's `Program.cs` is about 20 lines.
5. **Secure by default.** Every endpoint requires authentication unless explicitly marked `[AllowAnonymous]`. Users, machines and services all sign in, and all are authorized by their role in each module. No secrets in the repo, ever. Where the cloud supports it, services use managed identities instead of secrets.
6. **Cloud-ready by default.** Services are stateless and can run as several instances: files go to object storage, time is UTC, there's no local disk state, and recurring jobs run once, not once per instance.
7. **Modules can be switched on per tenant.** A tenant's subscription (entitlements) decides which modules it can use. The backend enforces this; the frontend only hides what the tenant doesn't have.
8. **Observable and operable.** Every request, message and job is traceable end to end with its tenant. Health, metrics and audit logs exist from the first commit.
9. **Boundaries are enforced by tests, not by convention.** Architecture tests fail the build when a rule in this document is broken.
10. **Single source of truth (hard rule, 2.1).** One definition per fact; everything else is generated or read from it.

### 2.1 HARD RULE: single source of truth

**Every fact about the application exists in exactly one place.** Changing something (adding a module, a role, a setting, a route, a port, a package version) means editing **one** file, then running **one** command. If the same fact has to be typed in two places, the scaffold is wrong. This applies equally to the backend, the frontend, the infrastructure and the docs. It is not a guideline: if you find a case where it seems impossible, record it in `docs/deviations.md` and explain why.

**The sources of truth**

| Fact | The one place it's defined | What's generated from it, or reads it |
|---|---|---|
| **The application's structure:** the parameters in section 0; **services** (key, host, port, which access modules each owns); **access modules** (key, display name, plans, owning service); roles and their order | **`platform.yaml`** at the repo root | Backend `Modules` and `Roles` constants; frontend module keys and role types; gateway (YARP) routes; Aspire AppHost resources; `docker-compose.yml`; CI workflows and path filters; module seed data for the catalog; the module table in `README.md` |
| **Runtime configuration** (URLs, connection references, limits, timeouts) | **One typed options schema per area in `Core`** (C# records with validation attributes), with defaults written once in those records. Values per environment come only from environment variables or the secret store. | `.env.example` and the configuration reference in `docs/configuration.md` are generated from the schema. Startup validation uses the same schema. |
| **Frontend runtime configuration** | The same backend schema, served by the Gateway at `GET /bff/config` | The shell loads it at start-up. **Build once, deploy everywhere:** no environment values are baked into frontend builds, and there are no per-environment frontend builds. |
| **API contracts** (endpoints, request and response shapes, enums, validation limits) | **The backend code** | OpenAPI documents, then the TypeScript clients and types in `api-clients`. The frontend never hand-writes an API type, URL path or enum. |
| **Error codes and their messages** | One error catalog per module in the backend (code, HTTP status, i18n key) | Returned in `ProblemDetails`; the frontend error-code type is generated |
| **Integration events** | `Contracts` (one C# type per event version) | Publisher and consumer code use the same type; event documentation is generated |
| **Tenant settings** (shape and defaults) | The settings record in the owning module (for example `PurchaseRequestSettings`) | Settings JSON schema for the admin UI and validation; frontend settings types via OpenAPI |
| **Per-tenant values** (settings, flags, strategy choices, entitlements, branding) | **The catalog database** (data, not files) | Read at runtime by backend and frontend through APIs. Never copied into code or config. |
| **Feature flag keys, permission names, slot names and slot prop types** | One declaration each: flag keys and permissions in the owning module (generated into the frontend), slot names and props in `web-core` | Everywhere else imports the declaration. No string literals. |
| **Design tokens** (colours, spacing, typography, radius) | `ui-kit/src/theme/tokens.ts` | The MUI theme, with tenant branding applied as overrides at runtime |
| **UI text** | One i18n resource file per module per locale | No UI strings in components |
| **Package versions** | `Directory.Packages.props` (.NET) and the **pnpm catalog** in `pnpm-workspace.yaml` (frontend) | Module Federation's `shared` config reads versions from the installed packages, never typed by hand |
| **Application version** | The git tag | Assembly versions, image tags, frontend build metadata |

**How it's enforced**
- **One command regenerates everything:** `scripts/generate` (a small .NET or Node tool in the repo). Generated files start with a `// <auto-generated> Edit platform.yaml instead and run scripts/generate.` header.
- **CI drift check:** CI runs `scripts/generate` and fails if anything changed. Generated files can't be edited by hand or fall out of date.
- **No duplicate literals:**
  - architecture tests and lint rules fail on hard-coded module keys, role names, flag keys or `/api/…` paths outside generated code
  - lint fails on API types written by hand in the frontend
  - lint fails on colour values outside `tokens.ts`
- **No per-environment copies:** there's no `appsettings.Production.json` holding values. Environments differ only by environment variables and secret store entries, all checked against the same schema.
- `scripts/new-module <key>` adds the service and module entries to `platform.yaml`, runs `scripts/generate`, then instantiates the module templates in `templates/` (13, item 5). A new module must never require editing routes, AppHost, compose, CI or constants by hand.

**Example: adding a module the right way**
```yaml
# platform.yaml (excerpt)
product: farm360
org: CodeX Studio
defaults: { timezone: Asia/Kuala_Lumpur, currency: MYR, locales: [en, ms, zh] }
cloud: azure
database: { providers: [sqlserver, postgresql] }   # which one an environment uses is runtime config
roles: [user, manager, admin]                 # lowest to highest
plans: [standard, plus]

services:                                     # what runs, and how it's routed: /api/{service-key}/*
  - { key: identity,           port: 5001, owns: [access-management] }
  - { key: tenancy,            port: 5002, owns: [tenant-settings, billing] }
  - { key: notification,       port: 5003, owns: [] }
  - { key: purchase-request,   port: 5101, owns: [purchase-request] }
  - { key: cost-center-budget, port: 5102, owns: [cost-center-budget] }

modules:                                      # what is authorized and entitled: roles claim, plans
  - { key: access-management,  name: Access Management,  plans: [standard, plus] }
  - { key: tenant-settings,    name: Tenant Settings,    plans: [standard, plus] }
  - { key: billing,            name: Billing,            plans: [standard, plus] }
  - { key: purchase-request,   name: Purchase Requests,  plans: [standard, plus] }
  - { key: cost-center-budget, name: Cost Center Budget, plans: [plus] }
```
- A **service** is something that runs and is routed (`/api/{service-key}/*`). An **access module** is something roles and plans apply to (`roles` claim, entitlements). A business module is usually both, with the same key. Platform services can own several access modules, or none (Notification is available to every signed-in principal).
- Adding a service and module entry for `goods-receipt` and running `scripts/generate` updates the backend constants, frontend types, gateway route, hosts, AppHost, compose, Migrator's module list, CI workflow, catalog seed and README in one step.
- **Scripts are cross-platform:** `scripts/generate`, `new-module`, `onboard-tenant` and `dev-up` are implemented once in .NET or Node, with thin `.sh` and `.ps1` wrappers, so macOS, Linux and Windows developers run the same thing.

## 3. Repository layout

Use **one monorepo** with hard boundaries, so any module, host or frontend module can be moved into its own repo later without code changes.

```
{{PRODUCT_NAME}}/
├── platform.yaml                            # SINGLE SOURCE OF TRUTH for structure (2.1)
├── backend/
│   ├── {{PRODUCT_NAME}}.slnx                # .NET 10 XML solution format
│   ├── Directory.Build.props                # Nullable, warnings as errors, analyzers, banned APIs
│   ├── Directory.Packages.props             # Central package management (exact versions)
│   ├── BannedSymbols.txt                    # DateTime.Now, DateTime.Today, DateTimeOffset.Now, …
│   ├── src/
│   │   ├── Core/                            # Shared kernel (packable as a NuGet package)
│   │   ├── Contracts/                       # Versioned integration events and shared contract types only
│   │   ├── Modules/
│   │   │   ├── Identity/                    # Token server, users, machines, service clients, groups, module roles
│   │   │   ├── Tenancy/                     # Catalog, tenant lifecycle, onboarding, entitlements, settings
│   │   │   ├── Notification/                # Real-time notifications and email
│   │   │   ├── PurchaseRequest/             # Reference scenario: standard module for every tenant (section 7)
│   │   │   └── CostCenterBudget/            # Reference scenario: add-on module for one tenant's plan (section 7)
│   │   │       # each module is one class library with folders Api/, Domain/, Application/, Infrastructure/ (DbContext, migrations);
│   │   │       # architecture tests enforce Domain → nothing, Application → Domain, Api/Infrastructure → Application
│   │   ├── Hosts/
│   │   │   ├── Identity.Host/  Tenancy.Host/  Notification.Host/
│   │   │   ├── PurchaseRequest.Host/  CostCenterBudget.Host/
│   │   │   └── AllInOne.Host/               # Runs every module in one process (dev, small deployments)
│   │   ├── Gateway/                         # YARP: public entry point, BFF for the web app, /api/{module-key}/*
│   │   ├── Migrator/                        # Console job: applies catalog and tenant migrations
│   │   ├── Scheduler/                       # Quartz.NET host: the ONE place recurring jobs run
│   │   └── AppHost/                         # Aspire AppHost: local orchestration and dashboard
│   └── tests/
│       ├── Architecture.Tests/              # Enforces the boundary rules (ArchUnitNET or NetArchTest)
│       ├── Core.Tests/
│       ├── Identity.Tests/
│       ├── PurchaseRequest.Tests/
│       ├── CostCenterBudget.Tests/
│       └── Integration.Tests/               # Testcontainers: SQL Server AND PostgreSQL, RabbitMQ, Valkey
├── frontend/
│   ├── pnpm-workspace.yaml (with the pnpm catalog)   turbo.json
│   ├── apps/shell/                          # Host app: layout, sidebar, module loader, theme, i18n
│   ├── packages/
│   │   ├── ui-kit/                          # MUI theme and shared components
│   │   ├── web-core/                        # HTTP client, session, tenant context, guards, event bus, i18n
│   │   └── api-clients/                     # TypeScript clients generated from each module's OpenAPI document
│   ├── modules/
│   │   ├── purchase-request/                # Standard module (federated, exposes ./manifest)
│   │   └── cost-center-budget/              # Add-on module, loaded only for entitled tenants
│   └── e2e/                                 # Playwright tests against the composed app
├── deploy/
│   ├── docker-compose.yml   .env.example    # Keys only, no values
│   └── {{CLOUD}}/                           # Placeholder for cloud deployment (not implemented)
├── docs/
│   ├── adr/   deviations.md   adding-a-module.md   onboarding-a-tenant.md   configuration.md (generated)
│   └── runbooks/                            # Restore a tenant, rotate keys, move a tenant, offboard a tenant
├── templates/                               # dotnet new module template + frontend module template (tested in CI)
├── scripts/                                 # generate, new-module, onboard-tenant, dev-up (cross-platform, .sh and .ps1 wrappers)
└── .github/workflows/                       # CI per module, host and frontend module, filtered by changed paths
```

## 4. Backend: shared kernel and platform rules

`Core` exposes `builder.AddPlatform(PlatformServices.PurchaseRequest)` and `app.UsePlatform()`, where `PlatformServices` is generated from `platform.yaml` (the service's key, port and owned modules come from there, never typed in `Program.cs`). Between them they register and enable everything in this section. `Core` also provides the Aspire service defaults (OpenTelemetry, health checks, resilience, service discovery), so there's one shared setup, not two.

### 4.1 Tenancy

- `ITenantContext` (scoped) with `TenantId`, `TenantSlug`, `Status`, `TimeZone`, `Currency`, `Locale` and `EnabledModules`.
- **Tenant resolution:**
  - **Authenticated requests:** the tenant is the token's `tenant_id`, nothing else. If the request's subdomain names a different tenant, the request gets **403**.
  - **Platform service principals** (no `tenant_id`, with the `tenant:any` permission) name the tenant with the `X-Tenant` header. `X-Tenant` from any other caller gets **403**.
  - **Anonymous requests** (sign-in pages, invitation links): the tenant comes from the subdomain, `{tenant}.{BaseDomain}`, where `BaseDomain` is runtime configuration.
  - Locally, tenants are `demo.localhost` and `demo-plus.localhost`; browsers resolve `*.localhost` to the local machine.
- **Catalog database**, owned by the Tenancy module. It holds tenants, plan, entitlements, status, settings, and **tenant placement**: which server or pool, which database and **which database provider** each tenant lives on.
  - **Azure:** services connect with managed identity (Microsoft Entra authentication), for both Azure SQL and Azure Database for PostgreSQL, so there's no password at all.
  - **AWS:** RDS for PostgreSQL uses IAM database authentication. RDS for SQL Server doesn't support IAM authentication, so its credentials come from Secrets Manager with rotation. The catalog stores the secret's ARN, never a password.
- **Tenant placement is data, not code.** Moving a tenant to another server or pool means copying the database and updating the catalog. `docs/runbooks/move-a-tenant.md` covers it.
- `ITenantConnectionResolver` looks up a tenant's connection with a short-lived cache, and **refuses** tenants that are suspended or deleted.
- `AddModuleDbContext<TContext>(schema)` configures EF Core per request with the resolved tenant's connection. Use `IDbContextFactory` or per-request options; **don't use DbContext pooling** with tenant-specific state.
- **Connection pools:** each distinct connection string gets its own ADO.NET pool. Set `Max Pool Size` deliberately and document the ceiling (tenants × instances × pool size). For many small tenants use elastic pools (Azure SQL) or a connection pooler in front of PostgreSQL (PgBouncer, which is built into Azure Database for PostgreSQL flexible server), because each PostgreSQL connection is a server process.
- **Tenant lifecycle:** `Provisioning → Active → Suspended (read-only) → PendingDeletion → Deleted`. Suspended tenants can sign in and read but not write. Deletion first produces a full data export, then deletes the database and the tenant's file prefix after a configurable grace period.
- Tenancy flows through everything:
  - **Messaging:** every event envelope carries `TenantId`, and consumers restore `ITenantContext` before handling.
  - **Jobs:** the scheduler runs each job once per active tenant, in that tenant's scope.
  - **Storage:** object keys start with `{tenantId}/`.
  - **Cache:** keys start with `{tenantId}:`.
  - **SignalR:** connections join group `tenant:{tenantId}`.
  - **Logs and traces:** `tenant.id` is on every log scope and span. **Not on metrics by default**, because thousands of tenants would explode metric cardinality. Use per-tenant metrics only for a bounded allowlist.

### 4.2 Data

- **Module-owned schemas:** each module has its own schema (for example `purchase_request.*`), its own `DbContext`, and its own migrations history table in that schema. No shared "god" `DbContext`.
- **Keys:** Guid primary keys are **generated by the EF Core provider**, never in entity code (don't initialise `Id = Guid.NewGuid()`). The right generator differs per provider, and `Core` configures it (4.13): SQL Server uses EF Core's sequential GUIDs, because SQL Server sorts `uniqueidentifier` by its last six bytes first and v7 values would fragment the index; PostgreSQL uses UUIDv7, which sorts correctly there.
- `BaseEntity`: `Id`, `CreatedOn`, `CreatedBy`, `UpdatedOn`, `UpdatedBy`, filled by a `SaveChanges` interceptor from the current principal. When a user is signed in on a machine, record the user, and keep the machine in the audit log.
- **Optimistic concurrency** on every aggregate (a provider row version, 4.13), exposed to the API as an `ETag` and checked with `If-Match`. A conflict returns 412.
- **Soft delete:** an interface plus a global query filter. Unique indexes on soft-deletable tables are filtered on not-deleted rows, with the filter SQL produced by a `Core` helper per provider (4.13).
- **Business dates:** dates that belong to the business day (order date, harvest date) are `DateOnly`, calculated in the **tenant's** timezone. Timestamps are UTC (`datetime2` on SQL Server, `timestamptz` on PostgreSQL).
- **Money:** `decimal(19,4)` (`numeric(19,4)` on PostgreSQL) stored with its ISO currency code. Never `float` or `double`.
- **Document numbering:** `ISequenceService`, backed by a per-tenant counter table incremented atomically in the same transaction (one `UPDATE … OUTPUT` on SQL Server, `UPDATE … RETURNING` on PostgreSQL), or database sequences. Each provider has its own implementation, tested on both. Never `count + 1`. The format is configurable per tenant (`PO-{yyyyMMdd}-{seq:0000}`), and the date part uses the tenant's business date.
- **Migrations:**
  - Only `Migrator` applies migrations, never a running API host. Its list of module `DbContext`s is generated from `platform.yaml`. `Migrator` supports `--catalog`, `--tenant <id>` and `--all`. `--all` runs with bounded parallelism, records per-tenant success or failure in the catalog, and can resume.
  - **Expand and contract:** every migration must be backward compatible with the previous release, because tenants are migrated one by one while old and new code both run. Destructive changes (drop or rename) ship in a later release, after the code stops using the old shape.
  - The deploy pipeline runs `Migrator` **before** rolling out new code.

### 4.3 Time

- Use .NET's built-in **`TimeProvider`** (inject it; never call the static clock) and an `ITenantClock` wrapper with `Today` (`DateOnly`) and `Now` in the tenant's timezone, using IANA timezone IDs.
- **Ban `DateTime.Now`, `DateTime.Today` and `DateTimeOffset.Now`** with `Microsoft.CodeAnalysis.BannedApiAnalyzers` (`BannedSymbols.txt`). Tests use `FakeTimeProvider`.

### 4.4 Identity: three kinds of principal

A **principal** is anything that can sign in. There are exactly three kinds, all authorized the same way (4.5).

| Principal type | Examples | How it signs in | Tokens |
|---|---|---|---|
| **User** | Staff on the web app or mobile app | **Web:** through the Gateway acting as a BFF (below), using authorization code + PKCE. **Mobile:** authorization code + PKCE in the system browser (RFC 8252). Passkeys (WebAuthn) preferred; password + TOTP MFA supported. | Access 15 min. Refresh 7 days with sliding renewal, rotated on use. For web, refresh tokens stay in the Gateway, never in the browser. |
| **Machine** | Shared tablet at a workstation, scale or IoT gateway, label-print agent on a customer's LAN | **Enrollment:** a tenant admin creates the machine and gets a one-time enrollment code (valid 15 minutes, single use). The device **generates its own key pair** and registers only the public key, so the private key never leaves the device. **Sign-in:** `client_credentials` with `private_key_jwt` (RFC 7523). | Access 15 min. No refresh token: the device requests a new token. |
| **Service** | Our own modules calling each other; external integrations (ERP, partner APIs) | `client_credentials` with `private_key_jwt` (preferred) or a client secret from the secret store | Access 5–15 min, cached and renewed by a `Core` HTTP handler |

**Token server**
- OpenIddict on top of ASP.NET Core Identity in the Identity module. Don't hand-roll OAuth.
- **No password grant:** RFC 9700 (OAuth 2.0 Security Best Current Practice) says it MUST NOT be used.
- **Access tokens are signed JWTs, not encrypted.** OpenIddict encrypts access tokens by default, so call `DisableAccessTokenEncryption()`. That lets the other services validate them with public keys only. Authorization codes and refresh tokens stay encrypted, as OpenIddict requires.
- **Signing:** asymmetric **ES256**. The private key exists only in the Identity module, loaded from Key Vault or Secrets Manager, never from the repo or config files. Every key has a `kid`. Keys rotate on a schedule, and the old public key stays published until its last token expires.
- **Encryption keys** (OpenIddict encrypts authorization codes and refresh tokens) also come only from the secret store and rotate on a schedule, with the previous key kept for decryption until its tokens expire. The same applies to the ASP.NET Core Data Protection key ring that protects BFF sessions, which is stored in Redis and protected by a key from the secret store.
- **Validation in `Core`** (identical in every service):
  - accept **only ES256** (no `none`, no HMAC)
  - fetch keys only from the configured issuer's discovery and JWKS endpoints
  - check `iss`, `exp` and `nbf` with at most 30 seconds of clock skew, and `aud` against **this host's own audiences** (below)
  - require the `typ` header `at+jwt` (RFC 9068)
  - the request's subdomain must name the same tenant as the token's `tenant_id`, or the request gets 403 (4.1)
- **One audience per service:** every service has its own audience, `{product}:{service-key}` (for example `farm360:purchase-request`), generated from `platform.yaml`. A host accepts only the audiences of the services it runs (`AllInOne.Host` accepts all of its own). A token stolen from one service is therefore useless against another, which blocks lateral movement. Clients ask for the audience they need with resource indicators (RFC 8707): the BFF obtains and caches an access token per downstream service for each session, and a module calling another module exchanges for a token with that module's audience (RFC 8693).
- A token is trusted because of its signature, not because its contents are hard to read. Anyone can decode a JWT, so never put secrets or personal data in one.
- Never log tokens, cookies or `Authorization` headers. Redact them in logs and traces.

**Web sign-in through a BFF**
- Follow **RFC 10017** (OAuth 2.0 for Browser-Based Applications, BCP 212) and use its BFF pattern. The browser never holds access or refresh tokens.
- The Gateway is a confidential OAuth client. After sign-in it keeps the tokens server-side (in Redis, encrypted with Data Protection) and gives the browser a session cookie: `__Host-` prefix, `HttpOnly`, `Secure`, `SameSite=Lax`. Use `Lax` rather than `Strict`: with `Strict`, users arriving from a link in an email or another site would look signed out on the first page. State-changing requests are protected by the CSRF header below.
- When proxying `/api/*`, the Gateway attaches the access token and refreshes it when needed.
- **CSRF:** every state-changing request must carry a custom header (`X-CSRF: 1`). A cross-site page can't add custom headers without a CORS preflight, which the Gateway refuses, so forged requests are blocked.
- The Gateway exposes `/bff/login`, `/bff/logout`, `/bff/user` (claims needed by the UI, including `roles`) and `/bff/config` (frontend runtime configuration, 2.1). It also serves the shell's static files, so the app, the BFF and `/api` share one origin per tenant subdomain.
- **One issuer, central sign-in domain:** the token server lives at `https://login.{BaseDomain}`, and that is the single, constant `iss` for every tenant. The Gateway on `{tenant}.{BaseDomain}` passes the tenant in the authorization request; the sign-in page shows that tenant's branding and authenticates against that tenant's users.
- **Redirect URIs are exact, never wildcards.** Each tenant's callback (`https://{tenant}.{BaseDomain}/bff/signin-oidc`, plus its sign-out callback) is registered on the BFF client during onboarding and removed during offboarding. Local dev registers the `*.localhost` demo hostnames the same way.
- **SignalR under the BFF:** WebSocket connections go through the Gateway, which authenticates the session cookie and attaches the Notification service's access token server-side; the browser never sees a token. The Gateway re-checks the session on every reconnect and closes connections when the session ends.

**Passwords** (when used), following NIST SP 800-63B-4:
- at least 15 characters when the password is the only factor, or 8 when MFA is on
- allow at least 64 characters
- **no** composition rules (forced symbols or digits) and no forced periodic changes
- reject passwords found in a breached-password list, for example the Have I Been Pwned k-anonymity API
- hash with ASP.NET Core Identity's hasher using an iteration count that meets current OWASP guidance

**Other rules**
- Every token carries `principal_type` (`user`, `machine` or `service`) and `tenant_id`.
  - Users and machines belong to exactly one tenant.
  - A service principal belongs to one tenant (external integrations) or is a **platform service** with no tenant. Only platform services may send `X-Tenant`, and they need the `tenant:any` permission. Anyone else sending it gets 403.
- **Acting for a user across services:** when module A calls module B while handling a user's request, it exchanges the user's token for one with module B's audience (RFC 8693 token exchange), so B still sees the user. A module's own service token is only for background or system work.
- **A user signed in on a shared machine** (for example a worker on a packing-station tablet): the device exchanges the user's sign-in and its machine token for one delegated token (RFC 8693). In it, `sub` is **the user** and the `act` claim identifies **the machine**. The request is authorized with the user's roles, and the audit trail records both. Use OpenIddict's built-in token exchange flow; don't write a custom grant.
- **Where identities and OpenIddict data live:**
  - **Tenant database:** users, passkeys, machines, groups, assignments, and OpenIddict's **authorizations and tokens** (including refresh tokens) for that tenant's principals.
  - **Catalog:** OpenIddict **applications** (client registrations) with a `tenant_id` column (null for platform clients such as the BFF and platform services), so a token request can find its tenant; the platform operator realm.
  - OpenIddict's default stores aren't tenant-aware, so implement **tenant-aware OpenIddict stores**: the application store reads the catalog, and the authorization and token stores resolve the tenant database from the client's or subject's tenant. Revoking a tenant's tokens never touches another tenant's database.
- **Platform operators** (our own support staff) are a separate realm stored in the catalog. They sign in to a separate operator console client, on its own hostname, with **passkey or TOTP MFA required**. They can access a tenant only through **time-limited, audited support access** that a tenant admin can see and optionally must approve. A support-access token carries the tenant's `tenant_id`, a restricted set of `roles`, and an `act` claim identifying the operator, so every action is attributed to the operator, and the tenant can see it in its audit log.
- Machines and service clients can be disabled and have their keys or secrets rotated (two valid at once during rotation), and they record a last-seen timestamp.
- Refresh tokens are stored hashed (OpenIddict's token store), rotated on every use, and reuse is detected: reusing an old token revokes the whole chain.
- **Rate limiting** on the token, sign-in, enrollment and password-reset endpoints. Lockout uses increasing delays, not a permanent lock, so attackers can't lock users out on purpose.
- **No default passwords in code.** The first admin is created during onboarding through an emailed invitation. Password reset uses a single-use emailed token.
- Optional: bind machine tokens to the device key with DPoP (RFC 9449), if the OpenIddict version supports it. Record in `docs/deviations.md` if it doesn't.

### 4.5 Authorization: module roles

Every principal type is authorized the same way: **by its role in each module.**

**Concepts**
- **Module (access module):** a business area that roles and plans apply to, for example `purchase-request` or `cost-center-budget`. Each has a GUID primary key in the database and a **module key** used everywhere else (4.6).
- **Platform modules** are modules too: `access-management` (users, groups, roles), `tenant-settings` and `billing`. A "tenant admin" is simply someone with `admin` in `access-management`.
- **Role:** an ordered hierarchy where a higher role includes everything a lower one can do. Default: `user (1) < manager (2) < admin (3)`. Roles and their order are defined once in `platform.yaml` (2.1). The backend, frontend and catalog seed data are generated from it.
- **Group:** a named set of principals (users and machines) in a tenant.
- **Access assignment:** `(principal OR group) → module → role`.

**Assignment rules**
- A principal can access **many modules, with a different role in each**. For example, Ali is `manager` in `purchase-request` and `user` in `cost-center-budget`.
- **One role per module per assignee:** a principal can't be given two roles in the same module directly, and neither can a group. Enforce it with **filtered** unique indexes: `(PrincipalId, ModuleId) WHERE PrincipalId IS NOT NULL` and `(GroupId, ModuleId) WHERE GroupId IS NOT NULL`. Without the filter, SQL Server would treat the NULLs as duplicates. Add a check constraint that exactly one of `PrincipalId` and `GroupId` is set, and return a clear validation error.
- A principal can belong to many groups, and **everyone in a group inherits the group's module roles**.
- Service principals get direct assignments only; they aren't put in groups.

**Effective access** (one unit-tested `EffectiveAccessResolver`)
1. Collect the principal's active direct assignments and the assignments of every active group it belongs to.
2. For each module, keep the **highest** role. Direct and group assignments count equally, and **the highest always wins**. If a principal ends up with both `manager` and `user` in `purchase-request`, they get `manager`.
3. Remove modules the tenant isn't entitled to. Entitlement always overrides assignment.
4. Merge data scopes (for example site IDs) from the principal and its groups as a union.

| Source | `purchase-request` | `cost-center-budget` | `access-management` |
|---|---|---|---|
| Ali directly | **admin** | user | — |
| Group "Finance" (Ali is a member) | user | **manager** | **user** |
| **Ali's effective access** | **admin** | **manager** | **user** |

If Ali's tenant is on the Standard plan, `cost-center-budget` is removed in step 3.

**Enforcement**
- **Fallback authorization policy:** `RequireAuthenticatedUser()`. Health and token endpoints are explicitly `[AllowAnonymous]`.
- `[ModuleRole(Modules.PurchaseRequest, Role.Manager)]` means "at least manager in `purchase-request`". It also checks the tenant's entitlement and that the tenant isn't suspended for writes.
- `[AllowPrincipalTypes(PrincipalType.Machine)]` optionally restricts an endpoint to certain principal types. Without it, any type with the required role may call.
- **Finer rules,** such as "only managers can approve", are written as a permission-to-minimum-role map in the module's own code (`Permissions.Approve => Role.Manager`). They never go in the token.
- `[ModuleRole]` compares role **levels**. Don't use `User.IsInRole("purchase-request:user")`: it's an exact match and would refuse a manager.

**Token claims**
- `sub`, `principal_type`, `tenant_id`, `access_version`; optional `act`.
- `roles`: the **RFC 9068 claim**, an array of `"{module-key}:{role}"` holding **only the effective role**, at most one entry per module. Example: `"roles": ["purchase-request:manager", "cost-center-budget:user"]`. Never put raw assignments in the token.
- `entitlements`: optional RFC 9068 claim listing the tenant's module keys, included only if the UI needs it.
- `data_scope`: only for small, bounded lists. If the list can grow, put a reference in the token and have services look it up in Redis.
- Keep tokens under 4 KB, with a test using a principal that has every module.

**Access changes take effect quickly:** changing an assignment, a group membership or a group's roles increments the affected principals' `access_version`; for a group change, that's every member. `Core` middleware in every service compares the token's `access_version` with Redis (cached in memory for at most a few seconds) and returns 401 if it's stale. The BFF or client then refreshes and gets the new roles.
- **If Redis is unavailable:** use the last known value for up to 60 seconds (configurable), then **fail closed** with 503, not 401, so clients retry instead of signing users out. Both behaviours are tested.
- **Entitlements, tenant settings and feature flags are checked live, not only from the token:** `[ModuleRole]` also checks the tenant's current entitlements from a cache of the catalog. Any change to entitlements, settings or flags publishes an invalidation over Redis pub/sub, so every instance drops its cached copy within seconds. Removing a module from a plan takes effect immediately, without waiting for tokens to expire.

**Audit:** sign-ins, failed sign-ins, assignment changes, group changes, support access and key rotations are written to an **append-only audit log** per tenant, with who, what, when, from where and the trace ID.

### 4.6 Module keys

A module key is the readable, stable ID of a module or service, for example `purchase-request`.
- **Format:** `^[a-z][a-z0-9-]{1,29}$`, unique, in lowercase kebab-case.
- **Reserved keys** that no module or service may use: `api`, `bff`, `mfe`, `health`, `login`, `admin`, `static`. The generator rejects them.
- **Use readable words, not abbreviations** (`purchase-request`, not `pr`), and no filler words (`inventory`, not `inventory-management`).
- **Never rename a key once it's live.** Change the display name instead. Renaming would break tokens, permissions, URLs and stored manifests.
- **Use the same key everywhere:** the `roles` claim, the gateway route `/api/{key}/*`, the frontend module `id` and `basePath` `/{key}`, the CDN path `/mfe/{key}/{version}/`, the database schema name (with hyphens replaced by underscores) and the OpenAPI document name.
- **Storage:** a `Module` table with `ModuleId` (GUID primary key), `ModuleKey` (unique `varchar(30)`, with a check constraint for the pattern) and `DisplayName`. Assignments reference `ModuleId`; tokens and code use `ModuleKey`.
- **Code:** module keys are declared **only** in `platform.yaml`. `scripts/generate` produces the backend constants (`Modules.PurchaseRequest = "purchase-request"`), the frontend `ModuleKey` type, gateway routes and seed data. The generator fails if two modules share a key or a key breaks the pattern.
- Role names in tokens are lowercase (`user`, `manager`, `admin`).

### 4.7 API conventions

- Every service's routes live under `/api/{service-key}/v{n}/…` (for business modules, the service key and the module key are the same). Version with `Asp.Versioning`. Breaking changes need a new version, and the old one is kept until clients have moved.
- **Errors:** `ProblemDetails` (RFC 9457, which replaced RFC 7807) from global exception handling. Business exception types map to status codes (`NotFound` 404, `Conflict` 409, `Forbidden` 403, `BusinessRule` 422, validation 400 with field errors). Never return stack traces outside Development.
- **Validation** at the edge (.NET's built-in validation or FluentValidation), with error messages localized to the tenant's or user's locale.
- **Idempotency:** `POST` endpoints that create records accept an `Idempotency-Key` header (following the IETF Idempotency-Key header draft). A repeated key returns the original result instead of creating a duplicate.
- **Concurrency:** `ETag` and `If-Match` on updates (4.2). An update **without** `If-Match` returns **428 Precondition Required** (RFC 6585), so lost updates can't happen silently.
- **Paging:** cursor or page-number paging with a hard maximum page size, plus a consistent response shape.
- **Rate limits:** per-tenant and per-principal limits, taken from the tenant's plan, use a **distributed limiter backed by Redis**, because ASP.NET Core's built-in limiter counts per instance and would multiply the limit by the number of instances. The built-in in-memory limiter is used only as coarse per-instance protection. This stops one noisy tenant affecting everyone. Exceeding a limit returns 429 with `Retry-After`.
- Request body size limits per endpoint. Upload limits are set separately (4.9).

### 4.8 Messaging

- RabbitMQ with **quorum queues** (classic queue mirroring was removed in RabbitMQ 4.0), publisher confirms, manual acknowledgements and a bounded prefetch.
- `IEventPublisher` and `IEventConsumer<T>` in `Core`. Retries with exponential backoff, then a dead-letter queue, plus a dead-letter viewer and replay command.
- **Transactional outbox:** events are saved in the same transaction as the business data and published by a relay. **The relay is signal-driven, not a poller over every tenant database:** after a commit that wrote outbox rows, the service publishes a small "tenant X has pending outbox" signal (Redis stream). Relay workers process signalled tenants with bounded parallelism, plus a slow sweep (for example every 5 minutes) that catches any missed signal. **Inbox:** consumers record processed message IDs per tenant, so handling is idempotent.
- **Event contracts** live in `Contracts`, are versioned (`OrderPlaced.v1`), and only ever change by adding optional fields. Breaking changes mean a new version.
- Every message carries `TenantId`, the event ID, the event time, the producer, and the W3C `traceparent`, so traces continue across the queue.

### 4.9 Files

- `IFileStorage` with adapters for local disk (dev only), Azure Blob and S3. Locally use **Azurite** for `azure`. For `aws`, use a maintained S3-compatible emulator such as **SeaweedFS**, recorded in an ADR. **Don't use MinIO:** its community edition stopped publishing images in October 2025 and was archived in April 2026.
- Object keys are `{tenantId}/{module-key}/{entity}/{id}/{fileId}`. File names come from the server, never from user input.
- Upload: check the size limit and the **real** content type (magic bytes), and call a malware-scan hook (an interface; Defender for Storage or ClamAV adapters are optional). Files stay quarantined until the scan passes.
- Downloads use short-lived signed URLs. APIs never stream from local disk.

### 4.10 Service-to-service calls and resilience

- Every outgoing `HttpClient` uses `Microsoft.Extensions.Http.Resilience` (the standard resilience handler: timeout, retry with jitter, circuit breaker).
- A `Core` delegating handler attaches the right token: an exchanged user token when acting for a user, or the module's service token for system work (4.4).
- Calls go through service discovery names (Aspire locally, platform DNS in the cloud), never hardcoded hosts.

### 4.11 Observability and health

- OpenTelemetry traces, metrics and logs for ASP.NET Core, HttpClient, EF Core, RabbitMQ and Redis, exported over OTLP (the Aspire dashboard locally).
- Structured JSON logs with no personal data or secrets. Every log entry has the trace ID, tenant ID and principal ID.
- **Health:**
  - `/health/live` checks **only that the process responds**, with no dependencies, so a database blip doesn't restart every container.
  - `/health/ready` checks the catalog database, RabbitMQ and Redis.
- Starter SLOs in `docs/` (availability and p95 latency per module), with alert rules as placeholders.

### 4.12 Network and HTTP security

- Only the Gateway is public. Hosts, databases, RabbitMQ and Redis stay on a private network. TLS everywhere outside a developer's machine.
- **Security headers** from the Gateway:
  - `Strict-Transport-Security`
  - `Content-Security-Policy`: `script-src` limited to self and the module CDN; `style-src 'self' 'nonce-…'`, with the Gateway injecting a per-request nonce into the shell's `index.html` and the shell passing it to Emotion's cache (MUI injects styles at runtime); `connect-src 'self'` plus the module CDN (for `mf-manifest.json`) and the Gateway's WebSocket origin (`wss:`) for SignalR; `frame-ancestors 'none'`. If the nonce isn't feasible, allow `'unsafe-inline'` for styles only and record it in `docs/deviations.md`.
  - `X-Content-Type-Options: nosniff`
  - `Referrer-Policy`
- CORS: since the web app is served from the same origin as the BFF, CORS is **off** by default. Any extra origins come from configuration, never code.
- Options validation at startup: a host refuses to start on missing or weak configuration (issuer URL, signing key reference, catalog connection, Redis, RabbitMQ).

### 4.13 Database providers: SQL Server and PostgreSQL

The platform supports **two database providers, SQL Server and PostgreSQL**, and is designed so more can be added.

- **Where it's chosen:** the list of supported providers is in `platform.yaml` (`database.providers: [sqlserver, postgresql]`). Which provider an environment uses is one runtime configuration value (`Database:Provider`), applied to the catalog and every tenant database. The catalog records each tenant's provider in its placement, so per-tenant providers can be added later; until then, all tenants use the environment's provider.
- **Business code is provider-neutral.** Modules use EF Core LINQ only. No raw SQL, provider APIs or provider-specific column types in `Domain`, `Application` or `Api`. Anything that has to differ lives in a provider package in `Core` (`Core.Data.SqlServer`, `Core.Data.PostgreSql`) behind one interface. Architecture tests enforce this.
- **What the provider packages own:**

| Concern | SQL Server | PostgreSQL |
|---|---|---|
| EF Core provider | `Microsoft.EntityFrameworkCore.SqlServer` | `Npgsql.EntityFrameworkCore.PostgreSQL` |
| Guid keys | EF Core sequential GUIDs | UUIDv7 |
| Row version (concurrency) | `rowversion` | the `xmin` system column |
| Filtered unique index SQL | `[IsDeleted] = 0` | `"is_deleted" = false` |
| JSON columns | native `json` (2025 / Azure SQL) | `jsonb` |
| Money | `decimal(19,4)` | `numeric(19,4)` |
| UTC timestamps | `datetime2` | `timestamptz` (Npgsql requires `DateTimeKind.Utc`, which matches the UTC rule) |
| Atomic counter (`ISequenceService`) | `UPDATE … OUTPUT` | `UPDATE … RETURNING` |
| Distributed lock | `sp_getapplock` | `pg_advisory_xact_lock` |
| Quartz job store | SQL Server delegate | PostgreSQL delegate |
| Health check | SQL Server check | PostgreSQL check |
| Naming | PascalCase | snake_case through a naming convention, applied in one place |

- **Case sensitivity differs:** SQL Server's default collation is case-insensitive, and PostgreSQL is case-sensitive. Uniqueness and lookups on user-entered text (usernames, emails, module keys, codes) therefore always use **normalised columns** (for example ASP.NET Core Identity's `NormalizedUserName`), never the database's collation, so behaviour is identical on both.
- **Migrations per provider:** each module has one migrations project per provider (`PurchaseRequest.Migrations.SqlServer`, `PurchaseRequest.Migrations.PostgreSql`), generated from the same model. `Migrator` loads the set that matches the provider. CI runs `dotnet ef migrations has-pending-model-changes` for every module and provider, so neither set can fall behind.
- **Tests run on both:** integration tests, tenant-isolation tests and the scenario tests in 7.5 run against both providers in CI, as a matrix, using Testcontainers. A feature isn't done until it passes on both.
- **Adding a third provider later** means a new `Core.Data.*` package, a migrations project per module, and an entry in `platform.yaml`. Business modules don't change.

## 5. Platform modules

- **Identity:**
  - OpenIddict endpoints: authorization code + PKCE, `client_credentials`, refresh and token exchange
  - ASP.NET Core Identity: users, passkeys, TOTP MFA, invitations, password reset
  - machine enrollment and key rotation; service client registration
  - groups and membership; module role assignments; data-scope assignments
  - the `EffectiveAccessResolver`, `access_version` and the audit log
- **Tenancy:**
  - catalog, tenant lifecycle (4.1), plans and entitlements, per-tenant settings (company profile, timezone, currency, locale, numbering formats, branding), and the module manifest for the frontend
  - **onboarding pipeline** that's idempotent and resumable: create the database, run `Migrator` for that tenant (Tenancy never applies migrations itself), seed reference data, create the first admin invitation, enable the plan's modules
  - **tenant data export** (machine-readable, per module) and **offboarding**
  - billing is an `IBillingProvider` **interface only**
  - platform operator realm and support access (4.4)
- **Notification:** SignalR hub with a Redis backplane, scoped to tenant and user; email through an `IEmailSender` interface (SMTP adapter, Mailpit locally), with templates localized per tenant; notification history with read status.
- **Business modules:** see the reference scenario in section 7.

## 6. Frontend: micro frontends

### 6.1 Shell (`apps/shell`)
- Owns the layout (navbar and sidebar), theme, i18n, error boundaries, the 404 page, the session (from `/bff/user`) and the module loader.
- After sign-in it fetches the tenant's **module manifest** from `GET /api/tenancy/v1/me/modules`:
  ```json
  { "modules": [
    { "id": "purchase-request",   "entry": "https://cdn.example/mfe/purchase-request/1.4.0/mf-manifest.json" },
    { "id": "cost-center-budget", "entry": "https://cdn.example/mfe/cost-center-budget/1.0.2/mf-manifest.json" }
  ] }
  ```
- It registers those remotes with the Module Federation runtime API (`createInstance` and `registerRemotes`, then `loadRemote`), pointing at each module's **`mf-manifest.json`**, which is the recommended Module Federation 2.0 entry. It then builds routes and sidebar items from each module's manifest, filtered by the user's `roles`.
- Every module mounts inside its own error boundary and `Suspense`, so one failing module never breaks the app.

### 6.2 Module contract (`modules/*`)
Each module exposes exactly one entry, `./manifest`:
```ts
export default {
  id: "purchase-request",          // the module key
  basePath: "/purchase-request",   // always "/" + module key
  routes: [/* lazy route objects under basePath */],
  menu: [/* sidebar items with icon, i18n label key, path, requiredRole */],
  settings: [/* optional settings pages for the shell's Settings area */],
  extensions: [/* optional components this module contributes to other modules' slots (7.4) */],
  i18n: { namespace: "purchase-request" },
} satisfies ModuleManifest;
```
- **Modules never import other modules.** They communicate only through URLs and the typed `web-core` event bus. ESLint (`eslint-plugin-boundaries` or `dependency-cruiser`) fails the build on violations.
- Modules depend only on `ui-kit`, `web-core` and `api-clients`.
- Each module can run standalone (`pnpm dev` inside it) in a lightweight dev shell.
- **Slots:** a module can leave a named place in its UI (`<Slot name="…" props={…} />`) that other modules fill through `extensions` in their manifest. Slot names and prop types are declared in `web-core`, so neither module imports the other (7.4).

### 6.3 Shared singletons
Module Federation shares these as `singleton: true` with a `requiredVersion`, with versions read from the installed packages (pinned once in the pnpm catalog, never typed into the federation config). The list is: `react`, `react-dom`, `react-router`, `@mui/material`, `@emotion/react`, `@emotion/styled`, `zustand`, `i18next`, `react-i18next`, `@{{PRODUCT_NAME}}/web-core`, `@{{PRODUCT_NAME}}/ui-kit`.

### 6.4 `web-core`
- An HTTP client with **same-origin cookies** (no tokens in JavaScript). It adds the `X-CSRF` header, turns `ProblemDetails` into UI errors, sends `Idempotency-Key` on create requests, and redirects to `/bff/login` when the session ends.
- Session store (user and `roles` from `/bff/user`), tenant context (timezone, currency, locale, branding), `useRouteAccess` and `<RequireModuleRole module role>` guards (the same "at least this role" rule as the backend), a typed event bus, and date, number and currency formatting in the tenant's settings.
- Tenant hooks: `useTenantSettings()`, `useFeature(key)` and `<Slot>`.
- **Per-tenant theme:** the shell builds the MUI theme with `cssVariables: true` from the tenant's branding settings (primary colour, logo). Modules use theme tokens only, never hard-coded colours.
- The frontend uses `roles` **only** to show or hide menus and routes. It never decides access; action buttons follow the `allowedActions` the API returns (7.3).
- No `localhost` URLs in code. Every call is a relative `/api/{module-key}/v{n}/…` path.

### 6.5 `api-clients`
Generate typed clients from each module's OpenAPI document, using `openapi-typescript` with `openapi-fetch`, or `orval`. Regenerate in CI and fail the build if the generated code is out of date.

### 6.6 Quality, accessibility and delivery
- TypeScript strict mode, ESLint, Prettier, Vitest unit tests, and Playwright end-to-end tests in `frontend/e2e` against the composed app.
- Accessibility target: **WCAG 2.2 AA**. Run automated axe checks in Playwright.
- Every UI string goes through i18n, in `{{DEFAULT_LOCALES}}`.
- Each module builds to an immutable, versioned CDN path (`/mfe/{key}/{version}/`) served with `Cache-Control: public, max-age=31536000, immutable`. Because the shell fetches `mf-manifest.json` and chunks from the CDN across origins, the CDN must return `Access-Control-Allow-Origin` for the app's origins. Locally and in compose, the Gateway serves `/mfe/*` from a local static origin, so there's no CDN dependency. **The catalog stores only each tenant's module keys and versions; the module base URL comes from configuration**, and the manifest endpoint builds full URLs from both. Tenant manifests are `no-cache`. Changing a tenant's module version is a data change, not a rebuild, which also allows canary releases and one-line rollbacks per tenant.

## 7. Reference scenario: purchase requests, from backend to frontend

Build **one realistic scenario** across the backend and frontend, with **two demo tenants**. It teaches every pattern in this document, and especially how to make behaviour differ per tenant **without forking code**. `docs/tenant-customization.md` must walk through this scenario as the team's guide.

### 7.1 The story

- **`purchase-request`** (standard module, in every plan): staff create a request with lines (item, quantity, unit price) and attach a supplier quote, then submit it. Managers approve or reject. Requests are numbered `PR-{yyyyMMdd}-{seq:0000}`.
- **`cost-center-budget`** (add-on module, only in the Plus plan): finance sets a budget per cost center. Approved requests use up the budget, and the purchase request page shows how much is left.

| Behaviour | Tenant `demo` (Standard plan) | Tenant `demo-plus` (Plus plan, customised) |
|---|---|---|
| Auto-approve below (setting) | MYR 500 | MYR 200 |
| Approval policy (strategy) | `single`: any manager approves | `two-level`: manager, then an admin as well above MYR 5,000 |
| Cost center field (custom field) | none | required, chosen from a list |
| Draft sharing (feature flag) | off | on |
| `cost-center-budget` module | not entitled | entitled; budget panel on the request page |
| Branding (theme) | default | own primary colour and logo |

`purchase-request` must also demonstrate every platform pattern, so new modules can copy it:
- its own schema, `DbContext` and migrations
- `[ModuleRole]` checks, a finer permission ("approve needs `manager`"), and one endpoint restricted to machines with `[AllowPrincipalTypes]` (a scanner device posting goods-received confirmations)
- `ISequenceService` numbering, `ETag` concurrency and an `Idempotency-Key` on create
- the quote upload through `IFileStorage`
- `PurchaseRequestApproved.v1` published through the outbox, consumed by Notification (the requester is told) and by `cost-center-budget`
- one recurring job: a daily reminder at 09:00 **tenant local time** about requests waiting more than two days for approval. Design: one Quartz trigger fires every hour (at minute 0) and selects the tenants whose local time is now 09:00. It publishes one message per tenant, and workers handle them in parallel. Never loop over thousands of tenants serially inside the trigger. Tenants in timezones with a 30- or 45-minute offset are covered by firing every 15 minutes.
- localized validation messages

### 7.2 The customization ladder

When a tenant needs something different, use the **lowest rung that works**. Each rung appears in the scenario.

| Rung | Use it when | Mechanism | In the scenario |
|---|---|---|---|
| 1. Setting | Only a value differs | Typed per-tenant settings | Auto-approve threshold |
| 2. Feature flag | A behaviour is on or off | `IFeatureFlags` | Draft sharing |
| 3. Strategy | The logic itself differs | Keyed services, chosen by a setting | Approval policy |
| 4. Custom fields | A tenant needs extra data | Field definitions plus a JSON column | Cost center |
| 5. Event extension | Extra behaviour should react to something | A consumer in another module | Budget is used up on approval |
| 6. Slot | Extra UI inside another module's page | Frontend slot plus manifest `extensions` | Remaining-budget panel |
| 7. Tenant-only module | A whole new capability | A separate module plus plan entitlement | `cost-center-budget` |

**Rules**
- **Never branch on tenant identity.** `if (tenant.Slug == "demo-plus")` is forbidden in the backend and frontend. Name a variation by **what it does** (`two-level`), so any tenant can choose it. CI fails on comparisons against `TenantId` or `TenantSlug` in business modules. Only `Core`, Tenancy, Identity, the Gateway and `Migrator` may compare them.
- **Never fork** a module, and never keep a branch per customer.
- **Every variation has a default,** so a new tenant works with zero configuration.
- Settings, flags and strategy choices are data in the catalog. Platform operators edit them in Tenancy; tenant admins edit the ones they're allowed to in `tenant-settings`. Every change is audited.
- If a module refers to a strategy key that doesn't exist, it falls back to the default and logs a warning. `Migrator --catalog` validates all tenants' keys.

### 7.3 Backend, rung by rung

**1. Setting**
```csharp
public sealed record PurchaseRequestSettings
{
    public decimal AutoApproveBelow { get; init; } = 500m;   // default for every tenant
    public string ApprovalPolicy { get; init; } = "single";
}

// Registered once. Values come from the catalog for the current tenant (cached, validated).
services.AddTenantSettings<PurchaseRequestSettings>(Modules.PurchaseRequest);

public sealed class SubmitPurchaseRequest(ITenantSettings<PurchaseRequestSettings> settings, TimeProvider time)
{
    public void Handle(PurchaseRequest request)
    {
        request.Submit(time);
        if (request.Total < settings.Value.AutoApproveBelow)
            request.AutoApprove(time);
    }
}
```

**2. Feature flag**
```csharp
if (!await flags.IsEnabledAsync(Features.PurchaseRequest.DraftSharing))
    return Results.NotFound();   // feature off for this tenant: behave as if it doesn't exist
```
`IFeatureFlags` evaluates with the current tenant (and optionally the principal) as context. Implement it as an **OpenFeature** provider backed by the catalog, so a hosted flag service can replace it later without code changes.

**3. Strategy**
```csharp
public interface IApprovalPolicy
{
    // The next approval needed, or null when the request is fully approved.
    ApprovalStep? NextStep(PurchaseRequest request);
}

services.AddKeyedScoped<IApprovalPolicy, SingleApproverPolicy>("single");
services.AddKeyedScoped<IApprovalPolicy, TwoLevelApprovalPolicy>("two-level");
services.AddTenantStrategy<IApprovalPolicy>(
    s => s.Get<PurchaseRequestSettings>().ApprovalPolicy,   // which key this tenant uses
    defaultKey: "single");

public sealed class ApprovePurchaseRequest(ITenantStrategy<IApprovalPolicy> policy)
{
    public void Handle(PurchaseRequest request, Principal approver)
    {
        var step = policy.Current.NextStep(request)
                   ?? throw new BusinessRuleException("Request is already fully approved.");
        request.Approve(step, approver);
    }
}
```
`ITenantStrategy<T>` (in `Core`) uses .NET keyed services and resolves `T` from the key in the current tenant's settings.

**4. Custom fields**
- A `CustomFieldDefinition` table in the module's schema: entity type, key, label (i18n key), type (text, number, date or list), required, list options and display order.
- Values live in one JSON column, `CustomFields`, on the entity: mapped with EF Core's JSON column support, which becomes SQL Server's native `json` type (SQL Server 2025 and Azure SQL) or PostgreSQL's `jsonb`. The **server** validates values against the definitions on every write. If a field needs filtering at scale, add a computed column with an index.
- API: `GET /api/purchase-request/v1/custom-fields?entity=purchase-request` returns the definitions, and entities carry `"customFields": { "cost-center": "CC-100" }`.

**5. Event extension**
```csharp
// Lives in the cost-center-budget module. purchase-request knows nothing about it.
public sealed class UseBudgetOnApproval(BudgetDbContext db) : IEventConsumer<PurchaseRequestApproved.V1>
{
    public async Task HandleAsync(PurchaseRequestApproved.V1 e, CancellationToken ct)
    {
        // Idempotent through the inbox (4.8): a duplicate delivery doesn't use the budget twice.
    }
}
```
`Core` delivers an event to a module's consumers only for tenants entitled to that module.

**Server-driven actions:** every purchase request in the API includes `"allowedActions": ["approve", "reject"]`. The backend works these out from the principal's role, the request's state and the tenant's approval policy. The frontend shows buttons from this list and **never re-implements approval rules**.

**6. Slot:** rung 6 is frontend-only; see 7.4.

**7. Tenant-only module:** `cost-center-budget` is an ordinary module with its own schema, `DbContext`, migrations and endpoints protected by `[ModuleRole(Modules.CostCenterBudget, Role.Manager)]`. It's simply entitled only in the Plus plan. For `demo`, its endpoints return 403 and its events aren't delivered.

### 7.4 Frontend: a standard module and a tenant-only module

**Standard module `modules/purchase-request`**
- Pages: list, create or edit form, and detail with an approval timeline.
- The form renders the standard fields plus `<CustomFieldsSection entity="purchase-request" />` from `ui-kit`. That section is driven by the definitions API, so there's no tenant-specific code in the frontend.
- Approve and reject buttons come from `allowedActions`.
- `useFeature("purchase-request.draft-sharing")` shows or hides "Share draft".
- The detail page leaves a slot for other modules:
  ```tsx
  <Slot name="purchase-request.detail.side-panel"
        props={{ requestId: request.id, costCenter: request.customFields["cost-center"], total: request.total }} />
  ```

**Slot contract (in `web-core`, the only thing both modules share)**
```ts
export interface SlotProps {
  "purchase-request.detail.side-panel": { requestId: string; costCenter?: string; total: Money };
}
```

**Tenant-only module `modules/cost-center-budget`**
```ts
export default {
  id: "cost-center-budget",
  basePath: "/cost-center-budget",
  routes: [{ path: "", lazy: () => import("./pages/BudgetList") }],
  menu: [{ label: "costCenterBudget.menu", path: "", icon: "Savings", requiredRole: "manager" }],
  extensions: [
    {
      slot: "purchase-request.detail.side-panel",
      component: () => import("./slots/RemainingBudget"),
      requiredRole: "user",
    },
  ],
  i18n: { namespace: "cost-center-budget" },
} satisfies ModuleManifest;
```

**What happens at runtime**
```
 demo user                                   demo-plus user
 ─────────                                   ──────────────
 GET /api/tenancy/v1/me/modules              GET /api/tenancy/v1/me/modules
   → [purchase-request]                        → [purchase-request, cost-center-budget]
 Shell registers 1 remote                    Shell registers 2 remotes
 Sidebar: Purchase Requests                  Sidebar: Purchase Requests, Cost Center Budget
 Request detail page:                        Request detail page:
   <Slot side-panel> renders nothing           <Slot side-panel> lazy-loads RemainingBudget
                                               from the cost-center-budget remote,
                                               inside its own error boundary
 Form: no cost center field                  Form: cost center (required, from definitions API)
 Theme: default                              Theme: demo-plus primary colour and logo
```
- Neither module imports the other. Removing `cost-center-budget` from a tenant's plan makes its menu and panel disappear at the next sign-in, with no rebuild.
- **Replacing another module's whole page for one tenant isn't supported.** If it's ever needed, it requires an ADR first.

### 7.5 Scenario tests (required)
- **Backend:**
  - MYR 300 is auto-approved in `demo` but needs approval in `demo-plus`.
  - MYR 6,000 needs one approval in `demo` and two in `demo-plus`.
  - Cost center is required only in `demo-plus`, and an invalid value is rejected by the server.
  - Draft sharing returns 404 in `demo`.
  - `cost-center-budget` endpoints return 403 for `demo`.
  - Approving in `demo-plus` uses up the budget exactly once, even when the event is delivered twice.
  - `allowedActions` matches the role and policy.
- **Frontend (Playwright):**
  - A `demo` user sees no budget menu, panel or cost center field.
  - A `demo-plus` user sees all three.
  - Buttons follow `allowedActions`.
  - The theme colour differs per tenant.
  - A failing `cost-center-budget` remote leaves the purchase request page working.
- **Architecture:**
  - `PurchaseRequest` and `CostCenterBudget` have no references to each other, in backend projects or frontend packages.
  - No business module compares against `TenantId` or `TenantSlug` (only `Core`, Tenancy, Identity, the Gateway and `Migrator` may).

## 8. Local development

- **Aspire AppHost** starts everything: the database selected for local dev (SQL Server 2025 or PostgreSQL 18, from configuration), RabbitMQ, Valkey, Azurite (or the S3 emulator for `aws`), Mailpit, all hosts, the Gateway, the Scheduler and the frontend dev servers, with the Aspire dashboard for logs, traces and metrics.
- `deploy/docker-compose.yml` does the same without Aspire. It's **produced from the AppHost with `aspire publish`** (Aspire's Docker Compose publisher), not by a separate generator, so the AppHost (itself generated from `platform.yaml`) is its only source. **Only the Gateway and dev UIs (Mailpit, the Aspire dashboard) are published to the host.** Containers run in UTC.
- **HTTPS locally:** `__Host-` cookies need HTTPS, so local development uses HTTPS for the Gateway with a locally trusted certificate (for example from mkcert) covering `localhost`, `demo.localhost` and `demo-plus.localhost`. Onboarding a new local tenant adds its hostname to the certificate through `scripts/dev-up`.
- **Database images:** pin `mcr.microsoft.com/mssql/server` (SQL Server 2025) and `postgres` (18). Switching the local provider is one configuration value.
- **SQL Server on Apple Silicon:** On Apple Silicon it runs under Docker Desktop's Rosetta emulation. **Don't use Azure SQL Edge**: it was retired on 30 September 2025.
- `scripts/dev-up` starts the stack, runs `Migrator --catalog`, onboards the two demo tenants (`demo` on the Standard plan, `demo-plus` on the Plus plan with the customisations in 7.1), seeds a few purchase requests, cost centers and budgets, and prints each tenant's admin invitation link.
- `.env.example` is **generated** from the configuration schema (2.1) and lists every key with no values. `.env` is git-ignored. Developers use `dotnet user-secrets` or `.env` locally; nothing secret is committed.

## 9. CI/CD and supply chain

- One workflow per service and frontend module, **generated from `platform.yaml`**, with path filters. Each runs build, tests, analyzers (warnings as errors), architecture tests, the generated-client freshness check, and the single-source-of-truth drift check (2.1).
- **Security in CI:**
  - CodeQL (code scanning)
  - GitHub secret scanning with push protection
  - Dependabot or Renovate
  - `dotnet list package --vulnerable` and `pnpm audit`
  - a **license check** that enforces the license policy in section 1
  - container image scanning (Trivy or similar)
- **Build integrity:** Dockerfiles are multi-stage with pinned base image digests, run as a non-root user, and include a health check. Each build produces an SBOM (CycloneDX), and images get signed build provenance (GitHub artifact attestations or cosign).
- **Deploy order:** run `Migrator` (expand-only migrations), then roll out hosts, then publish frontend modules, then update tenant manifests. Deployment targets are placeholders; don't implement cloud infrastructure-as-code.

## 10. Operations (documentation and placeholders)

`docs/runbooks/` must cover:
- **Backup and restore:** point-in-time restore per tenant database (Azure SQL or RDS automated backups), restoring one tenant without touching others, and target RPO and RTO.
- **Key rotation:** token signing keys, machine keys, service secrets.
- **Moving a tenant** between servers or pools, and **data residency** (pinning a tenant to a region).
- **Offboarding:** export, grace period, deletion, confirmation.
- **Incident response:** where logs and traces are, how to find all activity for one tenant or principal, and how to revoke a principal immediately.

## 11. Anti-patterns the template must make impossible or obvious

These come from real problems in a previous system:

| Don't | Do instead |
|---|---|
| Controllers without `[Authorize]` reachable anonymously | Fallback policy requires authentication |
| One shared JWT secret copied into every service (anyone with repo access can forge an admin token) | ES256 private key only in Identity, from the secret store; services validate with published public keys |
| Tokens readable by browser JavaScript (`localStorage`) | BFF with an `HttpOnly` session cookie (RFC 10017) |
| GUIDs in role claims (`29e97cc3-…:Manager`) | Module keys: `purchase-request:manager` |
| Integrations and devices signing in with a person's username and password | Machine and service principals with `client_credentials` |
| Secrets in `appsettings.json`, a committed `.env`, or library license keys in config | Secret store or managed identity; `.env.example` only |
| Hardcoded default or reset passwords | Invitation and reset-token flows |
| One shared `DbContext` holding every module's entities, used by all services | Each module owns its schema and `DbContext`; no cross-module tables or foreign keys |
| Migrations owned and applied by one API service (for example the auth service) at startup | A dedicated `Migrator`, run by the pipeline before deployment, with expand and contract |
| `Guid.NewGuid()` in entities, or `Guid.CreateVersion7()` as SQL Server clustered keys | Keys generated by the EF Core provider: sequential GUIDs on SQL Server, UUIDv7 on PostgreSQL |
| SQL written for one database (`UPDLOCK`, `[brackets]`, `ISJSON`, `bit` literals) inside business code | Provider-neutral EF Core; provider-specific SQL only in `Core`'s provider packages, tested on both |
| `DateTime.Now`, which breaks in UTC containers | `TimeProvider` and `ITenantClock`; banned-API analyzer |
| `count + 1` document numbers, which race | `ISequenceService` |
| Files on the container's local disk | `IFileStorage`, object storage, signed URLs |
| A server opening TCP connections to devices on a customer's LAN (for example label printers) | A machine principal: a local agent that connects outward |
| Customer-specific data in seed code | Reference data only; tenant data comes through onboarding |
| A single-company settings table | Per-tenant settings in the catalog |
| Background jobs inside every API instance | One clustered Quartz `Scheduler`, looping per tenant |
| Liveness checks that test the database | Liveness checks the process only; readiness checks dependencies |
| Hardcoded CORS origins or `localhost` API URLs | Same-origin BFF; relative `/api/…` paths |
| Infrastructure ports (database, broker, admin UIs) exposed publicly | Private network; only the Gateway is public |
| Frontend modules importing each other | Module manifest contract, URLs, slots and the event bus, enforced by lint |
| `if (tenant == "farm360")` in code, or a branch or fork per customer | The customization ladder (7.2): settings, flags, strategies, custom fields, events, slots, tenant-only modules |
| Frontend re-implementing business rules to decide which buttons to show | Server-driven `allowedActions` |
| The same fact typed in several places (module list in routes, AppHost, compose, CI and constants; API types written by hand in the frontend; per-environment config files; per-environment frontend builds) | Single source of truth (2.1): `platform.yaml`, the config schema, OpenAPI, and `scripts/generate` with a CI drift check |
| Commercial-license libraries added without anyone deciding to | License policy and a CI license check |

## 12. Build order

Build in these milestones. **Each must build, pass its tests and pass the drift check before you start the next.** Don't scaffold everything at once and fix it later.

1. **Foundation:** `platform.yaml`, `scripts/generate`, `Directory.*.props`, banned APIs, `Core` (tenancy, data with **both database providers** from the start, time, errors, observability, health), `Architecture.Tests`, `Migrator`, AppHost and compose.
2. **Identity and access:** Tenancy (catalog, lifecycle, onboarding), Identity (OpenIddict, users, machines, services, groups, `EffectiveAccessResolver`), the Gateway as BFF, token validation in `Core`, and their tests.
3. **Standard module, backend:** `purchase-request` with every platform pattern (7.1), messaging (outbox and inbox), Scheduler, files, and Notification.
4. **Frontend foundation and standard module:** shell, `web-core`, `ui-kit`, `api-clients`, and `modules/purchase-request`, plus Playwright.
5. **Tenant customization:** the two demo tenants, rungs 1 to 6, and `cost-center-budget` (backend and frontend, rung 7), plus the scenario tests in 7.5.
6. **Delivery and docs:** CI workflows, supply-chain checks, runbooks, ADRs, `README.md`, and `docs/deviations.md`.

## 13. Deliverables and acceptance criteria

1. Everything builds with zero warnings. `scripts/dev-up` (or the Aspire AppHost) starts the stack, and both demo tenants' admins can accept their invitations, sign in, and use the scenario end to end, seeing the differences in 7.1.
2. The same modules also run correctly in `AllInOne.Host`, and the whole stack runs on **both SQL Server and PostgreSQL** by changing only `Database:Provider`.
3. `dotnet test` passes, including:
   - **Tenant isolation:** tenant A cannot read or change tenant B's data, files, cache entries, SignalR messages or events.
   - **`EffectiveAccessResolver`:** direct only; group only; direct and group with the highest winning (the Ali example); several groups; inactive group or membership ignored; non-entitled module removed.
   - Rejecting a second role for the same principal or group in the same module.
   - Users, machines and services each getting a token, and being allowed or refused by `[ModuleRole]` according to their effective role.
   - A delegated token (user on a machine) authorized with the user's roles, with both recorded in the audit log.
   - `X-Tenant` refused from anything except a platform service principal.
   - **Token validation:** tampered (edited `roles`), `alg: none`, HMAC-signed, expired, wrong-audience, wrong-`typ` and other-tenant tokens all rejected.
   - A stale `access_version` returning 401.
   - BFF: no token ever reaches the browser; state-changing requests without `X-CSRF` rejected.
   - Module key validation and the 4 KB token size limit.
   - `ISequenceService` producing no duplicates under concurrent requests.
   - `Idempotency-Key` returning the original result on retry; `If-Match` conflicts returning 412.
   - Outbox and inbox: an event published exactly once from the business transaction and handled once even if delivered twice.
   - All scenario tests in 7.5.
   - **Single source of truth:** `scripts/generate` followed by `git diff --exit-code` passes; no hard-coded module keys, role names, flag keys, `/api/` paths or colour values outside their sources; every key in `.env.example` exists in the configuration schema, and every schema key is in `.env.example`.
   - **Architecture tests:** no module references another module's internals; only `Migrator` references migration APIs; no `DbContext` maps another module's schema; banned APIs are absent.
4. Frontend: typecheck, lint (including boundary rules), unit tests and Playwright end-to-end tests (the scenario tests in 7.5, plus a role-restricted menu hidden for a `user`) pass, with axe accessibility checks.
5. `scripts/new-module <key>` adds the service and module entries to `platform.yaml`, runs `scripts/generate` (constants, gateway route, AppHost, compose, CI workflow, seed data), and instantiates the **clean module templates** in `templates/`: a `dotnet new` template (module, host, migrations for every supported database provider, tests) and a frontend module template. CI instantiates both templates and builds and tests the result, so they never rot. `purchase-request` stays as the worked example, not as something to copy and strip. No other file needs a manual edit. `docs/adding-a-module.md` explains it.
6. `scripts/onboard-tenant <slug>` creates a fully isolated second tenant. `docs/onboarding-a-tenant.md` explains it.
7. `docs/tenant-customization.md` explains the customization ladder (7.2) using the scenario, with a short "which rung do I need?" decision guide.
8. `docs/adr/` has one short ADR per principle in section 2, plus ADRs for: single source of truth and `scripts/generate`, supporting SQL Server and PostgreSQL, the messaging layer over `RabbitMQ.Client`, Valkey and Redis, the S3 emulator, per-service token audiences, tenant-aware OpenIddict stores, Rsbuild and MUI (and why not Vite or Tailwind), the customization ladder, the BFF, OpenIddict, quorum queues and the outbox, and the license policy. Each states the decision and the alternative rejected. For example, a row-level `TenantId` was rejected in favour of a database per tenant; one repo per micro frontend was rejected in favour of a monorepo with Module Federation; tokens in the browser were rejected in favour of a BFF.
9. Root `README.md` covers the architecture diagram, prerequisites, quick start, and how to extract a module or frontend module into its own repo.
10. `docs/deviations.md` lists every place the scaffold differs from this prompt, and why. It must be empty, or every entry justified.

**Out of scope:** real billing, business features beyond the reference scenario, cloud infrastructure-as-code, and mobile apps. Leave clearly marked interfaces or placeholders for these.

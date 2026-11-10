# Adding a module

A module is a vertical slice with one key, used by its backend part and its frontend part: `goods-receipt` is `GoodsReceipt` in the
backend (a class library, a thin host, migrations for every database provider, tests, contracts for other modules) and `frontend/modules/goods-receipt` in the
browser. One command creates all of it from the clean templates in `templates/`:

```sh
scripts/new-module.sh goods-receipt --name "Goods Receipt"       # macOS and Linux
scripts/new-module.ps1 goods-receipt --name "Goods Receipt"      # Windows
```

The other options are `--from-plan <plan>` (the lowest plan that includes the module; every plan from there upward gets it, and without it the module is in every plan), `--no-frontend` (backend only), `--skip-compose` (leave `deploy/docker-compose.yml` as it is), `--resume` (the module is already in `platform.yaml`: create whatever of it is missing, its contracts folder included, and finish the remaining steps) and `--no-catalog-migration` (you make the catalog's migration yourself). It needs Docker only to run the tests afterwards. What it does, in order:

1. Adds the service and the module to `platform.yaml` (the only file you would otherwise edit by hand), with the next free port.
2. Creates the backend with `dotnet new saaskit-module`: `backend/src/Modules/GoodsReceipt` (Domain, Application, Infrastructure, Api),
   `backend/src/Hosts/GoodsReceipt.Host` and `backend/tests/GoodsReceipt.Tests`, and its contracts, `backend/src/Contracts/Modules/GoodsReceipt`.
3. Creates `frontend/modules/goods-receipt` from `templates/frontend-module`.
4. Runs the generator (`scripts/generate.sh`, `scripts/generate.ps1` on Windows): the constants (`Modules.GoodsReceipt`, `PlatformServices.GoodsReceipt`), the Gateway route, the AppHost
   resource, the compose file, the migrations projects,
   `AllInOne.Host`'s registration, the OpenAPI document, the typed API client and the TypeScript types.
5. Creates the module's first migration for every provider in `platform.yaml` (SQL Server), and the catalog migration (the catalog seeds the list of modules and
   plans, which just changed).
6. Installs the frontend dependencies.

Nothing else needs a manual edit. Then:

```sh
dotnet test --project backend/tests/GoodsReceipt.Tests      # every provider in platform.yaml; creates, lists, renames, deletes, isolates tenants
cd frontend && pnpm exec turbo run typecheck lint test build --filter=@saaskit/mfe-goods-receipt...
scripts/dev-up.sh                                           # the module appears in the stack, in the menu of tenants that have it (dev-up.ps1 on Windows)
```

## What you get, and what to replace

The template is a small, complete example (an `Item` with a name) that follows every platform rule, so you replace its domain and keep
its shape:

| Where | What it shows |
|---|---|
| `Domain/` | An aggregate root (`IAggregateRoot`: ETags), soft delete, no dependencies |
| `Application/` | Use cases, the port to the store, the module's **error catalog** (stable codes with a status and a message key) |
| `Infrastructure/` | The module's own `DbContext` and schema; repositories; no other module reads these tables |
| `Api/` | `RequireModuleRole` on every endpoint, `Idempotency-Key` on create, `If-Match` on changes, paging |
| `Resources/Messages.*.json` | Messages in every locale from `platform.yaml` |
| `Contracts/Modules/GoodsReceipt` | An event, a view record and a typed client, for other modules (see below) |
| `Host/Program.cs` | About 10 lines: `AddPlatform(...)`, `AddModule<...>()`, `UsePlatform()` |
| `Tests` | Domain unit tests, and integration tests of the module hosted exactly as in production on every provider in `platform.yaml`, including tenant isolation |

## Rules the build enforces (so you don't have to remember them)

- A module references `Core` and `Contracts` only; never another module. (`ModuleBoundaryTests`)
- `Domain` depends on nothing, `Application` on `Domain`, `Api` and `Infrastructure` on `Application`. (`ModuleBoundaryTests`)
- No `DbContext` maps another module's schema; only the Migrator references migrations. (`DataOwnershipTests`)
- No hard-coded module keys, role names, flag keys or `/api/` paths; no comparing `TenantId` or `TenantSlug`. (`SingleSourceOfTruthTests`)
- A migration exists for every model change, per provider. (`MigrationsConsistencyTests`)
- No spatial type (`NetTopologySuite`) in `Api` or `Contracts`. (`ModuleBoundaryTests`)
- The frontend module imports only `ui-kit`, `web-core` and `api-clients`, never another module. (ESLint boundaries, with tests)

## Contracts: what other modules may use

A module may reference `Core` and `Contracts` only, never another module. So what one module offers the others is in
`backend/src/Contracts/Modules/GoodsReceipt` (namespace `Saaskit.Contracts.GoodsReceipt`), in three files that open with a short note on what belongs there. The
template's example uses all three, so the pattern works from the start; replace the examples with your own.

| File | What it holds | The rule |
|---|---|---|
| `GoodsReceiptEvents.cs` | Integration events, such as `ItemCreated.V1` with `[IntegrationEvent("goods-receipt.item-created", 1)]` | The name starts with the module key (`ContractsTests` checks it). A published `V1` never changes except for new optional fields; a breaking change is a new `V2`, published next to it until every consumer has moved. |
| `GoodsReceiptViews.cs` | The records the module's API returns, such as `ItemView`; the OpenAPI document and the frontend's typed client use them too | Only what another module may rely on. Add optional fields; never rename or remove one. |
| `GoodsReceiptClient.cs` | `IGoodsReceiptClient` and its implementation, which calls the module's HTTP API | Reads and commands other modules truly need. It uses only the framework, so Contracts still references nothing. |

To publish, the module registers its outbox (`services.AddModuleOutbox<GoodsReceiptDbContext>(Service.Schema)` in `Register`, already there in the template) and calls
`IEventPublisher.Publish(db, event)` before the `SaveChanges` that stores the data, so both commit together or not at all and a relay sends the event after the commit.
The outbox table is part of every module's `DbContext`, so there is no migration to add.

Another module uses them like this:

```csharp
// In the consuming module's Register: call it (adds the base address and the right token) ...
services.AddPlatformServiceClient<IGoodsReceiptClient, GoodsReceiptClient>(PlatformServices.GoodsReceipt);
// ... and/or react to it (the consumer is an IEventConsumer<ItemCreated.V1>; its inbox lives in the consumer's own DbContext).
services.AddEventConsumer<ItemCreated.V1, ItemCreatedConsumer, MyDbContext>(Service, Modules.MyModule);
```

The last argument of `AddEventConsumer` is the module the consumer itself belongs to (`Modules.MyModule`, the consuming module): it only receives events for tenants that have that module.
The platform's own types, such as `NotificationRequested` and `TenantDayStarted`, are in `backend/src/Contracts` itself.

## Changing the data model

A table's single-column primary key is always named after its table plus `Id` (`Suppliers` gets `SupplierId`), whatever the C# property is called: `PlatformDbContext` applies this to every table, so don't set the column name yourself.

`scripts/new-module.sh` creates the first migration only. After you change an entity or the `DbContext`, add a migration
(`MigrationsConsistencyTests` fails otherwise). From `backend/`:

```sh
dotnet tool restore
dotnet ef migrations add AddSupplier -p src/Modules/GoodsReceipt.Migrations.SqlServer -s src/Modules/GoodsReceipt.Migrations.SqlServer -c GoodsReceiptDbContext -o Migrations/GoodsReceipt
```

`platform.yaml` lists only `sqlserver` (PostgreSQL is not supported in this release, see [ADR 0011](adr/0011-sql-server-and-postgresql.md)), so
that is the only migration to add. Never write or edit a migration by hand.

Migrations are expand-only: add columns and tables, never rename or drop in the same release. The Migrator applies them to every tenant
database (`scripts/dev-up.sh` runs it for you).

## Storing locations (spatial)

Spatial support is switched on for the whole platform in `Core.Data.*` (see [ADR 0024](adr/0024-spatial-types.md)). A module that stores a
location only adds the type library to its `.csproj`, with no version: the version is in `Directory.Packages.props`.

```xml
<PackageReference Include="NetTopologySuite" />
```

The column is `geography` on SQL Server. Distances and `IsWithinDistance` are in metres. The rules:

- **Create points as `new Point(longitude, latitude) { SRID = 4326 }`.** X is the longitude, so the order is longitude first.
- **Never put `Point` (or any `Geometry`) in a request or response.** It serializes with circular properties, which breaks the OpenAPI
  document and the generated TypeScript client. Use `Latitude` and `Longitude` numbers, and convert in the `Application` layer. `Domain`
  may use `Point`.
- **Validate the numbers:** latitude in [-90, 90], longitude in [-180, 180]. Reject anything else with a validation error before you build
  the point.

## The frontend module's toolkit

Everything a screen needs comes from `@saaskit/web-core` (logic) and `@saaskit/ui-kit` (components). The shell already renders the
query cache, the snackbar and the dialog host, so a module only calls the hooks.

| Need | Use | Notes |
|---|---|---|
| Load data | `useApiQuery(key, (signal) => unwrap(client.GET(...)))` | Cached under `key`, shared with every module (TanStack Query, ADR 0025). Typed by the OpenAPI document. |
| Change data | `useApiMutation(fn, { invalidate, success })` | Refreshes `invalidate` and shows `success`. A failure shows the server's localized message, except field errors, which the form shows (`error.forField(name)`). |
| Reference ("master") data | `useMaster("farm", "/api/farm/v1/farms")` and `<MasterSelect master={...} />` | Any service's list endpoint (array or `Page<T>`), fetched once for the whole app; `options`, `byId`, `labelOf`. After changing a master, invalidate `masterKey("farm")`. |
| Tell the person something | `useSnackbar().success(...)` / `info` / `warning` / `error` / `fromError(e)` | Callable outside React too (`snackbar`). |
| Ask first | `if (await useDialog().confirm({ title, tone: "danger" })) ...` | `alert({ title, text })` for a message that needs an OK. One dialog at a time. |
| A small form in a dialog | `<FormDialog>` | Enter submits; fields are the children. |
| Module settings | Nothing for the values; `settings: [{ id, label, path, requiredRole, lazy }]` in `manifest.ts` for pages of your own | The shell adds **Settings** to the module's part of the sidebar (`/{module}/settings`). Its General tab is built from the module's `[TenantSettings]` record; your pages are further tabs. Managers of the module read them, its admins change them (a tenant-settings admin may too). The top bar gear is for platform settings only. Don't declare a `settings` route yourself: the path is the shell's. |

In tests, wrap components that use these hooks in `TestProviders` from `@saaskit/web-core/testing`. The template's `ItemList` shows a
list, an add and a confirm-then-delete.

## What to look at next

- **Plans and entitlement**: `platform.yaml` lists the plans that include the module. Say the lowest plan that has it and every plan above
  gets it too: `scripts/new-module.sh goods-receipt --from-plan advanced` writes `plans: [advanced, complete]` (the first-run
  generator takes `--modules goods-receipt:advanced`). Without a plan the module is in every plan. A module only some plans have is an
  *add-on*. The platform's own modules (access management, tenant settings, billing, feedback and base) are in every plan and the operator
  console doesn't let anyone take them out of one; `platform.yaml` fails validation if one is missing from a plan.
- **Events**: see [Contracts](#contracts-what-other-modules-may-use) below. `docs/events.md` is generated.
- **Per-tenant variation**: see [tenant-customization.md](tenant-customization.md). Start with a setting; do not branch on the tenant.
- **A page in another module's screen**: declare a slot in `web-core` and contribute with `extensions` in the manifest.

## Taking a module out again

Remove its lines from `platform.yaml`, delete its folders (and the catalog migration that added it is kept: migrations are
expand-only), and run `scripts/generate.sh`.

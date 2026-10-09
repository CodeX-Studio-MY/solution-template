# Adding a module

A module is a vertical slice with one key, used by its backend part and its frontend part: `goods-receipt` is `GoodsReceipt` in the
backend (a class library, a thin host, migrations for every database provider, tests) and `frontend/modules/goods-receipt` in the
browser. One command creates all of it from the clean templates in `templates/`:

```sh
scripts/new-module goods-receipt --name "Goods Receipt"
```

(`scripts/new-module.ps1` on Windows.) It needs Docker only to run the tests afterwards. What it does, in order:

1. Adds the service and the module to `platform.yaml` (the only file you would otherwise edit by hand), with the next free port.
2. Creates the backend with `dotnet new acme-module`: `backend/src/Modules/GoodsReceipt` (Domain, Application, Infrastructure, Api),
   `backend/src/Hosts/GoodsReceipt.Host` and `backend/tests/GoodsReceipt.Tests`.
3. Creates `frontend/modules/goods-receipt` from `templates/frontend-module`.
4. Runs `scripts/generate`: the constants (`Modules.GoodsReceipt`, `PlatformServices.GoodsReceipt`), the Gateway route, the AppHost
   resource, the compose file, the CI workflows (`service-goods-receipt.yml`, `frontend-goods-receipt.yml`), the migrations projects,
   `AllInOne.Host`'s registration, the OpenAPI document, the typed API client and the TypeScript types.
5. Creates the module's first migration for **every** provider, and the catalog migration (the catalog seeds the list of modules and
   plans, which just changed).
6. Installs the frontend dependencies.

Nothing else needs a manual edit. Then:

```sh
dotnet test --project backend/tests/GoodsReceipt.Tests      # both databases; creates, lists, renames, deletes, isolates tenants
cd frontend && pnpm exec turbo run typecheck lint test build --filter=@acme/mfe-goods-receipt...
scripts/dev-up                                              # the module appears in the stack, in the menu of tenants that have it
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
| `Host/Program.cs` | About 10 lines: `AddPlatform(...)`, `AddModule<...>()`, `UsePlatform()` |
| `Tests` | Domain unit tests, and integration tests of the module hosted exactly as in production on **both** providers, including tenant isolation |

## Rules the build enforces (so you don't have to remember them)

- A module references `Core` and `Contracts` only; never another module. (`ModuleBoundaryTests`)
- `Domain` depends on nothing, `Application` on `Domain`, `Api` and `Infrastructure` on `Application`. (`ModuleBoundaryTests`)
- No `DbContext` maps another module's schema; only the Migrator references migrations. (`DataOwnershipTests`)
- No hard-coded module keys, role names, flag keys or `/api/` paths; no comparing `TenantId` or `TenantSlug`. (`SingleSourceOfTruthTests`)
- A migration exists for every model change, per provider. (`MigrationsConsistencyTests`)
- The frontend module imports only `ui-kit`, `web-core` and `api-clients`, never another module. (ESLint boundaries, with tests)

## Which parts of the specification you will want next

- **Plans and entitlement**: `platform.yaml` lists the plans that include the module (the default is all of them). A module only some
  plans have is an *add-on* (see `cost-center-budget`).
- **Events**: declare them in `backend/src/Contracts` with `[IntegrationEvent("{module-key}.{what-happened}", 1)]`, publish with
  `IEventPublisher` inside your transaction, consume with `AddEventConsumer`. `docs/events.md` is generated.
- **Per-tenant variation**: see [tenant-customization.md](tenant-customization.md). Start with a setting; do not branch on the tenant.
- **A page in another module's screen**: declare a slot in `web-core` and contribute with `extensions` in the manifest.

## Taking a module out again

Remove its lines from `platform.yaml`, delete its folders (and the catalog migration that added it is kept: migrations are
expand-only), and run `scripts/generate`.

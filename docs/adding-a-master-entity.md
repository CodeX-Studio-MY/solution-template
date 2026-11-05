# Adding a master entity

Master data is the reference list the rest of a product points at: farms, crops, statuses, suppliers. The scaffold gives every product a
module for it, **Base** (`base`, in every plan). It ships empty: its migration holds only the platform's own tables, and its menu entry
opens an empty page. This guide adds the first master entity, `MsFarm`, from the database to the screen. Every other master (`MsCrop`,
`MsStatus`, `MsFarmBlock`) is the same steps again.

A product that is not scaffolded yet names the module the same way as the rest: in a product called Aviot it is `Aviot.Base`, the
context is `BaseDbContext`, and `Modules.Base` is the key. Replace `Saaskit` below with your product's name.

## The naming rule

| What | Rule | `MsFarm` |
|---|---|---|
| Entity (C# class) | `Ms` + a singular name | `MsFarm` |
| Table | the entity's name, singular | `MsFarm` |
| Primary key column | the table's name + `Id` | `MsFarmId` |
| C# key property | `Id`, from `BaseEntity` | `farm.Id` |

You do not map the key. `PlatformDbContext` names every table's single-column primary key after the table, and a table that starts with
`Ms` and an uppercase letter is never made singular, so `MsStatus` gets `MsStatusId` (not `MsStatuId`) and `MsFarmBlock` gets
`MsFarmBlockId`. Never call `HasColumnName` on a key. (`Single_column_primary_keys_are_named_after_their_table` fails if a key is
named otherwise.)

## 1. The entity

`backend/src/Modules/Base/Domain/MsFarm.cs`. Domain depends on nothing. An aggregate root (`IAggregateRoot`) gets an ETag, so changes
can be checked with `If-Match`; `ISoftDeletable` keeps deleted rows for the audit trail and lets the name be reused.

```csharp
using Saaskit.Core.Data;

namespace Saaskit.Base.Domain;

public sealed class MsFarm : BaseEntity, IAggregateRoot, ISoftDeletable
{
    public const int MaxNameLength = 200;

    private MsFarm()
    {
    }

    public MsFarm(string name) => Name = name.Trim();

    public string Name { get; private set; } = "";

    public bool IsDeleted { get; private set; }

    public DateTime? DeletedOn { get; private set; }

    public void Rename(string name) => Name = name.Trim();

    public void MarkDeleted(DateTime utcNow)
    {
        IsDeleted = true;
        DeletedOn = utcNow;
    }
}
```

## 2. The table

Add a `DbSet` to `BaseDbContext` and map the entity. The table name is the entity's name, `MsFarm`, set by hand because EF's default
would be the plural `MsFarms`:

```csharp
public DbSet<MsFarm> MsFarms => Set<MsFarm>();

protected override void ConfigureModel(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<MsFarm>(farm =>
    {
        farm.ToTable("MsFarm");
        farm.Property(f => f.Name).HasMaxLength(MsFarm.MaxNameLength);
        farm.HasIndex(f => f.Name).IsUniqueAmongNotDeleted();   // two live farms can't share a name; a deleted one doesn't count
    });
}
```

The table lives in the `base` schema of each tenant's database. No other module reads it: they ask Base's API, or hold the farm's ID.

## 3. The service

`Application/MsFarmService.cs`: a port to the store (`IMsFarmRepository`), the use cases, and the module's error catalog. Endpoints only
translate HTTP; the rules (validation, "not found") live here. Follow the shape of the module template (`templates/backend-module`):
`ListAsync`, `GetAsync`, `CreateAsync`, `RenameAsync`, `DeleteAsync`, with `ApplyIfMatch` before every change.

```csharp
[ErrorCatalog(Modules.Base)]
public static class BaseErrors
{
    public static readonly ErrorCode FarmNotFound = new("base.farm-not-found", 404, "errors.base.farmNotFound");
}
```

Add the message to `Resources/Messages.en.json`, `.ms.json` and `.zh.json` (`"errors.base.farmNotFound": "..."`), in every locale in
`platform.yaml`. Register the service and the repository in `BaseModule.Register`:

```csharp
services.AddScoped<IMsFarmRepository, MsFarmRepository>();
services.AddScoped<MsFarmService>();
```

## 4. The endpoints

`Api/BaseEndpoints.cs`. Every endpoint says the least role in Base that may call it, with `RequireModuleRole`. Reading a master is
usually `user`, because every screen that picks a farm needs it. Writing is `manager`, and deleting `admin`; choose what your product needs.

```csharp
var farms = api.MapGroup("/v1/farms").WithTags("farms");

farms.MapGet("", (MsFarmService service, [AsParameters] PageRequest page, CancellationToken ct) => service.ListAsync(page, ct))
    .RequireModuleRole(Modules.Base, Role.User);

farms.MapPost("", async (SaveFarmInput body, MsFarmService service, HttpResponse response, CancellationToken ct) =>
{
    var created = await service.CreateAsync(body, ct);
    response.Headers.ETag = created.ETag;
    return Results.Created($"{PlatformServices.Base.RoutePrefix}/v1/farms/{created.Id}", created);
}).RequireModuleRole(Modules.Base, Role.Manager).RequireIdempotency<BaseDbContext>().Produces<MsFarmDto>(StatusCodes.Status201Created);
```

Add `PUT /{id}` (`If-Match`) and `DELETE /{id}` the same way. Never write a role name or a module key as a string: `SingleSourceOfTruthTests`
fails the build.

## 5. The migration

The model changed, so add a migration. Only through `dotnet ef`; never write or edit one by hand. From `backend/`:

```sh
dotnet tool restore
dotnet ef migrations add AddMsFarm -p src/Modules/Base.Migrations.SqlServer -s src/Modules/Base.Migrations.SqlServer -c BaseDbContext -o Migrations/Base
```

Check the new file creates a table named `MsFarm` with the key `MsFarmId`. If `platform.yaml` lists PostgreSQL as well, run the command for
`Base.Migrations.PostgreSql` too (`MigrationsConsistencyTests` fails otherwise). Migrations are expand-only: add tables and columns; never
rename or drop in the same release. `scripts/dev-up` runs the Migrator, which applies it to every tenant database.

Then run `scripts/generate`: it exports Base's OpenAPI document and regenerates the typed client (`BaseSchemas`), which the next step uses.

## 6. The screen

`frontend/modules/base/src/pages/FarmList.tsx`. Use the hooks every module screen uses, as in `templates/frontend-module`:

- `useApiQuery` for the list, and `useApiMutation` for create, rename and delete (it refreshes the queries you name and shows a snackbar).
- `useDialog().confirm` before a delete.
- `PageHeader`, `EmptyState`, `ProblemAlert` and `FormDialog` from `@saaskit/ui-kit`.
- All text through `useTranslation("base")`, with the string in `locales/en.json`, `ms.json` and `zh.json`. Colours come from the theme,
  never from a value typed in the page.

Invalidate the master's cache after every change, so pickers elsewhere show it at once:

```ts
const farmQueries = [["base", "farms"], masterKey("base")];
const add = useApiMutation(() => unwrap(client.POST("/api/base/v1/farms", { body: { name } })), { invalidate: farmQueries, success: t("form.added") });
```

Point the module's route at the page in `src/manifest.ts` (replace the empty page, or add a second route and a second `menu` entry):

```ts
routes: [{ index: true, lazy: () => import("./pages/FarmList") }],
```

## 7. Using the master elsewhere

Other screens, in any module, pick a farm with `useMaster` and `MasterSelect`. The list is fetched once, cached for five minutes and
shared by every picker that asks for it:

```tsx
const farms = useMaster("base", "/api/base/v1/farms", { query: { PageSize: 200 } });

<MasterSelect master={farms} label={t("fields.farm")} value={farmId} onChange={setFarmId} required />
farms.labelOf(row.farmId)   // the name where a row stores only the ID
```

`useMaster` reads one page. For a master with more rows than a page, filter on the server through `query`. Store the farm's `id` in the
other module's table as a plain column: there is no foreign key across modules, and the other module never maps `MsFarm`.

## Checklist

- [ ] Entity `MsFarm`, table `MsFarm`, no key mapping (the column is `MsFarmId`)
- [ ] `RequireModuleRole(Modules.Base, ...)` on every endpoint
- [ ] Error messages in en, ms and zh
- [ ] Migration made with `dotnet ef`, and `scripts/generate` run
- [ ] Page text in en, ms and zh; the menu entry in `manifest.ts`
- [ ] A test in `backend/tests/Base.Tests` (create, list, rename, delete, tenant isolation) and one for the page

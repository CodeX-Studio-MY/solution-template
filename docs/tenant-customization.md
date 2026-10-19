# Tenant customization

Every customer is different in some way. This page explains how the platform lets them be, **without a fork, a branch per
customer, or an `if (tenant == "...")` anywhere**. The examples use a hypothetical `goods-receipt` module (`scripts/new-module
goods-receipt`) and a `supplier-rating` add-on; replace them with your own modules.

| Rung | What varies per tenant | Example |
|---|---|---|
| 1. Setting | A value | Receipts above MYR 10,000 need a second check |
| 2. Feature flag | A behaviour, on or off | "Share a draft receipt with a link" |
| 3. Strategy | The logic itself | `single` check, or `two-step` check |
| 4. Custom field | Extra data on a record | A required "Warehouse" chosen from a list |
| 5. Event extension | Something extra happens when something happens | Supplier ratings update when a receipt is accepted |
| 6. Slot | Extra UI inside another module's page | A supplier-rating panel on the receipt page |
| 7. Tenant-only module | A whole capability only some plans include | The `supplier-rating` module |
| Branding (a setting) | Colour and logo | `BrandingSettings` (built in) |

`TenantCustomizationTests` covers rungs 1 and 2 with a test-only settings record and flag, and the module templates' tests show the rest
in a module of your own.

## Which rung do I need?

Use the **lowest rung that works**. Go down the list and stop at the first "yes".

1. **Only a value differs** (a threshold, a number format, a colour)? → **Setting**.
2. **A behaviour is simply on or off** for some tenants? → **Feature flag**.
3. **The logic itself differs** (two ways to approve, two ways to price)? → **Strategy**.
4. **The tenant needs to record extra data** on an existing record? → **Custom field**.
5. **Something extra should happen when something happens** in another module? → **Event extension** (a consumer).
6. **Extra UI is needed inside another module's page**? → **Slot**.
7. **A whole new capability** that only some tenants pay for? → **Tenant-only module**.

If none fits, stop and write an ADR. **Replacing another module's whole page for one tenant is not supported.**

The rules that hold for every rung:

- **Never branch on tenant identity.** Name a variation by what it does (`two-step`), so any tenant can choose it. A test fails the
  build on a comparison against `TenantId` or `TenantSlug` in a business module, and a lint rule does the same in the frontend.
- **Every variation has a default**, so a new tenant works with zero configuration (`A_new_tenant_works_with_no_configuration`).
- **Settings, flags and strategy choices are data in the catalog.** Operators edit all of them through the Tenancy service; a tenant's
  own admin edits only the settings marked `[TenantEditable]`. **Every change is audited.**
- A strategy key that doesn't exist falls back to the default and logs a warning.

## 1. Setting

A typed record in `src/Contracts`, with a default for every value:

```csharp
[TenantSettings(ModuleKeys.GoodsReceipt)]
public sealed record GoodsReceiptSettings
{
    [TenantEditable, Range(0, 1_000_000_000)]
    public decimal SecondCheckAbove { get; init; } = 10_000m;
    ...
}
```

It lives in `Contracts` (not in the module) because Tenancy edits it and the module reads it, and neither may reference the other.
A tenant stores only the values that differ from the defaults. The module reads them through `ITenantSettings<T>`:

```csharp
services.AddTenantSettings<GoodsReceiptSettings>();   // or AddTenantStrategy, which does this for you
var config = await settings.GetAsync(ct);
if (receipt.TotalAmount > config.SecondCheckAbove) ...
```

Changing one: `PUT /api/tenancy/v1/operator/tenants/{id}/settings/{module}` (operators) or `PUT /api/tenancy/v1/settings/{module}` (a tenant
admin, editable settings only). A `null` value puts a setting back to its default. Values are validated against the record's
attributes (`Range`, `AllowedValues`, patterns) before they are stored.

## 2. Feature flag

Declare the flag once, with its default, in `Contracts`:

```csharp
public static class GoodsReceiptFeatures
{
    [FeatureFlag(ModuleKeys.GoodsReceipt, DefaultEnabled = false, Description = "Share a draft receipt with a link.")]
    public const string DraftSharing = "goods-receipt.draft-sharing";
}
```

Check it with `IFeatureFlags`. A feature that is off behaves **as if it doesn't exist** (404, never 403):

```csharp
await flags.IsEnabledAsync(GoodsReceiptFeatures.DraftSharing, ct) ? Results.Ok(...) : Results.NotFound();
```

`IFeatureFlags` evaluates through an **OpenFeature** provider backed by the catalog, so a hosted flag service can replace it by
registering a different provider. Operators flip a flag with `PUT /api/tenancy/v1/operator/tenants/{id}/features/{key}`. The frontend
reads `useFeature(features.goodsReceiptDraftSharing)`; the flag keys are generated into `web-core`.

## 3. Strategy

When the *logic* differs, write one implementation per variation and let a setting pick:

```csharp
services.AddKeyedScoped<IReceiptCheck, SingleCheck>(ReceiptCheckKeys.Single);
services.AddKeyedScoped<IReceiptCheck, TwoStepCheck>(ReceiptCheckKeys.TwoStep);
services.AddTenantStrategy<IReceiptCheck, GoodsReceiptSettings>(s => s.CheckPolicy, ReceiptCheckKeys.Single);
```

The use case asks `ITenantStrategy<IReceiptCheck>.GetCurrentAsync()` and calls it. The list of allowed keys is on the setting
(`[AllowedValues(...)]`), so an invalid choice is refused when it is saved.

## 4. Custom fields

A tenant needs extra data on a record: a *definition* (a row in the module's own schema) plus a *value* in one native JSON column
(`json` on SQL Server 2025, `jsonb` on PostgreSQL).

- `GET /api/goods-receipt/v1/custom-fields?entity=goods-receipt` returns the definitions; records carry `"customFields": { "warehouse": "WH-1" }`.
- Admins define them with `PUT /api/goods-receipt/v1/custom-fields/{entity}/{key}` (label, type `text|number|date|list`, required, options, order).
- The **server** validates every write against the definitions (required, type, listed option; unknown keys are refused, not stored).
- The frontend renders them with `<CustomFieldsSection entity="goods-receipt" />` from `ui-kit`. It is driven entirely by the definitions
  API, so there is no tenant-specific code in the frontend, and a tenant with no definitions sees nothing.
- Keys other modules rely on are agreed in `Contracts` (`WellKnownCustomFields`).

## 5. Event extension

The supplier-rating module reacts to a receipt being accepted. `goods-receipt` knows nothing about it:

```csharp
internal sealed class RateOnAcceptance(RatingService ratings) : IEventConsumer<GoodsReceiptAccepted.V1> { ... }
services.AddEventConsumer<GoodsReceiptAccepted.V1, RateOnAcceptance, SupplierRatingDbContext>(Service, Modules.SupplierRating);
```

Events are delivered to a module's consumers **only for tenants entitled to that module**, and the inbox makes a second delivery a
no-op. The platform's own events (`TenantDayStarted` for daily work, `NotificationRequested` to tell people something) work the same
way. `docs/events.md` lists every event.

## 6. Slot

The receipt page leaves a named place; another module fills it. The only thing they share is the slot's name and props, declared in
`web-core` (`SlotProps`):

```tsx
<Slot name="goods-receipt.detail.side-panel" props={{ receiptId, supplierId }} />
```

```ts
// modules/supplier-rating/src/manifest.ts
extensions: [{ slot: "goods-receipt.detail.side-panel", component: () => import("./slots/SupplierRating"), requiredRole: "user" }],
```

If no module fills the slot (the tenant doesn't have the add-on) it renders nothing. Each extension loads lazily inside its own error
boundary, and a failing remote leaves the host page working. The shell's own slot is `home.dashboard`: cards that modules put on the
home page.

## 7. Tenant-only module

An add-on is an ordinary module with its own schema, `DbContext`, migrations and endpoints protected by
`RequireModuleRole(Modules.SupplierRating, Role.Manager)`. It is simply listed under the higher plans only in `platform.yaml`
(`plans: [plus]`). For a tenant without it, its endpoints return 403 (`platform.module-not-entitled`), its events aren't delivered, and
its menu and panels never load.

## Adding your own

- A new setting: add a property (with a default) to the record in `Contracts`. Nothing else.
- A new flag: add a `const string` with `[FeatureFlag]`, then run `scripts/generate` (the frontend type is generated).
- A new strategy: add an interface, one implementation per variation registered with a key, and `AddTenantStrategy`.
- A new custom-field entity: map `CustomFieldDefinition` in the module's `DbContext` (`ConfigureCustomFieldDefinitions`) and a JSON column on the entity.

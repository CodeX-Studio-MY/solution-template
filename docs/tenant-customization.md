# Tenant customization

Every customer is different in some way. This page explains how the platform lets them be, **without a fork, a branch per
customer, or an `if (tenant == "...")` anywhere**. Everything below is shown by the reference scenario: two tenants running the
same code.

| Behaviour | `demo` (Standard plan) | `demo-plus` (Plus plan, customised) | Rung |
|---|---|---|---|
| Requests below this total are approved on submit | MYR 500 (the default) | MYR 200 | 1. Setting |
| Draft sharing ("Copy link to draft") | off | on | 2. Feature flag |
| How a request is approved | `single`: any manager | `two-level`: a manager, then an admin as well above MYR 5,000 | 3. Strategy |
| Cost center on a request | none | required, chosen from a list | 4. Custom field |
| Budget is used up when a request is approved | no budget module | yes, exactly once | 5. Event extension |
| Remaining-budget panel on the request page | none | shown | 6. Slot |
| Cost Center Budget module | not entitled | entitled | 7. Tenant-only module |
| Colour and logo | platform default | its own | Branding (a setting) |

`scripts/dev-up` creates both tenants so you can see the differences in a browser. The backend and browser tests for each row are in
`ScenarioTests`, `TenantCustomizationTests` and `frontend/e2e` (`scenario.spec.ts` against the built app, `stack/` against the real stack).

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

- **Never branch on tenant identity.** Name a variation by what it does (`two-level`), so any tenant can choose it. A test fails the
  build on a comparison against `TenantId` or `TenantSlug` in a business module, and a lint rule does the same in the frontend.
- **Every variation has a default**, so a new tenant works with zero configuration (`A_new_tenant_works_with_no_configuration`).
- **Settings, flags and strategy choices are data in the catalog.** Operators edit all of them through the Tenancy service; a tenant's
  own admin edits only the settings marked `[TenantEditable]`. **Every change is audited.**
- A strategy key that doesn't exist falls back to the default and logs a warning.

## 1. Setting

A typed record in `src/Contracts`, with a default for every value:

```csharp
[TenantSettings(ModuleKeys.PurchaseRequest)]
public sealed record PurchaseRequestSettings
{
    [TenantEditable, Range(0, 1_000_000_000)]
    public decimal AutoApproveBelow { get; init; } = 500m;
    ...
}
```

It lives in `Contracts` (not in the module) because Tenancy edits it and the module reads it, and neither may reference the other.
A tenant stores only the values that differ from the defaults. The module reads them through `ITenantSettings<T>`:

```csharp
services.AddTenantSettings<PurchaseRequestSettings>();   // or AddTenantStrategy, which does this for you
var config = await settings.GetAsync(ct);
if (request.TotalAmount < config.AutoApproveBelow) ...
```

Changing one: `PUT /api/tenancy/v1/operator/tenants/{id}/settings/{module}` (operators) or `PUT /api/tenancy/v1/settings/{module}` (a tenant
admin, editable settings only). A `null` value puts a setting back to its default. Values are validated against the record's
attributes (`Range`, `AllowedValues`, patterns) before they are stored.

## 2. Feature flag

Declare the flag once, with its default, in `Contracts`:

```csharp
public static class PurchaseRequestFeatures
{
    [FeatureFlag(ModuleKeys.PurchaseRequest, DefaultEnabled = false, Description = "Share a draft request with a link.")]
    public const string DraftSharing = "purchase-request.draft-sharing";
}
```

Check it with `IFeatureFlags`. A feature that is off behaves **as if it doesn't exist** (404, never 403):

```csharp
await flags.IsEnabledAsync(PurchaseRequestFeatures.DraftSharing, ct) ? Results.Ok(...) : Results.NotFound();
```

`IFeatureFlags` evaluates through an **OpenFeature** provider backed by the catalog, so a hosted flag service can replace it by
registering a different provider. Operators flip a flag with `PUT /api/tenancy/v1/operator/tenants/{id}/features/{key}`. The frontend
reads `useFeature(features.purchaseRequestDraftSharing)`; the flag keys are generated into `web-core`.

## 3. Strategy

When the *logic* differs, write one implementation per variation and let a setting pick:

```csharp
services.AddKeyedScoped<IApprovalPolicy, SingleApproverPolicy>(ApprovalPolicyKeys.SingleApprover);
services.AddKeyedScoped<IApprovalPolicy, TwoLevelApprovalPolicy>(ApprovalPolicyKeys.TwoLevel);
services.AddTenantStrategy<IApprovalPolicy, PurchaseRequestSettings>(s => s.ApprovalPolicy, ApprovalPolicyKeys.SingleApprover);
```

The use case asks `ITenantStrategy<IApprovalPolicy>.GetCurrentAsync()` and calls it. The list of allowed keys is on the setting
(`[AllowedValues(...)]`), so an invalid choice is refused when it is saved.

## 4. Custom fields

A tenant needs extra data on a record: a *definition* (a row in the module's own schema) plus a *value* in one native JSON column
(`json` on SQL Server 2025, `jsonb` on PostgreSQL).

- `GET /api/purchase-request/v1/custom-fields?entity=purchase-request` returns the definitions; records carry `"customFields": { "cost-center": "CC-100" }`.
- Admins define them with `PUT /api/purchase-request/v1/custom-fields/{entity}/{key}` (label, type `text|number|date|list`, required, options, order).
- The **server** validates every write against the definitions (required, type, listed option; unknown keys are refused, not stored).
- The frontend renders them with `<CustomFieldsSection entity="purchase-request" />` from `ui-kit`. It is driven entirely by the definitions API, so
  there is no tenant-specific code in the frontend, and a tenant with no definitions sees nothing.
- Keys other modules rely on are agreed in `Contracts` (`WellKnownCustomFields.CostCenter`).

## 5. Event extension

The budget module reacts to a purchase request being approved. `purchase-request` knows nothing about it:

```csharp
internal sealed class UseBudgetOnApproval(BudgetService budgets) : IEventConsumer<PurchaseRequestApproved.V1> { ... }
services.AddEventConsumer<PurchaseRequestApproved.V1, UseBudgetOnApproval, CostCenterBudgetDbContext>(Service, Modules.CostCenterBudget);
```

Events are delivered to a module's consumers **only for tenants entitled to that module**, and the inbox makes a second delivery a
no-op (a unique usage row per request is the second line of defence). `docs/events.md` lists every event.

## 6. Slot

The purchase request detail page leaves a named place; another module fills it. The only thing they share is the slot's name and props,
declared in `web-core`:

```tsx
<Slot name="purchase-request.detail.side-panel" props={{ requestId, costCenter, total }} />
```

```ts
// modules/cost-center-budget/src/manifest.ts
extensions: [{ slot: "purchase-request.detail.side-panel", component: () => import("./slots/RemainingBudget"), requiredRole: "user" }],
```

If no module fills the slot (the tenant doesn't have the budget module) it renders nothing. Each extension loads lazily inside its own
error boundary, and a failing remote leaves the host page working.

## 7. Tenant-only module

`cost-center-budget` is an ordinary module with its own schema, `DbContext`, migrations and endpoints protected by
`[ModuleRole(Modules.CostCenterBudget, Role.Manager)]`. It is simply entitled only in the Plus plan (`platform.yaml`). For `demo` its
endpoints return 403 (`platform.module-not-entitled`), its events aren't delivered, and its menu and panel never load.

## Adding your own

- A new setting: add a property (with a default) to the record in `Contracts`. Nothing else.
- A new flag: add a `const string` with `[FeatureFlag]`, then run `scripts/generate` (the frontend type is generated).
- A new strategy: add an interface, one implementation per variation registered with a key, and `AddTenantStrategy`.
- A new custom-field entity: map `CustomFieldDefinition` in the module's `DbContext` (`ConfigureCustomFieldDefinitions`) and a JSON column on the entity.

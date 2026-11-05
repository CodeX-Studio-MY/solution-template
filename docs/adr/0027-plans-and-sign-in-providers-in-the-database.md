# 0027. Plans, sign-in providers and language preferences in the database

Status: accepted

## Context

ADR 0010 makes `platform.yaml` the single source of truth for the plans a product sells, and the generator turns them into constants the code reads. Adding or renaming a plan, or changing which modules it includes, was therefore a code change and a release. The people who decide this are in the operator console, not in the repository. Two related needs came with it: customers want to sign in with Google, Microsoft, Apple and Facebook, and nobody could choose a language, although the three translations exist.

## Decision

**Plans.** The catalog has a `Plans` table (key, display name, description, optional monthly price, currency, sort order, active flag) and `PlanModuleAccess` (plan and module). `platform.yaml` seeds the first plans and their modules once, with `HasData`, using the plan's position as its ID; after that the operator console owns them. A tenant refers to its plan by `PlanId`; the API still speaks plan keys such as `lite` and adds `displayName`. Onboarding, plan changes and the console read plans from the database, so the generated `Plans` and `Modules[].Plans` are used only for seeding and for tests. A plan is retired, never deleted: new tenants and plan changes can't choose it, tenants already on it stay. Adding or removing a module on a plan changes the entitlements of every tenant on it (removing also takes it away from tenants that had it as an add-on, as a plan change does) and publishes a tenant change for each, so services pick it up within seconds. Modules are still code, so the console only reads them.

**Sign-in providers.** `IdentityProviders` holds one row per set of provider settings, so one kind can have several (two Microsoft directories, separate Google apps). One disabled row per kind is seeded. Client secrets, and Apple's private key, are encrypted with Data Protection and never returned or logged: the API says only whether one is stored. A row can't be enabled until the settings its kind needs are complete. `PrincipalUserProviders` points at the settings row used, with the provider's subject (unique with the row), whether the provider vouches for the email, and when the account was last used. A user can link any number of accounts, and settings with linked accounts can only be disabled. The callback address given to the provider is on the sign-in host and derived from `Tenancy:BaseDomain`. Sign-in with these providers is not connected: the screens say so.

**Language.** A signed-in user's language is `UserPreferences.Locale` in Identity's tenant database, because that is where today's sign-in users are; the catalog's principals are the eventual home and the rows move with the same ID. Operators have no tenant, so `Operators.Locale`. Both are validated against the platform's locales. A support session, or any token with an `act` claim, can read but not change a customer's preference. The language is the user's, then the tenant's, then the platform's; operators fall back to the browser's. The switch applies without a reload, including dates, numbers and money, and sets `<html lang>`.

## Alternatives rejected

Keeping plans in `platform.yaml` and adding an editor that writes the file: the running system would need to change source and redeploy. A JSON column for preferences: real columns are smaller and queryable, and a new preference is one column. Putting the preference in the catalog now: people who sign in today have no catalog row, so a saved preference would not be found.

## Consequences

This is an exception to ADR 0010. A later edit to the plans in `platform.yaml` changes only the seed of a new database, never an existing one, and must not be shipped as a migration against data an operator now owns. Until sign-in moves to the catalog, a person with several companies sets their language once per company. The secrets are encrypted with the Tenancy service's Data Protection keys, so the service that connects provider sign-in has to share that key ring. The customer sign-in, invitation and password pages (`Identity/Api/Html.cs`) are still English only.

# Onboarding a tenant

A tenant is a customer: its own database, its own users, its own plan. Onboarding creates all of it in one idempotent pipeline.

## Locally

```sh
scripts/dev-up                                   # once: the stack, and two demo tenants
scripts/onboard-tenant saaskit-co --plan plus --name "Saaskit Co" --admin-email boss@saaskit-co.example
```

The command prints the tenant's address (`https://saaskit-co.localhost:7443/`; the local certificate covers `*.localhost`, so no new
certificate is needed) and the first admin's one-time invitation link. Open it, choose a password, sign in.

`scripts/onboard-tenant` signs in as the development operator that `scripts/dev-up` creates (credentials in `.dev/operator.json`,
git-ignored) and calls the Tenancy service's operator API. It exists for local development only; in production, operators use the
operator console (out of scope for this template) or call the same API.

## What the pipeline does

Each step is recorded in the catalog; a failed run **resumes where it stopped** when you run it again with the same slug.

1. **Catalog row** with the slug (lowercase letters, digits and hyphens; reserved names such as `login`, `api`, `admin` and `www` are refused),
   the plan, time zone, currency and locale (defaults from `platform.yaml`), and the database placement (provider and server pool).
2. **Database** created on the chosen server pool.
3. **Migrations** applied by the Migrator (the same code the pipeline runs before a release).
4. **Reference data**: modules create their own on first use; customer data is never seeded.
5. **Entitlements**: the plan's modules are enabled.
6. **Activation**: the tenant becomes `Active` (services refuse a tenant that is still `Provisioning`).
7. **First admin invitation**: an emailed, single-use link. There are no default passwords anywhere.

## Isolation, and how to check it

- Another tenant's data is in another database: there is no filter to forget. The platform's integration tests and every module's
  tests (from the module template) create two tenants and assert that neither sees the other's rows, files, events or notifications.
- Customization (settings, flags, custom fields) is stored per tenant and never read across tenants
  (`TenantCustomizationTests`, `One_tenants_customization_never_affects_another`).
- The tenant resolves from the host name and must match the token's tenant; a mismatch is refused (`platform.tenant-mismatch`).

## After onboarding

- Change the plan: `PUT /api/tenancy/v1/operator/tenants/{id}/plan` (modules are reconciled; the change reaches every service within seconds).
- Settings and flags: see [tenant-customization.md](tenant-customization.md).
- Suspend (read-only), reactivate, or offboard: see [runbooks/offboarding.md](runbooks/offboarding.md).
- Move to another server: [runbooks/moving-a-tenant.md](runbooks/moving-a-tenant.md).

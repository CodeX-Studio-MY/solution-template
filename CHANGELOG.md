# Changelog

All notable changes to this project are written here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and
the project uses [Semantic Versioning](https://semver.org/).

## [0.1.4] - 2026-11-12

### Fixed

- **Scaffolding works again.** New high-severity advisories on `Microsoft.AspNetCore.SignalR.StackExchangeRedis` 10.0.13 and
  `Aspire.Hosting` 13.6.1 made the restore fail (warnings are errors), so `scripts/generate` stopped part-way. The .NET servicing
  packages are now 10.0.15, and Aspire is 13.6.5.
- The first `scripts/generate` run accepts `--skip-compose` and `--skip-api`, and finds the Aspire CLI in `~/.aspire/bin` like later
  runs do.
- A scaffolded product's README is titled with the product's name, and its CHANGELOG starts empty instead of carrying the template's.
- The guides, runbooks and ADRs were checked against the code. Outdated statements were corrected, for example that invitations are
  only logged, the `MsFarmDto` naming, and ADR 0011's title.

## [0.1.3] - 2026-11-11

### Added

- **A Feedback module in every product.** Anyone signed in can send feedback from a button in the customer app's top bar: a type
  (bug, idea or other), an optional rating from 1 to 5, a message, and the page it was sent from. A tenant's admins read their own
  tenant's feedback under **Settings, Feedback**; the operator console has a **Feedback** page across every tenant, with a status
  filter and a status menu (new, reviewed, done) that uses `If-Match`, and the latest items also appear on the tenant's Overview.
  `feedback` is a platform module hosted by the tenancy service and locked into every plan, like billing. The data is in the catalog
  database (a `Feedback` table, with new catalog and tenancy migrations); the sender's name is stored with each item.

### Changed

- **`dev-up` raises the sign-in rate limit to 300 a minute for the local stack only.** Every `pnpm e2e:stack` test signs in from the
  same address, and the full run went past the default of 30. Deployed services keep the default.

## [0.1.2] - 2026-11-10

### Added

- **A new module gets its contracts.** `scripts/new-module` creates `backend/src/Contracts/Modules/{Pascal}` with an event, a view
  record and a typed client for the module, which other modules may reference (they may not reference the module itself). The
  template's example uses all three: the API returns `ItemView`, creating an item publishes `ItemCreated.V1` through the module's
  outbox, and `{Pascal}Client.GetItemAsync` reads an item. An architecture test checks that a module's events are named after its key.

### Changed

- The usage text of `scripts/new-module` lists every option (`--from-plan`, `--resume`, `--no-catalog-migration`).

### Fixed

- A scaffolded product's tests failed for a module that isn't in the lowest plan (`--modules appointments:advanced`): the test tenant
  now gets the lowest plan that includes the module under test. Two generator tests no longer assume the template's own `platform.yaml`.

## [0.1.1] - 2026-11-06

### Fixed

- **Account emails are really sent.** Inviting a user or resending an invitation, onboarding a tenant and "Forgot password" now send a
  plain-text English email over SMTP (Mailpit locally) instead of only writing the link to a log. The invitation is created either way:
  if the email could not be sent, the customer app says so and points to **Resend invitation**, and the operator console's onboarding
  dialog says so too. The links are never logged.
- **The Aspire dashboard link works.** `scripts/dev-up` prints the dashboard's sign-in link (with its token), and the README no longer
  sends people to search the Identity logs for invitation links.
- **Support access is visible on both sides.** The operator console lists a tenant's support requests on its Overview (module, role,
  reason, status, expiry and who decided) and refreshes the list after a request. Each step (requested, approved, denied, revoked) is in
  the customer's Security log under "Support staff action", and the tenant admin who decided is named in both consoles' activity.
- **Plans differ.** A module states the lowest plan that includes it (`--modules patients,appointments:advanced`,
  `scripts/new-module <key> --from-plan advanced`) and gets that plan and every plan above it, instead of every plan.
- The Activity tables show readable names for support-access decisions and for modules added to or removed from a plan.

### Changed

- The platform's own modules (access management, tenant settings, billing and base) are locked in every plan: `platform.yaml` fails
  validation if one is missing from a plan, the operator API refuses to remove one from a plan, and the Subscriptions matrix shows a
  locked tick instead of a checkbox.
- `IEmailSender` and `SmtpEmailSender` moved from the Notification module to `Core`, and `IAccountMessages.SendAsync` returns whether the
  email went out. Identity now needs the `Smtp` settings, as Notification already did.

### Known limitations

Account emails are English only. Operators still cannot use an approved support access from the console.

## [0.1.0] - 2026-11-05

The first release.

### Added

- The platform: Gateway (BFF with an `HttpOnly` session cookie), Identity (OpenIddict token server), Tenancy, Notification and Scheduler
  services, with one database per tenant on SQL Server 2025.
- The **Base** module, empty, for a product's master data, and `scripts/new-module` for the others.
- The customer app: sign-in, users, groups, devices, applications, module roles, branding, platform features, support-access approval,
  an activity log and a security log, in English, Malay and Chinese.
- The operator console: onboarding, plans and prices, module entitlement, principals and their tenant access with an audit trail,
  sign-in provider settings, support-access requests, and suspend, resume and offboard.
- Per-tenant customization: settings, feature flags, strategies, event extensions, slots and add-on modules.
- Generators: `platform.yaml` drives constants, routes, the AppHost, the compose file, the CI workflow, API clients and docs;
  `scripts/generate` scaffolds a product from the template (`--product`, `--modules`, `--plans`).
- Scripts for macOS, Linux and Windows: `dev-up`, `dev-down`, `generate`, `new-module`, `onboard-tenant`, `check-licenses`.
- Architecture tests, integration tests with Testcontainers, frontend unit tests and Playwright tests (mocked API and against the
  running stack).
- Documentation: README, guides, 29 architecture decision records, runbooks and generated references (configuration, events, design
  system). See `docs/README.md`.
- A license policy checked by `scripts/check-licenses`.

### Known limitations

PostgreSQL is not supported in this release. Sign-in with Google, Microsoft, Apple and Facebook is settings only. The operator's
Principals directory is not connected to sign-in. Billing is a placeholder and plan prices are display-only. Operators have no roles.
Operators can request support access but cannot use it from the console. Customer sign-in pages are English only. No account emails are
sent. CI only builds. The PowerShell scripts were not run on Windows. See [docs/known-limitations.md](docs/known-limitations.md).

[0.1.4]: https://github.com/CodeX-Studio-MY/solution-template/releases/tag/v0.1.4
[0.1.3]: https://github.com/CodeX-Studio-MY/solution-template/releases/tag/v0.1.3
[0.1.2]: https://github.com/CodeX-Studio-MY/solution-template/releases/tag/v0.1.2
[0.1.1]: https://github.com/CodeX-Studio-MY/solution-template/releases/tag/v0.1.1
[0.1.0]: https://github.com/CodeX-Studio-MY/solution-template/releases/tag/v0.1.0

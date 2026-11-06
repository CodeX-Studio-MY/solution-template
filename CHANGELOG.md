# Changelog

All notable changes to this project are written here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and
the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

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

[0.1.0]: https://github.com/CodeX-Studio-MY/solution-template/releases/tag/v0.1.0

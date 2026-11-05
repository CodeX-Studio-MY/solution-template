# Changelog

All notable changes to this project are written here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and
the project uses [Semantic Versioning](https://semver.org/).

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

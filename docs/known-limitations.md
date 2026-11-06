# Known limitations

What does not work yet, or works differently from what you might expect, in release 0.1.0. Each item was checked against the code or in a
browser for this release.

## Sign-in and accounts

- **Google, Microsoft, Apple and Facebook sign-in is settings only.** The operator console stores each provider's settings and can
  enable one once they are complete, but the sign-in page has no buttons and nobody can sign in with them.
- **Account emails are English only.** Invitation and password-reset emails are sent over SMTP (Mailpit locally; a product configures the
  `Smtp` settings, see [configuration](configuration.md)), but their text is plain English whatever the person's language. Register your own
  `IAccountMessages` to change the wording or the channel.
- **Customer sign-in pages are English only.** The customer app and the operator console are translated (English, Malay, Chinese); the
  pages on the sign-in host (sign-in, password, invitation) have no language switch.
- **Passkey sign-in has no automated test.** The sign-in page offers "Use a passkey". It needs a WebAuthn authenticator to test, and this
  release did not test it by hand.

## Operator console

- **The Principals directory is not connected to sign-in.** Users, devices and applications you add under Principals, and the tenant
  access you grant them, do not appear in the customer app's Users page and do not let anyone sign in. The tenant's **Login history** tab
  stays empty for the same reason: sign-in does not write to it.
- **Operators have no roles.** Any operator can do everything, including starting a tenant's offboarding and completing its deletion.
- **Support access can be requested, approved and issued, but not used from the console.** An operator asks for time-limited access to
  one module, and sees the request and the admin's answer on the tenant's Overview. The tenant's admin approves or denies it under
  Settings, Support access, and every step is in the tenant's Security log. The operator API then issues a 10-minute token
  (`POST /api/tenancy/v1/operator/support-access/{id}/token`), but the console has no control that uses it, and the Gateway does not
  accept it.
- **Billing is a placeholder.** `billing` is a module in the plans, but there is no billing provider (`NoBilling`): every tenant is in good
  standing. A plan's monthly price is shown in the console and used for nothing else.

## Tenants and data

- **Offboarding exports nothing and leaves files behind.** The export step is a placeholder (`NoExportYet`); a product adds an exporter
  for each module that holds customer data. Completing a deletion drops the tenant's database but not its files in object storage.
  Delete them yourself, as the [offboarding runbook](runbooks/offboarding.md) says.
- **Uploaded files are not scanned for malware.** The default scanner accepts everything ([ADR 0029](adr/0029-messaging-files-and-data-defaults.md)).
  Register your own `IMalwareScanner` before taking real uploads.
- **PostgreSQL is not supported.** `platform.yaml` lists `sqlserver` only, and there are no PostgreSQL migrations
  ([ADR 0011](adr/0011-sql-server-and-postgresql.md)). `scripts/dev-up.sh --provider postgresql` refuses.
- **A duplicate master name returns 500.** A master entity with a unique name (`MsFarm`) answers 500 when a second one has the same name,
  until the service checks first and throws a `ConflictException`. The [guide](adding-a-master-entity.md) shows how. The sample `Item` in
  the module template has the same gap.
- **Custom fields are building blocks.** The definition entity, the validator and the `CustomFieldsSection` component exist. There are no
  custom-field endpoints; a module that wants them writes its own ([guide](tenant-customization.md)).

## The product

- **There is no notification bell.** The Notification service stores notifications, pushes them over SignalR and sends email, but no screen
  in the shell shows them.
- **Notification email is best effort.** A failed send is logged and dropped; there is no retry queue
  ([ADR 0029](adr/0029-messaging-files-and-data-defaults.md)).

## Build, CI and delivery

- **CI only builds.** `.github/workflows/ci.yml` builds the backend and typechecks, lints and builds the frontend. It does not run the
  tests, the generator's drift check or the license check, and it publishes nothing. A tag starts no workflow. The README's Release
  section lists the manual steps.
- **The compose file runs the stack on one machine.** It needs images you build, a TLS certificate for the Gateway, and extra settings to
  create the first operator (README, Deploy). It is not a cloud deployment.
- **Windows scripts were not run for this release.** The PowerShell scripts (`*.ps1`) were read and compared with the shell scripts, but
  no Windows machine or `pwsh` was available. Treat them as untested.
- **One dependency advisory is ignored.** `braces` has no patched release. It is a lint-time dependency that only reads this repository's
  own patterns, so it is ignored in `frontend/pnpm-workspace.yaml`.
- **TypeScript stays on 6.x.** `typescript-eslint` supports TypeScript below 6.1 only. Move to 7 when it does (one line in the pnpm
  catalog, `frontend/pnpm-workspace.yaml`).
- **No `eslint-plugin-jsx-a11y`.** It does not support ESLint 10 yet. The axe checks in the Playwright tests cover the rendered pages, and MUI provides the semantics.

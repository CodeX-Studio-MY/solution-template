# Documentation

In the order most people need them. The [README](../README.md) comes first: it covers prerequisites, running the stack, signing in,
testing and deploying.

## Build with the platform

1. [Adding a module](adding-a-module.md): one command creates a backend and frontend module; what it makes, the rules the build enforces,
   changing the data model, and the frontend toolkit.
2. [Adding a master entity](adding-a-master-entity.md): the first `MsFarm` in the Base module, from the table to the screen.
3. [Onboarding a tenant](onboarding-a-tenant.md): the pipeline, how isolation is checked, what to do afterwards.
4. [Tenant customization](tenant-customization.md): which of the seven rungs to use, from a setting to an add-on module.
5. [Frontend](../frontend/README.md): the workspace, the design system and the lint rules.

## Know what to expect

- [Known limitations](known-limitations.md): what is not connected yet in this release.
- [Changelog](../CHANGELOG.md): what each release contains.

## Operate it

[Runbooks](runbooks/README.md): [backup and restore](runbooks/backup-and-restore.md), [key rotation](runbooks/key-rotation.md),
[moving a tenant](runbooks/moving-a-tenant.md), [offboarding](runbooks/offboarding.md), [incident response](runbooks/incident-response.md)
and [SLOs](runbooks/slos.md).

## Understand why

[Architecture decisions](adr/README.md): one page per decision, with the alternative that was rejected.

## Generated references

Do not edit these by hand. Change the source and run `scripts/generate.sh` (`scripts/generate.ps1` on Windows).

- [Configuration](configuration.md): every setting and environment variable, from the typed options in `backend/src/Core/Configuration`.
- [Events](events.md): every integration event, from `backend/src/Contracts`.
- [Design system](design-system.md): the visual tokens, from `frontend/packages/ui-kit/src/theme/tokens.ts`.

`license-exceptions.json` is data for `scripts/check-licenses.sh`, not a guide.

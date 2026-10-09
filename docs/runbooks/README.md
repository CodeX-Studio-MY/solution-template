# Runbooks

Short procedures for the operations that need one. Each says what you need, what to do, how to check, and what can go wrong.
Where the platform has an API for a step it is named; where a step belongs to your cloud (backups, networking, secret stores), the
runbook says what to ask of it and leaves the product-specific commands as placeholders to fill in for your environment.

| Runbook | When |
|---|---|
| [Backup and restore](backup-and-restore.md) | Restoring one tenant to a point in time; recovery targets |
| [Key rotation](key-rotation.md) | Token signing keys, machine keys, service secrets, the Data Protection key |
| [Moving a tenant](moving-a-tenant.md) | To another server or pool; pinning to a region (data residency) |
| [Offboarding a tenant](offboarding.md) | Export, grace period, deletion, confirmation |
| [Incident response](incident-response.md) | Finding everything one tenant or principal did; revoking a principal now |
| [SLOs](slos.md) | What "healthy" means, and what to alert on |

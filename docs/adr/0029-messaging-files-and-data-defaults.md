# 0029. Defaults for messaging, files and data that a product must know about

Status: accepted

## Context

Several behaviours are choices that look like omissions. A product changes them on purpose, so they are written down once.

## Decision

- **Events nobody has a queue for** go to an alternate exchange and a queue of their own (`UnroutedExchange`) instead of being dropped.
  Publishing before a consumer has declared its queue (first start) would otherwise lose the event silently.
- **Dead-letter replay** publishes the message back to the exchange with its original routing key, so every consumer bound to that key
  gets it again. Consumers are idempotent (inbox), so the extra deliveries are skipped.
- **A failed email** is logged and does not fail the event, so a broken mail server never dead-letters business events. There is no
  email retry queue; add a retrying sender if delivery must be guaranteed.
- **Idempotency keys**: a key whose first request never finished (a crash) is treated as abandoned after a time window and can be used
  again. The stored hash covers the method, path and body.
- **The Scheduler** has no module database, so it sends its trigger events straight to the broker with publisher confirms. The event ID is
  derived from job, tenant and local date, so a repeat produces the same message and consumers handle it once.
- **Uploads** are checked for size, extension and sniffed content type, then passed to `IMalwareScanner`. The default adapter,
  `NoScanner`, accepts everything. A real scanner (ClamAV, Defender for Storage) is a deployment choice: register your own.
- **SQL Server retries** (`EnableRetryOnFailure`) are off. EF Core's retrying execution strategy rejects the user-started transactions that
  the outbox and document numbering use. Transient faults are handled by HTTP resilience and message retries.
- **Suspended tenants** can read but not write. Writes by the tenant's own principals are refused (`platform.tenant-read-only`); platform
  maintenance by platform service principals, such as revoking tokens and offboarding, still works.

## Alternative rejected

Making each of these a setting. Rejected: nothing here varies by tenant, and a safe default plus a replaceable service is simpler to
reason about than another switch.

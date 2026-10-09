# Incident response

## First five minutes

1. **Is it one tenant or all?** `/health/ready` per service (readiness checks the dependencies; `/health/live` only the process). The
   dashboards in [slos.md](slos.md).
2. **Stop the bleeding** if data is at risk: suspend the tenant (read-only), or revoke a principal (below).
3. **Write down the time** and keep every log line you look at.

## Where the evidence is

Every service exports OpenTelemetry (traces, metrics, logs) over OTLP (`OTEL_EXPORTER_OTLP_ENDPOINT`). Logs are JSON, one object per
line, with the **tenant**, the **trace ID** and the **principal** as scope; secrets and tokens are redacted.

- Locally: the Aspire dashboard (`http://localhost:15100`), Mailpit for emails.
- Deployed: your OTLP backend (`<query tool>`).

**All activity of one tenant**: filter logs/traces by `tenant.id = {tenantId}` (the ID is in `catalog.Tenants`). Add `service.name` to narrow.
**All activity of one principal**: filter by `enduser.id = {principalId}` (the token's `sub`), across tenants if needed.
**A single request**: take the `traceparent` from the failing response's `traceId` (it is in every `ProblemDetails`) and open the trace; the
same trace continues through RabbitMQ into the consumers.
**What changed in the data**: every entity has `CreatedBy/On` and `UpdatedBy/On`, set by the platform from the current principal (a
service principal when it was background work, a delegated token records the user and the machine). The Identity module has an audit
log of access changes; Tenancy has one of tenant and customization changes.

## Revoking a principal now

- A user, machine or service principal: `PUT /api/identity/v1/principals/{id}/active` with `{ "active": false }` (needs `If-Match`).
  This raises the principal's `access_version`; services compare the token's version to the current one and refuse a stale token
  (`platform.access-changed`, 401) on its next request. There is no waiting for the token to expire.
- A machine's key: `DELETE /api/identity/v1/principals/{id}/keys/{keyId}`.
- A role: `DELETE /api/identity/v1/principals/{id}/assignments/{module}` (also raises the version).
- **Everyone in a tenant**: the platform service call `POST /api/identity/v1/platform/revoke-all-tokens` (made by offboarding; operators do it
  through support tooling).
- **A leaked signing key**: see [key-rotation.md](key-rotation.md).

If the valkey outage handling matters: while the access-version store is unavailable, services fail closed for requests that need the check
(`platform.access-check-unavailable`, 503) rather than trusting an old token.

## Stuck messages

Dead-lettered events are in queues named `{queue}.dlq` (RabbitMQ management UI, or `IDeadLetterAdmin` for list and replay). Replay only after
the cause is fixed; consumers are idempotent (inbox), so a replay is safe. Events nobody listens for land in the unrouted queue instead of vanishing.

## After

Write down what happened, what you did, and one change that would have made it easier (a runbook edit counts). Put it in an ADR if it
changes a decision.

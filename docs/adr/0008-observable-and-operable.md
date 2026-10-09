# 0008. Observable and operable

Status: accepted

## Context

We need to answer 'what happened to this tenant's request' quickly.

## Decision

Every request, message and job carries a trace (W3C `traceparent`, continued through RabbitMQ), the tenant and the principal, exported with OpenTelemetry over OTLP. Logs are JSON with secrets redacted. Liveness checks only the process; readiness checks the dependencies. Audit fields are filled by the platform on every entity, and Identity and Tenancy keep audit logs of access and tenant changes. Runbooks cover the operations that need one.

## Alternative rejected

Per-service logging and ad hoc metrics. Rejected: no way to follow a request across services or find all activity of one tenant.

## Consequences

Dashboards and alerts are placeholders to fill per environment; the signals exist from the first commit (see `docs/runbooks/slos.md`).

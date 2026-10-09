# Service level objectives

Targets to start from; change them with the business. Each has an indicator you can compute from the telemetry the platform already
emits (the ASP.NET Core and HTTP client metrics from OpenTelemetry, the health checks, and queue depth from RabbitMQ).

| Objective | Indicator | Target | Alert when |
|---|---|---|---|
| API availability | Share of requests per service that are not 5xx (excluding 503 from a deliberate read-only tenant) | 99.9% over 30 days | Burning the error budget 6× too fast for 1 hour |
| API latency | p95 duration of `/api/*` per service | < 500 ms (reads), < 1 s (writes) | p95 above target for 15 minutes |
| Sign-in | Success rate of the authorization-code flow, and p95 of `/connect/token` | 99.9%, < 300 ms | Success rate below 99% for 10 minutes |
| Event delivery | Age of the oldest message in any non-DLQ queue; messages in any `.dlq` | < 60 s; 0 | Age > 5 minutes, or any dead letter |
| Outbox lag | Oldest unpublished outbox row (per module) | < 30 s | > 5 minutes |
| Scheduled jobs | Time since the reminder trigger last fired; misfires | every 15 minutes ± 1 | Missing two ticks |
| Readiness | `/health/ready` per instance | 100% of instances ready | Any instance not ready for 5 minutes |
| Backups | Age of the newest restorable point per tenant database | < 5 minutes | > 15 minutes |
| Errors by tenant | 5xx rate per tenant | one tenant above 5% while others are below 0.5% | Page the on-call: this is a data problem, not an outage |

Dashboards worth having from day one: request rate, error rate and latency per service (RED); per-tenant error rate; RabbitMQ queue
depth and dead letters; outbox lag; database connections per pool; Valkey memory and evictions; the Scheduler's last tick.

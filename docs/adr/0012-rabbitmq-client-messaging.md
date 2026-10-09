# 0012. A thin messaging layer over RabbitMQ.Client

Status: accepted

## Context

Modules publish and consume integration events with exactly-once effect, and the common messaging frameworks have turned commercial or are heavy.

## Decision

A small layer in `Core` over `RabbitMQ.Client`: a transactional outbox written in the business transaction, a signal-driven relay (a Redis stream tells it which tenant has rows, with a slow sweep as a safety net), publisher confirms, an inbox that makes consumers idempotent, entitlement-filtered delivery, retries with growing delays, dead-letter queues with list and replay, and trace propagation. No messaging framework.

## Alternative rejected

MassTransit (v9 and later is commercial), a hosted-service poller over every tenant database, and CAP-style libraries. Rejected on license, scaling (polling thousands of databases) and control.

## Consequences

We own about a thousand lines of messaging code with tests, in exchange for no licence risk and behaviour that fits one-database-per-tenant.

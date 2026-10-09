# 0021. Quorum queues and the transactional outbox

Status: accepted

## Context

Events must not be lost or duplicated in effect, and a database commit and a broker publish cannot be atomic.

## Decision

Events are written to an outbox table in the same transaction as the business change and published after commit with publisher confirms; consumers record handled event IDs in an inbox in the same transaction as their effect. Queues are RabbitMQ quorum queues (replicated), with retry queues, dead-letter queues and an alternate exchange for unrouted events.

## Alternative rejected

Publishing straight to the broker after commit (a crash loses the event), distributed transactions, and classic mirrored queues. Rejected: unsafe, unavailable, and deprecated.

## Consequences

At-least-once delivery with idempotent consumers, which is how every consumer is written.

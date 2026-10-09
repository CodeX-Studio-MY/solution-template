# 0013. Valkey locally, any Redis-protocol store in the cloud

Status: accepted

## Context

We need a shared store for BFF sessions, the Data Protection key ring, rate limits, the outbox signal stream and the SignalR backplane.

## Decision

Code uses the Redis protocol through `StackExchange.Redis` only. Locally and when self-hosted the store is Valkey (BSD licence); in the cloud, Azure Managed Redis or ElastiCache.

## Alternative rejected

Redis itself (its licence changed), or an in-memory cache per instance. Rejected: licence, and per-instance state breaks sessions and limits across instances.

## Consequences

Sessions and caches are disposable by design; the key ring is wrapped by a key in the secret store.

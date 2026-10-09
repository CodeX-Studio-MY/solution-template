# 0006. Cloud-ready by default

Status: accepted

## Context

Services run as several instances and are replaced at any time.

## Decision

Services are stateless: files go to object storage (`IFileStorage`: Azure Blob, S3, or a local folder for development only), time is UTC through `TimeProvider`, sessions and caches are in a Redis-protocol store, there is no local disk state, and recurring jobs run once in a clustered Scheduler rather than in every instance. Containers run in UTC as a non-root user with a health check.

## Alternative rejected

Sticky sessions, local files, and a timer per instance. Rejected: they work with one instance and fail in production.

## Consequences

Everything that was convenient locally has an emulator (Azurite, SeaweedFS, Mailpit) so the same code paths run.

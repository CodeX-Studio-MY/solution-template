# 0015. Per-service token audiences

Status: accepted

## Context

A token stolen from one service should not work against another.

## Decision

Every service has its own audience, `{product}:{service-key}`, generated from `platform.yaml`. A host accepts only the audiences of the services it runs (`AllInOne.Host` accepts all of its own). The Gateway obtains and caches a token per downstream service for each session (resource indicators, RFC 8707); a module calling another module exchanges its token for the target's audience (RFC 8693).

## Alternative rejected

One audience for the whole platform. Rejected: any compromised service could replay its tokens anywhere.

## Consequences

Slightly more token traffic, handled by caches with single-flight refresh.

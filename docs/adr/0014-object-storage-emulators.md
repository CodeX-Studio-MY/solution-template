# 0014. Object storage and its emulators

Status: accepted

## Context

Files must live in object storage in every environment, and developers need the same code path locally.

## Decision

`IFileStorage` has Azure Blob, S3 and local-folder adapters. The cloud in `platform.yaml` decides the emulator the stack runs: Azurite for `azure`, SeaweedFS (S3 API) for `aws`. Uploads are checked by their real content type, stored quarantined until a malware-scan hook clears them, keyed `{tenantId}/{module}/...`, and downloaded through short-lived signed links.

## Alternative rejected

MinIO as the S3 emulator (its licence and the features removed from its community edition make it a poor default for a template that must stay freely redistributable) and storing files in the database. Rejected on licence and on database size and backup cost.

## Consequences

The default scanner accepts everything (deviation 26); production should register a real one.

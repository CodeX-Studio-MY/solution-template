# 0011. Support SQL Server and PostgreSQL

Status: accepted

## Context

Customers' clouds and policies differ; some products need SQL Server, others PostgreSQL, and the choice must not change the code.

## Decision

Two supported providers, chosen per environment by `Database:Provider`. Business code is provider-neutral EF Core LINQ; everything that differs (sequential GUIDs vs UUIDv7, row version vs `xmin`, filtered-index SQL, native JSON, sequence counters, locks, naming) is behind `IDatabaseProvider` in `Core.Data.SqlServer` and `Core.Data.PostgreSql`. Each module has a migrations project per provider generated from the same model, and every test that touches data runs on both (Testcontainers, SQL Server 2025 and PostgreSQL 18).

## Alternative rejected

A single provider with the other 'planned', or an abstraction like a repository per provider in each module. Rejected: a second provider added late means rewriting; per-module provider code multiplies work.

## Consequences

Case sensitivity differs, so uniqueness on user-entered text uses normalised columns. A third provider is a new `Core.Data.*` package, a migrations project per module and a line in `platform.yaml`.

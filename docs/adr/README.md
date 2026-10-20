# Architecture decisions

Each page states a decision, why, and the alternative that was rejected. Add one when you change a principle or choose between real alternatives; keep it short.

| # | Decision |
|---|---|
| 0001 | [Multi-tenant from day one: one database per tenant](0001-multi-tenant-database-per-tenant.md) |
| 0002 | [A module is a vertical slice that owns its data](0002-modules-own-their-data.md) |
| 0003 | [One service per module by default, but hosting is a deployment choice](0003-hosting-is-a-deployment-choice.md) |
| 0004 | [A shared kernel provides the plumbing](0004-shared-kernel.md) |
| 0005 | [Secure by default](0005-secure-by-default.md) |
| 0006 | [Cloud-ready by default](0006-cloud-ready.md) |
| 0007 | [Modules can be switched on per tenant](0007-entitlements-per-tenant.md) |
| 0008 | [Observable and operable](0008-observable-and-operable.md) |
| 0009 | [Boundaries are enforced by tests, not by convention](0009-boundaries-enforced-by-tests.md) |
| 0010 | [Single source of truth and `scripts/generate`](0010-single-source-of-truth.md) |
| 0011 | [Support SQL Server and PostgreSQL](0011-sql-server-and-postgresql.md) |
| 0012 | [A thin messaging layer over RabbitMQ.Client](0012-rabbitmq-client-messaging.md) |
| 0013 | [Valkey locally, any Redis-protocol store in the cloud](0013-valkey-and-redis-protocol.md) |
| 0014 | [Object storage and its emulators](0014-object-storage-emulators.md) |
| 0015 | [Per-service token audiences](0015-per-service-token-audiences.md) |
| 0016 | [Tenant-aware OpenIddict stores](0016-tenant-aware-openiddict-stores.md) |
| 0017 | [Rsbuild and MUI, not Vite or Tailwind](0017-rsbuild-and-mui.md) |
| 0018 | [The customization ladder](0018-customization-ladder.md) |
| 0019 | [A BFF: no tokens in the browser](0019-bff-no-tokens-in-the-browser.md) |
| 0020 | [OpenIddict as the token server](0020-openiddict-token-server.md) |
| 0021 | [Quorum queues and the transactional outbox](0021-quorum-queues-and-outbox.md) |
| 0022 | [A monorepo with Module Federation, not a repository per micro frontend](0022-monorepo-and-module-federation.md) |
| 0023 | [License policy](0023-license-policy.md) |
| 0024 | [Spatial types with NetTopologySuite, as geography in SRID 4326](0024-spatial-types.md) |

## Writing one

Copy any page. Keep Context to what a newcomer needs, Decision to what the code does today (not what it might do), and always name the alternative you did not choose and why. Supersede, don't edit history: a new ADR changes the old one's status to `superseded by NNNN`.

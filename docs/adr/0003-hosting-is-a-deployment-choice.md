# 0003. One service per module by default, but hosting is a deployment choice

Status: accepted

## Context

Small customers and local development want one process; larger deployments want to scale and release modules separately.

## Decision

Each module is a class library plus a thin host (about ten lines of `Program.cs`). By default every module runs as its own service behind the Gateway. `AllInOne.Host` registers every module in one process, generated from `platform.yaml`. Module-to-module calls always use HTTP with a token for the target's audience, even inside one process, so there is one code path and one test strategy.

## Alternative rejected

In-process method calls between modules when they share a process. Rejected: it would make the all-in-one layout behave differently from the distributed one and hide boundary violations.

## Consequences

The Gateway routes `/api/{service-key}/*` to one address (`Bff:AllInOneAddress`) in the all-in-one layout.

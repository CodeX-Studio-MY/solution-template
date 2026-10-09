# 0022. A monorepo with Module Federation, not a repository per micro frontend

Status: accepted

## Context

Frontend modules must ship independently per tenant, but share a design system, a client and types.

## Decision

One repository holds the shell, the shared packages and every module, with pnpm workspaces and Turborepo. Each module builds to an immutable, versioned path and exposes `./manifest`; the shell registers the tenant's modules at runtime from data and loads them with Module Federation 2.0. Shared singletons (React, MUI, Emotion, Zustand, i18next, web-core, ui-kit) are read from the installed versions. Modules never import each other (lint), and any module can later move to its own repository unchanged.

## Alternative rejected

A repository per micro frontend, or a static federation config in the shell. Rejected: version skew in shared packages and a release per change; and a rebuild to change what a tenant has.

## Consequences

Changing a tenant's module version is a data change, which also allows canary releases and one-line rollbacks per tenant.

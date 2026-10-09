# 0017. Rsbuild and MUI, not Vite or Tailwind

Status: accepted

## Context

The frontend is a set of independently built modules composed at runtime, themed per tenant.

## Decision

Rsbuild (Rspack) builds the shell and every module; its Module Federation 2.0 support is first class, and development and production use the same bundler. MUI provides the components a business app needs and one theme object that changes per tenant at runtime through CSS variables.

## Alternative rejected

Vite 8: comparably fast, but Module Federation arrives through a plugin and has newer support. Tailwind: a styling utility rather than a component library, and in micro frontends each module's classes can be missing from the host's CSS or clash with it; mixing it with MUI would mean two styling systems.

## Consequences

Emotion's styles need the CSP nonce (the Gateway injects it); fonts are served from the app because the policy allows none from elsewhere.

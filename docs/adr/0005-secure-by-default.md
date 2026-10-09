# 0005. Secure by default

Status: accepted

## Context

Previous systems had anonymous controllers, one shared JWT secret in every service, tokens in `localStorage`, and default passwords.

## Decision

A fallback authorization policy requires an authenticated principal on every endpoint unless it is marked `[AllowAnonymous]`. Users, machines and services all sign in with OAuth 2.0 and are authorized by their role in each module (`{module-key}:{role}`). Tokens are ES256 signed by Identity alone and validated everywhere with published public keys. There are no secrets in the repository; where a cloud supports it, managed identities replace secrets. Passwords are never defaulted: invitation and reset links only.

## Alternative rejected

Per-controller `[Authorize]` attributes (forgettable), HMAC-signed tokens with a shared secret, and a long-lived API key per integration. Rejected as the anti-patterns they are.

## Consequences

Everything that is public is a deliberate, visible exception (sign-in pages, health, the machine enrollment endpoint, with rate limits).

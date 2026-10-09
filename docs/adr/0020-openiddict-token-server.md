# 0020. OpenIddict as the token server

Status: accepted

## Context

We need a standards-based OAuth 2.0 / OpenID Connect server that we can own, including token exchange and private-key client authentication.

## Decision

OpenIddict 7 on ASP.NET Core Identity: authorization code + PKCE, `client_credentials` with `private_key_jwt` for machines and services, refresh-token rotation with reuse detection, and RFC 8693 token exchange for delegation and audience exchange. ES256 keys come from the secret store; support-access tokens are signed by Identity itself.

## Alternative rejected

Duende IdentityServer (commercial licence), Keycloak or a hosted IdP (a second system to run, tenant model mismatch), or a hand-rolled token endpoint. Rejected on licence, operations and risk.

## Consequences

DPoP is not implemented (deviation 20). Machine clients must set `typ: client-authentication+jwt` on assertions (deviation 11).

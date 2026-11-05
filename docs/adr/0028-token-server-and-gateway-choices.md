# 0028. Token server and Gateway choices that follow from the libraries

Status: accepted

## Context

Identity uses OpenIddict and the Gateway uses ASP.NET Core's OpenID Connect handler. A few of their defaults do not fit a platform with
a BFF and several services behind it, and each needed a deliberate choice that code and operators have to know about.

## Decision

- **Machine sign-in** is `client_credentials` with `private_key_jwt` (RFC 7523). OpenIddict requires the client assertion's JWT header
  to say `typ: client-authentication+jwt`, so every agent that signs in this way must set it.
- **The `roles` claim** is one string when a principal has exactly one role and an array when it has two or more, because OpenIddict
  serialises a single-valued claim as a string. Services read both forms.
- **Access tokens** carry `iat` and `exp` but no `nbf`. Validation checks `iss`, `aud`, `exp`, `typ`, the ES256 signature and the key's
  `kid`, and `nbf` when it is present.
- **Service-to-service exchange**: OpenIddict's rule that a token may only be presented by the client it was issued to makes service
  calls on a user's behalf impossible. `ExchangeAuthorizedParty` replaces that handler for token exchange only: platform services may
  present a user's token. Authorization codes and refresh tokens keep the strict rule.
- **Support-access tokens** are signed by Identity itself with the token server's ES256 key (`SupportTokenIssuer`), not through an
  OpenIddict endpoint, because token exchange needs a subject token that OpenIddict issued. They last 10 minutes.
- **The Gateway uses `response_mode=query`.** `form_post` would need `SameSite=None` cookies, which conflicts with the `SameSite=Lax`
  session cookie.
- **Transport security**: `TokenServer:RequireTransportSecurity` is on by default. The AppHost and the published compose file turn it
  off, because services reach Identity over plain HTTP on the private network and the Gateway terminates TLS. Internal TLS is the
  deployment's job.

## Alternative rejected

Patching OpenIddict further, or issuing our own tokens for everything. Rejected: the exceptions above are narrow and each is covered
by a test; replacing the token server would drop its protocol coverage.

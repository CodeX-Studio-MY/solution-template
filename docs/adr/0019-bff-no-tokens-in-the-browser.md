# 0019. A BFF: no tokens in the browser

Status: accepted

## Context

Tokens readable by JavaScript (`localStorage`) are stolen by any script injection.

## Decision

The Gateway is a confidential OAuth client (authorization code with PKCE). After sign-in the tokens stay server-side in Valkey; the browser gets a `__Host-` session cookie (`HttpOnly`, `Secure`, `SameSite=Lax`) and sends `X-CSRF: 1` on anything that changes data. The app calls relative `/api/...` paths, and the Gateway attaches the right token per service. A strict CSP (nonce for styles, no inline scripts) is added by the Gateway.

## Alternative rejected

A public single-page-app client holding tokens in memory or storage; a cookie shared with the API directly. Rejected: both expose tokens or conflate sessions with API authentication.

## Consequences

The browser never sees a token; the cost is one extra hop and a stateful session store.

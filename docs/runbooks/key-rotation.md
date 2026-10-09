# Key rotation

Nothing secret is in the repository; every key below is in the secret store (or a managed identity), and every rotation is done by
**adding the new key first and removing the old one later**, so nothing is ever refused during the change.

## Token signing keys (ES256)

Only the Identity service holds the private key. Services validate with the published public keys (`/.well-known/jwks`), cached by `kid`.

1. Generate a new P-256 key: `openssl ecparam -name prime256v1 -genkey -noout`.
2. Put it in the secret store as the **first** entry of `TokenServer:SigningKeys` and keep the old one as the second. The first key signs;
   the others stay published.
3. Deploy Identity. New tokens carry the new `kid`; services fetch the new public key on the first unknown `kid`.
4. Wait longer than the longest token lifetime (`TokenServer:AccessTokenMinutes`, refresh tokens are re-signed on use) plus the JWKS cache time.
5. Remove the old key from `TokenServer:SigningKeys` and deploy again.

**If a key leaked**: do steps 1-3 immediately, remove the old key at once (accepting that every session signs in again), and revoke
everything: `POST /api/identity/v1/platform/revoke-all-tokens` for each tenant (a service call; it raises every principal's `access_version`).

## Encryption keys for codes and refresh tokens

`TokenServer:EncryptionKeys` (base64, 256 bits): the first encrypts, all of them still decrypt. Prepend a new one, deploy, and remove the old
one after the longest refresh-token lifetime (`TokenServer:RefreshTokenDays`).

## Service secrets and the Gateway's secret

Each service has a client secret (`ServiceClient:ClientSecret`), and the Identity service holds the matching one in
`TokenServer:PlatformClientSecrets` keyed by client ID (`{product}-{service}`, and `{product}-bff` for the Gateway).

1. Generate a new secret and add it to Identity's `PlatformClientSecrets` for that client (the setting accepts the new value as a
   replacement; during the switch, deploy Identity first, then the service).
2. Set the service's `ServiceClient:ClientSecret` (or the Gateway's `Bff:ClientSecret`) to the new value and deploy it.
3. Confirm service-to-service calls succeed (`platform.service-token-unavailable` stops appearing), then remove the old secret.

Where the cloud supports it, prefer a managed identity for the service's own cloud access (object storage, databases) so there is nothing to rotate.

## Machine keys (devices and integrations)

A machine signs in with `private_key_jwt`: it holds a private key; Identity holds the public key. To rotate:

1. A tenant admin issues a new enrollment code (`POST /api/identity/v1/machines/{id}/enrollment`).
2. The device generates a new key pair and enrolls with the code and the new public key (`POST /api/identity/v1/machines/enroll`).
3. Remove the old key: `DELETE /api/identity/v1/principals/{id}/keys/{keyId}`.

A lost device: remove its keys (or set the principal inactive, `PUT /api/identity/v1/principals/{id}/active`), which refuses its next token request.

## The Data Protection key encryption key

`DataProtection:KeyEncryptionKey` wraps (AES-256-GCM) the key ring stored in Valkey under `{product}:data-protection-keys`. The ring protects
session tickets, antiforgery tokens and the operators' TOTP secrets, and rotates itself every 90 days. To rotate the wrapping key:

1. Generate a new 256-bit key: `openssl rand -base64 32`.
2. Set `DataProtection:KeyEncryptionKey` to the new key and `DataProtection:PreviousKeyEncryptionKeys` to the old one(s). Deploy every service
   (they share the ring, so they all need both).
3. New ring entries are written under the new key; existing ones are still read with the previous key. Nobody is signed out.
4. After 90 days (every old ring entry has expired) remove the previous key and deploy again.

If a service starts with the wrong or missing previous key it fails loudly when it reads an entry it can't open, naming the setting.

## Database and broker credentials

Use the cloud's managed identity or rotate the password in the secret store and restart the hosts (connection strings are read at start).

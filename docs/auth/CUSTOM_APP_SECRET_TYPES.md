# Custom App Secret Types

This document describes how custom app secrets are typed and how each type is intended to be used.

## Overview

Custom app secrets now use an enum-like type model instead of a boolean flag:

- `Oidc`
- `AppConnect`

In code, this is represented by `CustomAppSecretType`.

## Secret Types

### `Oidc`

Use `Oidc` for OAuth2/OIDC client authentication flows.

Typical use:
- Token endpoint client authentication
- Existing OIDC provider client-secret validation logic

### `AppConnect`

Use `AppConnect` for non-OAuth SSO based on DysonNetwork's dedicated protocol.

Intended flow:
1. Server issues a challenge to the app.
2. App signs the challenge using an `AppConnect` secret.
3. Server verifies the signature using the same secret material.

`AppConnect` secrets are isolated from OIDC credentials to avoid protocol mixing.

## REST API (Develop Service)

Route prefix:
- `/api/developers/{pubName}/projects/{projectId}/apps/{appId}/secrets`

Create/rotate payload now uses `Type` instead of `IsOidc`:

```json
{
  "description": "AppConnect signing key",
  "expiresIn": "7.00:00:00",
  "type": "AppConnect"
}
```

Secret response includes:
- `type`

### Validate AppConnect challenge signature

Endpoint:
- `POST /api/developers/{pubName}/projects/{projectId}/apps/{appId}/app-connect/validate-challenge`

Request body:

```json
{
  "challenge": "nonce-or-challenge-payload",
  "signature": "base64-or-base64url-or-hex-signature",
  "secretId": "optional-secret-guid"
}
```

Response body:

```json
{
  "valid": true,
  "secretId": "matched-secret-guid-or-null"
}
```

Validation behavior:
- Uses active (non-expired) `AppConnect` secrets only.
- If `secretId` is provided, only that secret is checked.
- Signature algorithm is `HMAC-SHA256(secret, challengeUtf8)`.
- Signature input supports `base64`, `base64url`, or hex (`0x` prefix allowed).

## gRPC Compatibility

Current gRPC request contract still exposes optional `is_oidc` in `DyCheckCustomAppSecretRequest`.

Behavior:
- `is_oidc = true` => filter secrets as `Oidc`
- `is_oidc = false` (explicitly set) => filter secrets as `AppConnect`
- `is_oidc` not set => do not filter by type

This keeps backward compatibility while allowing typed usage in service and REST layers.

## Migration Notes

- Database schema remains unchanged (`is_oidc` is still the stored field).
- Model/API layer exposes typed `CustomAppSecretType`.
- Existing OIDC integrations continue to work.

## See also

- `/Users/littlesheep/Documents/Projects/DysonNetwork/docs/auth/APP_CONNECT.md`

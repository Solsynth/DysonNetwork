# APP_CONNECT Protocol

APP_CONNECT is DysonNetwork's non-OAuth shared-secret protocol for custom app SSO and app-to-client trust proof.

This document is the authoritative integration guide for creating, using, and validating APP_CONNECT signatures.

## Goals

- Provide a lightweight trust proof between a client and a third-party custom app.
- Keep APP_CONNECT secrets isolated from OAuth/OIDC secrets.
- Let DysonNetwork server verify proof-of-secret possession.

## Non-goals

- APP_CONNECT is not OAuth2/OIDC.
- APP_CONNECT is not encryption.
- APP_CONNECT does not replace session tokens by itself.

## Core concept

APP_CONNECT uses an app secret of type `AppConnect` and a signature over a challenge:

- Signature algorithm: `HMAC-SHA256(secret, challengeUtf8Bytes)`
- The third-party app never sends the secret itself.
- The server validates the signature against active AppConnect secrets.

## Entities

- Client: user-facing app or frontend SDK.
- Third-party app: custom app integrating with DysonNetwork.
- DysonNetwork Develop service: secret storage and signature verifier.

## Secret model

Custom app secrets are typed:

- `Oidc`: OAuth/OIDC flows
- `AppConnect`: APP_CONNECT signing

Only `AppConnect` secrets are used for APP_CONNECT challenge validation.

## Endpoints

### 1) Retrieve custom app by slug (public)

- `GET /api/apps/{slug}`

Use this to discover custom app metadata from slug before APP_CONNECT handshake.

### 2) Validate APP_CONNECT challenge signature

- `POST /api/developers/{pubName}/projects/{projectId}/apps/{appId}/app-connect/validate-challenge`

Request body:

```json
{
  "challenge": "random-string-or-payload",
  "signature": "base64/base64url/hex-signature",
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

Verifier behavior:

- Validates only active (non-expired) `AppConnect` secrets.
- If `secretId` is provided, checks only that secret.
- If `secretId` is omitted, checks all active AppConnect secrets for the app.
- Signature decoding supports:
  - base64
  - base64url
  - hex (with or without `0x` prefix)

## Recommended handshake flow

1. Client generates a cryptographically random challenge (nonce).
2. Client sends challenge to third-party app.
3. Third-party app computes HMAC-SHA256 using its AppConnect secret and returns signature (and optionally its `secretId`).
4. Client sends `challenge + signature (+ secretId)` to DysonNetwork validation endpoint.
5. If `valid=true`, client treats proof-of-secret as verified and continues its auth/session flow.

## Security requirements

### Challenge quality

- Use at least 128-bit randomness.
- Recommended format: base64url random bytes.

### Freshness and replay protection

Current server verifier validates signature correctness and secret validity, but does not persist challenge usage by itself.

Caller side must enforce freshness, for example:

- include `issuedAt` + short expiry in challenge payload,
- bind challenge to request/session context,
- store used challenge IDs/nonces and reject reuse.

### Secret hygiene

- Use separate secrets for `Oidc` and `AppConnect`.
- Rotate secrets periodically.
- Set expiration when possible.
- Revoke compromised secrets immediately.

## Message and encoding details

### Canonical signing input

Sign exact UTF-8 bytes of `challenge` as sent to verifier.

Any change in bytes (whitespace, encoding, JSON key order) changes signature.

### Signature output

Recommended wire format: base64url without padding.

Verifier also accepts standard base64 and hex for compatibility.

## Client and app examples

### JavaScript (third-party app signing)

```js
import crypto from "node:crypto";

export function signAppConnectChallenge(challenge, appConnectSecret) {
  const mac = crypto
    .createHmac("sha256", Buffer.from(appConnectSecret, "utf8"))
    .update(Buffer.from(challenge, "utf8"))
    .digest("base64url");
  return mac;
}
```

### C# (third-party app signing)

```csharp
using System.Security.Cryptography;
using System.Text;

static string SignAppConnect(string challenge, string secret)
{
    var keyBytes = Encoding.UTF8.GetBytes(secret);
    var data = Encoding.UTF8.GetBytes(challenge);
    var sig = HMACSHA256.HashData(keyBytes, data);
    return Convert.ToBase64String(sig)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
}
```

### Validate request example

```http
POST /api/developers/acme/projects/11111111-1111-1111-1111-111111111111/apps/22222222-2222-2222-2222-222222222222/app-connect/validate-challenge
Content-Type: application/json

{
  "challenge": "mR8u4FYD4Q4wG4wX7h2D7A",
  "signature": "Z2WgQff5aHkSxeh1mW4Xx7A0fS3PK0VdW8BiopUk7Bo",
  "secretId": "33333333-3333-3333-3333-333333333333"
}
```

## Operational checklist

- Create AppConnect secret for each custom app integration.
- Verify third-party app signs UTF-8 challenge bytes exactly.
- Enforce replay/freshness in caller service.
- Log failed validation attempts with app ID and reason category.
- Rotate AppConnect secrets and monitor expiration.

## Related docs

- `/Users/littlesheep/Documents/Projects/DysonNetwork/docs/auth/CUSTOM_APP_SECRET_TYPES.md`
- `/Users/littlesheep/Documents/Projects/DysonNetwork/docs/auth/UNIFIED_JWT_AUTH.md`


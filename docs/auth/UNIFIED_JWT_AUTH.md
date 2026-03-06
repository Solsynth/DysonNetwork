# Unified JWT Auth (Padlock) and Reuse Guide

## Purpose
This document defines the canonical auth contract after the unified JWT migration:
- User and session auth uses RS256 JWT (`Authorization: Bearer <token>`).
- API key auth uses a separate lane (`Authorization: Bot <token>`).
- Subservices validate JWT locally and only use Padlock gRPC as legacy fallback during grace window.

Padlock is the source of truth for token issuance, revocation, and key rotation.

## Scope and ownership
- Padlock owns auth, session lifecycle, token issuance, token revoke, API key auth lane.
- Pass and other services consume auth identity and do not issue primary user auth tokens.
- API key and user session token lanes are intentionally separate.

## Canonical token formats

### 1) User/session token
Header scheme: `Bearer`
Format: RS256 JWT

Required claims:
- `iss`: issuer
- `aud`: audience
- `sub`: account id (UUID)
- `jti`: session id (UUID)
- `sid`: session id (UUID)
- `exp`, `iat`, `nbf`
- `token_use`: `user`
- `ver`: account/session invalidation version

Standard profile/security claims used by services:
- `scope` (repeatable claim)
- `is_superuser` (`1` or `0`)
- `name`
- `nick`
- `region`

### 2) API key token
Header scheme: `Bot`
Format: RS256 JWT

Required claims:
- `iss`, `aud`, `exp`, `iat`, `nbf`
- `sub`: account id (UUID)
- `jti`: key session id (UUID)
- `sid`: key session id (UUID)
- `token_use`: `api_key`
- `api_key_id`: API key id (UUID)
- `account_id`: account id (UUID)
- `ver`: account/session invalidation version

## Header policy
- User token: `Authorization: Bearer <jwt>`
- API key: `Authorization: Bot <token>`
- Legacy temporary compatibility (during grace): `AtField` and `AkField`

Strict lane enforcement:
- `Bot` requires `token_use=api_key`.
- `Bearer` must not carry `token_use=api_key`.

## Subservice validation model
Subservices use local auth middleware (`DysonTokenAuthHandler`) and validate without per-request Padlock gRPC.

Validation steps on normal path:
1. Extract token from `Bearer` or `Bot` header.
2. Verify JWT signature (RS256) with Padlock public key.
3. Validate issuer, audience, `exp`, `nbf`.
4. Check revocation key in Redis: `auth:revoked:jti:{jti}`.
5. Check account version in Redis: `auth:account_ver:{accountId}` and compare with token `ver`.
6. Populate request context:
   - `CurrentUser`
   - `CurrentSession`
   - `CurrentTokenType`

Legacy fallback:
- If local JWT validation fails and grace config allows it, middleware can fallback to Padlock gRPC validation for legacy tokens.

## Revocation contract
Padlock writes revocation and freshness markers:
- `auth:revoked:jti:{jti}`: immediate token deny
- `auth:account_ver:{accountId}`: global invalidation/version bump

Write events happen on:
- logout
- session revoke
- revoke all sessions
- API key revoke
- API key rotate (old token revoked)

## Interactive-only endpoint policy
Some endpoints must not allow API key auth (session-sensitive flows).

Use `RequireInteractiveSessionAttribute` to deny `CurrentTokenType=ApiKey`.

Recommended targets:
- logout
- sudo/elevation
- session/device management
- account security settings requiring human session context

## Grace window and deprecation config
Current config flags:

```json
{
  "Auth": {
    "LegacyTokens": {
      "AcceptUntil": "2026-03-20T00:00:00Z"
    },
    "Headers": {
      "AcceptLegacySchemes": true
    },
    "Validation": {
      "UseGrpcFallbackForLegacy": true
    }
  }
}
```

Rollout phases:
- Phase 1 (dual-read, new-write): issue JWT, accept legacy.
- Phase 2 (hard cut): disable legacy headers and compact tokens, disable gRPC fallback.

## Required service configuration (for any repo/service)
Each service that validates locally must provide:
- `AuthToken:PublicKeyPath`
- `Authentication:Schemes:Bearer:ValidIssuer`
- `Authentication:Schemes:Bearer:ValidAudiences`
- Redis/cache connectivity for revocation/version keys

Optional compatibility flags:
- `Auth:LegacyTokens:AcceptUntil`
- `Auth:Headers:AcceptLegacySchemes`
- `Auth:Validation:UseGrpcFallbackForLegacy`

## Reuse guide for other repos/modules
To reuse this auth model in another repo:

1. Add shared auth middleware equivalent to `DysonTokenAuthHandler`.
2. Enforce `Bearer` user lane and `Bot` API key lane.
3. Validate RS256 JWT locally with Padlock public key.
4. Implement Redis checks for `auth:revoked:jti:*` and `auth:account_ver:*`.
5. Expose interactive-only policy and apply to sensitive endpoints.
6. Keep legacy compatibility flags only if migration is still in progress.

If the target repo is .NET and already references Dyson shared libraries:
- Reuse `DysonNetwork.Shared.Auth` handler and policy attribute directly.

If the target repo is not .NET:
- Implement the same claim contract and Redis revocation/version semantics.
- Keep identical header semantics (`Bearer` and `Bot`) for interoperability.

## Operational checklist
Before enabling hard cut:
- Confirm all issuers write JWT for user and API key lanes.
- Confirm all consumers validate locally.
- Confirm no critical client still sends `AtField` or `AkField`.
- Monitor auth failure reasons by type (signature, issuer, audience, expiry, revoked, stale version, wrong scheme).

After hard cut:
- Set `AcceptLegacySchemes=false`
- Set `UseGrpcFallbackForLegacy=false`
- Set `AcceptUntil` to past timestamp or remove legacy branches

## Code references
- Padlock JWT issuer/validator:
  - `/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Padlock/Auth/AuthJwtService.cs`
- Padlock auth issuance/revocation:
  - `/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Padlock/Auth/AuthService.cs`
- Shared local validation middleware:
  - `/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Shared/Auth/AuthScheme.cs`
- Interactive-only endpoint policy:
  - `/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Shared/Auth/RequireInteractiveSessionAttribute.cs`

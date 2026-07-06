# OAuth Device Flow Implementation Notes

This document describes how the Padlock OIDC provider implements the OAuth 2.0 Device Authorization Grant defined by [RFC 8628](https://datatracker.ietf.org/doc/html/rfc8628).

For application developers who want to call this provider, see [OIDC_DEVICE_PROVIDER_USAGE.md](/Users/littlesheep/Documents/Projects/SolarNetwork/DysonNetwork/docs/OIDC_DEVICE_PROVIDER_USAGE.md).

## Endpoints

Production endpoints are exposed through the gateway under `/padlock`.

| Purpose | Local route | Production route |
| --- | --- | --- |
| Device authorization | `/api/auth/open/device/code` | `/padlock/auth/open/device/code` |
| Device status | `/api/auth/open/device/code/{user_code}` | `/padlock/auth/open/device/code/{user_code}` |
| Approve device code | `/api/auth/open/device/code/{user_code}/approve` | `/padlock/auth/open/device/code/{user_code}/approve` |
| Decline device code | `/api/auth/open/device/code/{user_code}/decline` | `/padlock/auth/open/device/code/{user_code}/decline` |
| Token exchange | `/api/auth/open/token` | `/padlock/auth/open/token` |

## Request Flow

1. The client calls `POST /auth/open/device/code` with `client_id`, optional `scope`, and optional `nonce`.
2. Padlock generates:
   - a random `device_code`
   - a user-facing `user_code` in `XXXX-XXXX` format
   - a 10 minute expiration window
   - an initial polling `interval` of 5 seconds
3. The client shows `user_code` plus `verification_uri` or `verification_uri_complete`.
4. The verification UI loads device request details from `GET /auth/open/device/code/{user_code}`.
5. An authenticated interactive browser session approves or declines the request.
6. The device polls `POST /auth/open/token` with `grant_type=urn:ietf:params:oauth:grant-type:device_code`.
7. On approval, Padlock issues `access_token`, `id_token`, and `refresh_token`, then invalidates the device code.

## Stored State

Device flow state lives in cache:

- `auth:device-code:{device_code}` stores the full `DeviceCodeInfo`
- `auth:user-code:{user_code}` maps the displayed code back to `device_code`

The cached payload includes:

- `client_id`
- requested scopes
- optional OIDC `nonce`
- `status`
- `created_at`
- `expires_at`
- `polling_interval_seconds`
- `last_polled_at`
- approval metadata

## Polling Behavior

Padlock now tracks device polling cadence.

- Initial `interval` is 5 seconds.
- If the client polls before the current interval elapses, the token endpoint returns `slow_down`.
- Each `slow_down` response increases the stored polling interval by 5 seconds for subsequent attempts.
- If the request is still waiting on user approval, the token endpoint returns `authorization_pending`.

## Status Endpoint Contract

`GET /api/auth/open/device/code/{user_code}` returns display-friendly data for the verification page:

```json
{
  "user_code": "WDJB-MJHT",
  "client_id": "my-cli-tool",
  "client_name": "My CLI Tool",
  "client_slug": "my-cli-tool",
  "scopes": ["openid", "profile", "email"],
  "status": "pending",
  "expires_at": "2026-07-06T12:15:00Z",
  "expires_in": 534,
  "interval": 5,
  "verification_uri": "https://solsynth.dev/auth/device"
}
```

Notes:

- `client_id` is returned as the app slug when available, otherwise the app UUID.
- `user_code` lookup is normalized to uppercase trimmed input.
- expired or missing codes return `404 not_found`.

## Approval Rules

Approval and decline endpoints require:

- a valid authenticated session
- an interactive session via `RequireInteractiveSession`
- the device code still being `pending`
- the device code not being expired

On approval, the device code is bound to:

- `account_id`
- `approved_at`
- `approved_by_session_id`

## Token Exchange Rules

`POST /api/auth/open/token` supports the device flow grant:

```text
grant_type=urn:ietf:params:oauth:grant-type:device_code
```

Rules:

- `client_id` is always required
- confidential clients must also provide a valid `client_secret`
- the device code must belong to the same client
- expired codes return `expired_token`
- declined codes return `access_denied`
- early polling returns `slow_down`
- pending approvals return `authorization_pending`

On success:

- Padlock reuses an existing valid OAuth session for the same account and client when possible
- otherwise it creates a fresh OAuth session
- the device code and user code cache entries are deleted

## Discovery

The OpenID discovery document advertises the device flow:

```json
{
  "device_authorization_endpoint": "https://api.solsynth.dev/padlock/auth/open/device/code",
  "grant_types_supported": [
    "authorization_code",
    "refresh_token",
    "urn:ietf:params:oauth:grant-type:device_code"
  ]
}
```

## Current Defaults

- Device code lifetime: 10 minutes
- Initial polling interval: 5 seconds
- Slow-down increment: 5 seconds
- User code format: `XXXX-XXXX`

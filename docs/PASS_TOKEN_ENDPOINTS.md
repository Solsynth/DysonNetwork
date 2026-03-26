# Padlock Token Endpoints and Refresh Flow

## Summary

Padlock now uses a unified JWT model with short-lived access tokens and refresh tokens.

- Access token is used for API authentication.
- Refresh token is used only to mint a new access token and extend session lifetime.
- Session lifetime is extended only during refresh, not on normal authenticated requests.

## Why there are still two token endpoints

There are two endpoints because they serve two different protocols:

1. `POST /api/auth/token`
   - First-party app login/session token exchange.
2. `POST /api/auth/open/token`
   - OAuth/OIDC provider token endpoint for third-party clients.

This split is protocol-level, not token-format-level.

## Endpoint 1: First-party auth token endpoint

`POST /api/auth/token`

### Grant: `authorization_code`

Request body:

```json
{
  "grant_type": "authorization_code",
  "code": "<challenge-id>"
}
```

Response:

```json
{
  "token": "<access-jwt>",
  "refresh_token": "<refresh-jwt>",
  "expires_in": 3600,
  "refresh_expires_in": 2592000
}
```

### Grant: `refresh_token`

Request body:

```json
{
  "grant_type": "refresh_token",
  "refresh_token": "<refresh-jwt>"
}
```

`refresh_token` can also be read from `RefreshToken` cookie if omitted in body.

Response:

```json
{
  "token": "<new-access-jwt>",
  "refresh_token": "<new-refresh-jwt>",
  "expires_in": 3600,
  "refresh_expires_in": 2592000
}
```

### Cookies

On successful token exchange, Padlock sets:

- `AuthToken` cookie: access token, expires at access token expiry.
- `RefreshToken` cookie: refresh token, expires at refresh token expiry.

On `POST /api/auth/logout`, both cookies are deleted.

## Endpoint 2: OAuth/OIDC provider token endpoint

`POST /api/auth/open/token` (`application/x-www-form-urlencoded`)

### Grant: `authorization_code`

Required:

- `grant_type=authorization_code`
- `client_id`
- `client_secret`
- `code`

Optional:

- `redirect_uri`
- `code_verifier` (PKCE)

Response fields:

- `access_token`
- `refresh_token`
- `id_token`
- `expires_in`
- `token_type`
- `scope`

### Grant: `refresh_token`

Required:

- `grant_type=refresh_token`
- `client_id`
- `client_secret`
- `refresh_token`

Padlock validates that refresh token/session belongs to the same client app.

## Token use rules

- Access token claim: `token_use=user` (or `api_key` for bot lane).
- Refresh token claim: `token_use=refresh`.
- Refresh tokens are rejected as bearer tokens on normal API authentication path.

## Session lifecycle

1. Login creates session and returns access + refresh token.
2. Access token expiry does not log user out immediately if refresh token is still valid.
3. Refresh call:
   - validates refresh token (`exp`, signature, revocation, account version, session/client binding),
   - extends session expiry,
   - returns new access + refresh token pair.
4. Logout/revoke invalidates session and revokes token `jti`.

## Config

In `DysonNetwork.Padlock/appsettings.json`:

```json
"AuthToken": {
  "AccessTokenLifetime": "01:00:00",
  "RefreshTokenLifetime": "30.00:00:00"
}
```

- `AccessTokenLifetime`: access JWT lifetime.
- `RefreshTokenLifetime`: refresh session window (also refresh JWT expiry baseline).

## Migration notes

- Old compact/legacy token support still follows `Auth.LegacyTokens.*` compatibility settings.
- New clients should use `Bearer` + refresh flow and stop relying on long-lived access token cookies.

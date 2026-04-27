# OIDC Public Client Support

This document describes the support for OAuth 2.0 Public Clients (frontend-only applications) using PKCE (Proof Key for Code Exchange) for secure authentication.

## Overview

Public clients are applications that cannot securely store a client secret, such as:
- Single-page applications (SPA)
- Mobile apps
- Desktop applications
- Native apps

For these clients, we implement PKCE as the security mechanism instead of client secrets.

## Client Types

| Type | `is_public_client` | Secret Required | PKCE Required |
|------|---------------------|-----------------|---------------|
| Confidential | `false` (default) | Yes | Optional |
| Public | `true` | No | Yes (enforced) |

## Configuration

### OAuth Config Properties

When creating or updating a custom app, set the `oauth_config`:

```json
{
  "oauth_config": {
    "client_uri": "https://myapp.example.com",
    "redirect_uris": ["https://myapp.example.com/callback"],
    "post_logout_redirect_uris": ["https://myapp.example.com/logout"],
    "allowed_scopes": ["openid", "profile", "email"],
    "allowed_grant_types": ["authorization_code", "refresh_token"],
    "require_pkce": true,
    "allow_offline_access": false,
    "is_public_client": true
  }
}
```

### Key Properties

- **`is_public_client`**: Set to `true` for frontend-only apps. This enables PKCE-only authentication.
- **`require_pkce`**: For public clients, this is automatically enforced regardless of the value.
- **`redirect_uris`**: Valid redirect URIs. Required for production apps, optional for development.

## Authorization Flow

### 1. Generate PKCE Verifier and Challenge

Before starting the authorization flow, generate a code verifier and code challenge:

```javascript
// Generate a random code verifier (43-128 characters)
function generateCodeVerifier() {
  const array = new Uint8Array(32);
  crypto.getRandomValues(array);
  return base64UrlEncode(array);
}

// Generate code challenge from verifier
async function generateCodeChallenge(verifier) {
  const encoder = new TextEncoder();
  const data = encoder.encode(verifier);
  const digest = await crypto.subtle.digest('SHA-256', data);
  return base64UrlEncode(new Uint8Array(digest));
}

function base64UrlEncode(buffer) {
  let str = '';
  const bytes = new Uint8Array(buffer);
  for (let i = 0; i < bytes.length; i++) {
    str += String.fromCharCode(bytes[i]);
  }
  return btoa(str)
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=/g, '');
}
```

### 2. Authorization Request

Redirect the user to the authorization endpoint:

```
GET /api/auth/open/authorize?
    client_id={client_slug}&
    response_type=code&
    redirect_uri={redirect_uri}&
    scope=openid profile email&
    state={random_state}&
    code_challenge={code_challenge}&
    code_challenge_method=S256
```

**Parameters:**

| Parameter | Required | Description |
|-----------|----------|-------------|
| `client_id` | Yes | The client slug |
| `response_type` | Yes | Must be `code` |
| `redirect_uri` | Yes* | Must match registered URI |
| `scope` | No | Requested scopes |
| `state` | Recommended | CSRF protection |
| `code_challenge` | Yes (public clients) | PKCE code challenge |
| `code_challenge_method` | Yes (public clients) | Must be `S256` |

*Redirect URI validation:
- **Production apps**: Must match exactly (or wildcard pattern) registered URIs
- **Non-production apps** (Developing/Staging): Any redirect URI allowed

### 3. User Authentication & Consent

The user will:
1. Authenticate if not already logged in
2. Review the requested permissions
3. Approve or deny the authorization

### 4. Authorization Response

On approval, the user is redirected back with an authorization code:

```
{redirect_uri}?code={authorization_code}&state={state}
```

### 5. Token Exchange

Exchange the authorization code for tokens:

```http
POST /api/auth/open/token
Content-Type: application/x-www-form-urlencoded

grant_type=authorization_code&
code={authorization_code}&
redirect_uri={redirect_uri}&
client_id={client_slug}&
code_verifier={code_verifier}
```

**Note:** For public clients, `client_secret` is NOT required.

**Response:**

```json
{
  "access_token": "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9...",
  "id_token": "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9...",
  "refresh_token": "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9...",
  "token_type": "Bearer",
  "expires_in": 3600,
  "scope": "openid profile email"
}
```

### 6. Refresh Token Flow

To refresh an access token:

```http
POST /api/auth/open/token
Content-Type: application/x-www-form-urlencoded

grant_type=refresh_token&
refresh_token={refresh_token}&
client_id={client_slug}
```

**Note:** For public clients, `client_secret` is NOT required.

## OIDC Discovery

The OIDC discovery endpoint advertises support for public clients:

```http
GET /.well-known/openid-configuration
```

Key fields:

```json
{
  "token_endpoint_auth_methods_supported": [
    "client_secret_basic",
    "client_secret_post",
    "none"
  ],
  "code_challenge_methods_supported": ["S256"]
}
```

The `"none"` value in `token_endpoint_auth_methods_supported` indicates support for public clients.

## Security Considerations

### PKCE Enforcement

For public clients (`is_public_client: true`):

1. **Authorization Request**: `code_challenge` is REQUIRED
2. **Token Request**: `code_verifier` is REQUIRED
3. **No Client Secret**: The token endpoint will reject requests with `client_secret` for public clients

### Redirect URI Validation

- **Production apps**: Redirect URIs must match registered URIs exactly or match wildcard patterns (e.g., `*.example.com`)
- **Non-production apps**: Redirect URI validation is relaxed for development convenience

### App Status and Redirect URI Validation

| App Status | Redirect URI Validation |
|------------|------------------------|
| Developing | Disabled (any URI allowed) |
| Staging | Disabled (any URI allowed) |
| Production | Enabled (must match registered URIs) |
| Suspended | Disabled |

## Example: Single-Page Application

```javascript
class OAuthClient {
  constructor(clientId, redirectUri) {
    this.clientId = clientId;
    this.redirectUri = redirectUri;
    this.codeVerifier = null;
  }

  async startAuth() {
    // Generate PKCE parameters
    this.codeVerifier = this.generateCodeVerifier();
    const codeChallenge = await this.generateCodeChallenge(this.codeVerifier);
    const state = this.generateState();

    // Store verifier and state for later
    sessionStorage.setItem('pkce_verifier', this.codeVerifier);
    sessionStorage.setItem('oauth_state', state);

    // Build authorization URL
    const params = new URLSearchParams({
      client_id: this.clientId,
      response_type: 'code',
      redirect_uri: this.redirectUri,
      scope: 'openid profile email',
      state: state,
      code_challenge: codeChallenge,
      code_challenge_method: 'S256'
    });

    // Redirect to authorization endpoint
    window.location.href = `/api/auth/open/authorize?${params}`;
  }

  async handleCallback() {
    const params = new URLSearchParams(window.location.search);
    const code = params.get('code');
    const state = params.get('state');

    // Verify state
    const storedState = sessionStorage.getItem('oauth_state');
    if (state !== storedState) {
      throw new Error('State mismatch');
    }

    // Get stored verifier
    const codeVerifier = sessionStorage.getItem('pkce_verifier');

    // Exchange code for tokens
    const response = await fetch('/api/auth/open/token', {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: new URLSearchParams({
        grant_type: 'authorization_code',
        code: code,
        redirect_uri: this.redirectUri,
        client_id: this.clientId,
        code_verifier: codeVerifier
      })
    });

    return response.json();
  }

  generateCodeVerifier() {
    const array = new Uint8Array(32);
    crypto.getRandomValues(array);
    return this.base64UrlEncode(array);
  }

  async generateCodeChallenge(verifier) {
    const encoder = new TextEncoder();
    const data = encoder.encode(verifier);
    const digest = await crypto.subtle.digest('SHA-256', data);
    return this.base64UrlEncode(new Uint8Array(digest));
  }

  generateState() {
    const array = new Uint8Array(16);
    crypto.getRandomValues(array);
    return this.base64UrlEncode(array);
  }

  base64UrlEncode(buffer) {
    let str = '';
    const bytes = new Uint8Array(buffer);
    for (let i = 0; i < bytes.length; i++) {
      str += String.fromCharCode(bytes[i]);
    }
    return btoa(str).replace(/\+/g, '-').replace(/\//g, '_').replace(/=/g, '');
  }
}

// Usage
const client = new OAuthClient('my-app-slug', 'https://myapp.example.com/callback');

// Start auth flow
await client.startAuth();

// Handle callback (on redirect page)
const tokens = await client.handleCallback();
console.log('Access Token:', tokens.access_token);
```

## API Reference

### Authorization Endpoint

```
GET /api/auth/open/authorize
POST /api/auth/open/authorize
```

### Token Endpoint

```
POST /api/auth/open/token
```

### User Info Endpoint

```
GET /api/auth/open/userinfo
Authorization: Bearer {access_token}
```

### Discovery Endpoint

```
GET /.well-known/openid-configuration
```

### JWKS Endpoint

```
GET /.well-known/jwks
```

## Error Responses

| Error | Description |
|-------|-------------|
| `invalid_request` | Missing required parameter |
| `unauthorized_client` | Client not found |
| `invalid_client` | Invalid client credentials (confidential clients) |
| `invalid_grant` | Invalid or expired authorization code/refresh token |
| `unsupported_grant_type` | Grant type not supported |

## Migration Guide

### From Confidential to Public Client

1. Update the app's OAuth config:
   ```json
   {
     "oauth_config": {
       "is_public_client": true
     }
   }
   ```

2. Remove client secret handling from your application code

3. Add PKCE support to your authorization flow

4. Delete any existing OIDC secrets (no longer needed)

### From Public to Confidential Client

1. Update the app's OAuth config:
   ```json
   {
     "oauth_config": {
       "is_public_client": false
     }
   }
   ```

2. Generate an OIDC secret for your application

3. Update your application to include `client_secret` in token requests

4. PKCE becomes optional (but still recommended)

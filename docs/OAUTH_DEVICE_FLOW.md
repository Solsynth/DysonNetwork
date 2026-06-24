# OAuth Device Authorization Grant

Device authorization allows users to authenticate on devices with limited input capabilities (smart TVs, CLI tools, game consoles) by entering a user code on a separate device with a browser.

This implements [RFC 8628](https://tools.ietf.org/html/rfc8628) (OAuth 2.0 Device Authorization Grant).

## Overview

```
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│  Device Client  │     │     Redis       │     │  User's Browser │
│ (limited input) │     │                 │     │ (authenticated) │
│                 │     │                 │     │                 │
│ 1. Request Code │────▶│ Store Device    │     │                 │
│                 │     │ Code (10min TTL)│     │                 │
│ 2. Show User    │     │                 │     │                 │
│    Code + URI   │     │                 │     │                 │
│                 │     │                 │     │ 3. Enter Code   │
│                 │     │                 │◀────│    & Approve    │
│                 │     │                 │     │                 │
│ 4. Poll Token   │────▶│ Validate        │     │                 │
│  (every 5s)     │     │                 │     │                 │
│                 │◀────│ Return Tokens   │     │                 │
│ 5. Authenticated│     │                 │     │                 │
└─────────────────┘     └─────────────────┘     └─────────────────┘
```

## Grant Type

```
urn:ietf:params:oauth:grant-type:device_code
```

## Endpoints

All endpoints are prefixed with `/padlock` in production.

### Request Device Code

Initiate the device authorization flow.

```
POST /api/auth/open/device/code
Content-Type: application/x-www-form-urlencoded
```

**Request Body:**

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `client_id` | string | ✅ | OAuth client identifier (slug or UUID) |
| `scope` | string | ❌ | Space-delimited list of scopes |
| `nonce` | string | ❌ | Nonce for ID token |

**Example:**

```bash
curl -X POST https://api.solsynth.dev/padlock/auth/open/device/code \
  -d "client_id=my-cli-tool" \
  -d "scope=openid profile email"
```

**Response:**

```json
{
  "device_code": "GmRhmhcxhwAzkoEqiMEg_DnyEysNkuNhszIySk9eS",
  "user_code": "WDJB-MJHT",
  "verification_uri": "https://solsynth.dev/auth/device",
  "verification_uri_complete": "https://solsynth.dev/auth/device?code=WDJB-MJHT",
  "expires_in": 600,
  "interval": 5
}
```

| Field | Description |
|-------|-------------|
| `device_code` | Used by the device to poll for tokens |
| `user_code` | Displayed to the user, entered on verification page |
| `verification_uri` | URL where user enters the code |
| `verification_uri_complete` | Direct link with code pre-filled |
| `expires_in` | Seconds until device code expires |
| `interval` | Minimum seconds between polling requests |

### Check Device Code Status

Check the current status of a device code (used by the verification page).

```
GET /api/auth/open/device/code/{user_code}
```

**Response:**

```json
{
  "user_code": "WDJB-MJHT",
  "client_id": "550e8400-e29b-41d4-a716-446655440000",
  "scopes": ["openid", "profile", "email"],
  "status": "pending",
  "expires_at": "2024-01-15T10:15:00Z"
}
```

**Status Values:**

| Status | Description |
|--------|-------------|
| `pending` | Awaiting user approval |
| `approved` | User approved, device can exchange for tokens |
| `declined` | User declined the request |
| `expired` | Device code expired |

### Approve Device Code

Approve the device authorization from an authenticated browser session.

```
POST /api/auth/open/device/code/{user_code}/approve
Authorization: Bearer <access_token>
```

**Response:** `200 OK`

### Decline Device Code

Decline the device authorization from an authenticated browser session.

```
POST /api/auth/open/device/code/{user_code}/decline
Authorization: Bearer <access_token>
```

**Response:** `200 OK`

### Token Exchange

Exchange the device code for tokens after approval.

```
POST /api/auth/open/token
Content-Type: application/x-www-form-urlencoded
```

**Request Body:**

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `grant_type` | string | ✅ | `urn:ietf:params:oauth:grant-type:device_code` |
| `device_code` | string | ✅ | The device code from the initial request |
| `client_id` | string | ✅ | Same client ID used in initial request |
| `client_secret` | string | ❌ | Required for confidential clients |

**Example:**

```bash
curl -X POST https://api.solsynth.dev/padlock/auth/open/token \
  -d "grant_type=urn:ietf:params:oauth:grant-type:device_code" \
  -d "device_code=GmRhmhcxhwAzkoEqiMEg_DnyEysNkuNhszIySk9eS" \
  -d "client_id=my-cli-tool"
```

**Success Response (after approval):**

```json
{
  "access_token": "eyJhbGciOiJSUzI1NiIs...",
  "id_token": "eyJhbGciOiJSUzI1NiIs...",
  "refresh_token": "dGhpcyBpcyBhIHJlZnJl...",
  "expires_in": 3600,
  "token_type": "Bearer",
  "scope": "openid profile email"
}
```

**Error Responses (while pending or on failure):**

| Error | Description |
|-------|-------------|
| `authorization_pending` | User hasn't approved yet, keep polling |
| `slow_down` | Polling too frequently |
| `expired_token` | Device code expired |
| `access_denied` | User declined |

**Example Error:**

```json
{
  "error": "authorization_pending",
  "error_description": "Authorization pending."
}
```

## Client Implementation

### Device Client (CLI, TV, etc.)

```javascript
// Step 1: Request device code
const codeResp = await fetch('/padlock/auth/open/device/code', {
  method: 'POST',
  headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
  body: 'client_id=my-cli-tool&scope=openid profile'
});
const { device_code, user_code, verification_uri, interval } = await codeResp.json();

// Step 2: Display to user
console.log(`Visit ${verification_uri} and enter code: ${user_code}`);

// Step 3: Poll for tokens
while (true) {
  await sleep(interval * 1000);
  
  const tokenResp = await fetch('/padlock/auth/open/token', {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body: `grant_type=urn:ietf:params:oauth:grant-type:device_code&device_code=${device_code}&client_id=my-cli-tool`
  });
  
  const tokens = await tokenResp.json();
  
  if (tokens.error === 'authorization_pending') continue;
  if (tokens.error === 'slow_down') { interval += 5; continue; }
  if (tokens.error) throw new Error(tokens.error_description);
  
  // Success!
  console.log('Authenticated!', tokens.access_token);
  break;
}
```

### Verification Page (Web App)

```javascript
// Step 1: Get code from URL or user input
const urlParams = new URLSearchParams(window.location.search);
const userCode = urlParams.get('code') || document.getElementById('code-input').value;

// Step 2: Check status and get client info
const statusResp = await fetch(`/padlock/auth/open/device/code/${userCode}`);
const status = await statusResp.json();

if (status.status === 'pending') {
  // Show approval UI with client name and requested scopes
  document.getElementById('client-name').textContent = status.client_id;
  document.getElementById('scopes').textContent = status.scopes.join(', ');
}

// Step 3: Handle approval
async function approve() {
  await fetch(`/padlock/auth/open/device/code/${userCode}/approve`, {
    method: 'POST',
    headers: { 'Authorization': `Bearer ${accessToken}` }
  });
  alert('Device authorized! You can close this page.');
}
```

## Discovery

The device authorization endpoint is advertised in the OpenID Connect discovery document:

```
GET /.well-known/openid-configuration
```

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

## Security Considerations

- Device codes expire after **10 minutes**
- User codes are **8 characters** (format: `XXXX-XXXX`, consonants only to avoid ambiguous words)
- Minimum polling interval is **5 seconds**; clients should respect the `interval` field
- Device codes are single-use; successful token exchange invalidates the code
- Public clients don't need `client_secret`; confidential clients do
- User approval requires an active authenticated session

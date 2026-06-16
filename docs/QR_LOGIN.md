# QR Code Login

QR code login allows users to authenticate on a web/desktop client by scanning a QR code with their authenticated mobile device.

## Auth Factor

QR Login is a first-class authentication factor with:

| Property | Value |
|----------|-------|
| Type | `QrLogin` (int: `8`) |
| Trustworthy | 3 |
| Secret | None (verification is delegated to mobile approval) |

Users must enable the QR Login factor before they can approve login requests from other devices. This can be managed through the account security settings using the standard factor endpoints.

```bash
# Create QR Login factor
POST /api/factors
{ "type": 8 }
```

## Overview

```
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│   Web/Desktop   │     │     Redis       │     │   Mobile App    │
│                 │     │                 │     │                 │
│ 1. Generate QR  │────▶│ Store Challenge │     │                 │
│                 │     │ (5min TTL)      │     │                 │
│ 2. Display QR   │     │                 │     │                 │
│    solian://... │     │                 │     │                 │
│                 │     │                 │     │                 │
│ 3. Poll Status  │────▶│ Get Status      │     │                 │
│                 │     │                 │     │ 4. Scan & Approve│
│                 │◀────│ Update Status   │◀────│                 │
│ 5. WebSocket    │     │                 │     │                 │
│    notification │     │                 │     │                 │
│                 │     │                 │     │                 │
│ 6. Exchange     │────▶│ Validate        │     │                 │
│    Tokens       │     │                 │     │                 │
└─────────────────┘     └─────────────────┘     └─────────────────┘
```

## Endpoints

All endpoints are prefixed with `/padlock` in production.

### Generate QR Challenge

Create a new QR code challenge for authentication.

```
POST /api/auth/qr/generate
```

**Request Body:**

```json
{
  "device_id": "web-a1b2c3d4",
  "device_name": "Chrome on macOS",
  "platform": "Web",
  "audiences": [],
  "scopes": []
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `device_id` | string | ✅ | Unique identifier for the client device |
| `device_name` | string | ❌ | Human-readable device name |
| `platform` | enum | ✅ | `Web`, `Ios`, `Android`, `MacOs`, `Windows`, `Linux`, `Unidentified` |
| `audiences` | string[] | ❌ | OAuth audiences |
| `scopes` | string[] | ❌ | OAuth scopes |

**Response:**

```json
{
  "qr_challenge_id": "550e8400-e29b-41d4-a716-446655440000",
  "auth_challenge_id": "6ba7b810-9dad-11d1-80b4-00c04fd430c8",
  "qr_data": "solian://auth/qr/550e8400-e29b-41d4-a716-446655440000",
  "expires_at": "2024-01-15T10:35:00Z",
  "expires_in_seconds": 300
}
```

### Poll QR Status

Check the current status of a QR challenge.

```
GET /api/auth/qr/{id}
```

**Response:**

```json
{
  "qr_challenge_id": "550e8400-e29b-41d4-a716-446655440000",
  "auth_challenge_id": "6ba7b810-9dad-11d1-80b4-00c04fd430c8",
  "status": "Pending",
  "expires_at": "2024-01-15T10:35:00Z",
  "approved_at": null,
  "approved_device_id": null
}
```

**Status Values:**

| Status | Description |
|--------|-------------|
| `Pending` | Awaiting scan |
| `Scanned` | QR code scanned, awaiting approval |
| `Approved` | Login approved by mobile device |
| `Declined` | Login declined by mobile device |

### Scan QR Code (Optional)

Mark a QR challenge as scanned. This is optional but provides better UX by showing "Scanned!" on the web client.

**Requires:** QR Login factor enabled

```
POST /api/auth/qr/{id}/scan
Authorization: Bearer <mobile_access_token>
```

### Approve QR Login

Approve the login from an authenticated mobile session.

**Requires:** QR Login factor enabled

```
POST /api/auth/qr/{id}/approve
Authorization: Bearer <mobile_access_token>
```

### Decline QR Login

Decline the login from an authenticated mobile session.

**Requires:** QR Login factor enabled

```
POST /api/auth/qr/{id}/decline
Authorization: Bearer <mobile_access_token>
```

## WebSocket Events

The web/desktop client receives real-time updates via WebSocket:

### `auth.qr.scanned`

Sent when the mobile device scans the QR code.

```json
{
  "type": "auth.qr.scanned",
  "payload": {
    "qr_challenge_id": "550e8400-e29b-41d4-a716-446655440000",
    "scanned_by_device": "session-id"
  }
}
```

### `auth.qr.approved`

Sent when the mobile device approves the login.

```json
{
  "type": "auth.qr.approved",
  "payload": {
    "qr_challenge_id": "550e8400-e29b-41d4-a716-446655440000",
    "auth_challenge_id": "6ba7b810-9dad-11d1-80b4-00c04fd430c8",
    "approved_by_device": "session-id"
  }
}
```

### `auth.qr.declined`

Sent when the mobile device declines the login.

```json
{
  "type": "auth.qr.declined",
  "payload": {
    "qr_challenge_id": "550e8400-e29b-41d4-a716-446655440000",
    "declined_by_device": "session-id"
  }
}
```

## Token Exchange

After receiving `auth.qr.approved`, exchange the challenge for tokens:

```
POST /api/auth/token
Content-Type: application/json

{
  "grant_type": "authorization_code",
  "code": "6ba7b810-9dad-11d1-80b4-00c04fd430c8"
}
```

**Response:**

```json
{
  "token": "eyJhbGciOi...",
  "refresh_token": "eyJhbGciOi...",
  "expires_in": 3600,
  "refresh_expires_in": 2592000
}
```

## QR Code Format

The QR code should encode a URI with the following format:

```
solian://auth/qr/{qr_challenge_id}
```

| Component | Description |
|-----------|-------------|
| Scheme | `solian://` |
| Host | `auth` |
| Path | `/qr/{qr_challenge_id}` |

## Implementation Example

### Web/Desktop Client

```javascript
// 1. Generate QR challenge
const response = await fetch('/api/auth/qr/generate', {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({
    device_id: getDeviceId(),
    device_name: navigator.userAgent,
    platform: 'Web'
  })
});
const { qr_data, qr_challenge_id } = await response.json();

// 2. Display QR code
renderQRCode(qr_data);

// 3. Poll for status or listen to WebSocket
ws.on('auth.qr.approved', async (payload) => {
  if (payload.qr_challenge_id === qr_challenge_id) {
    // 4. Exchange for tokens
    const tokenResponse = await fetch('/api/auth/token', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        grant_type: 'authorization_code',
        code: payload.auth_challenge_id
      })
    });
    const tokens = await tokenResponse.json();
    // Store tokens and redirect
  }
});
```

### Mobile App

```javascript
// 1. Scan QR code and extract challenge ID
const qrData = await scanQRCode();
const qrChallengeId = extractChallengeId(qrData); // solian://auth/qr/{id}

// 2. Approve the challenge
await fetch(`/api/auth/qr/${qrChallengeId}/approve`, {
  method: 'POST',
  headers: {
    'Content-Type': 'application/json',
    'Authorization': `Bearer ${accessToken}`
  }
});
```

## Security Considerations

1. **Short Expiry**: QR challenges expire after 5 minutes
2. **Single Use**: Each QR code can only be approved once
3. **Account Binding**: Account is verified at approval time, not generation time
4. **HTTPS Only**: All API calls must use HTTPS
5. **Authenticated Approval**: Mobile must be authenticated to approve

## Cache Keys (Redis)

| Key Pattern | TTL | Description |
|-------------|-----|-------------|
| `auth:qr:{qr_challenge_id}` | 5 min | QR challenge data |
| `auth:qr:auth:{auth_challenge_id}` | 5 min | Maps auth challenge to QR challenge |

## Error Responses

| Status | Code | Description |
|--------|------|-------------|
| 400 | `QR_CHALLENGE_EXPIRED` | QR challenge has expired |
| 400 | `QR_CHALLENGE_NOT_PENDING` | QR challenge is no longer pending |
| 404 | `QR_CHALLENGE_NOT_FOUND` | QR challenge not found or expired |
| 401 | `UNAUTHORIZED` | Missing or invalid authentication |

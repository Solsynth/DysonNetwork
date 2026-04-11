# Key Package Status API

## Overview

The Key Package (KP) Status API allows clients to check whether their devices have sufficient key packages for MLS operations, and receive notifications when key packages are running low.

Key packages are consumed when a device joins an MLS group. To maintain continuous service, devices should have at least 3 non-consumed key packages available at all times.

## KP Status Endpoint

### GET /api/e2ee/mls/kp/status

Returns the key package status for all devices belonging to the authenticated user.

**Requires:** `X-Client-Ability: chat.mls.v2` header (same as other MLS endpoints)

**Response:**

```json
{
  "needsMoreKps": true,
  "devicesNeedingKps": [
    {
      "deviceId": "device-uuid-123",
      "deviceLabel": "My iPhone",
      "availableCount": 1
    }
  ]
}
```

**Fields:**

| Field | Type | Description |
|-------|------|-------------|
| `needsMoreKps` | `bool` | `true` if any device has fewer than 3 non-consumed KPs |
| `devicesNeedingKps` | `array` | List of devices that need more KPs |
| `devicesNeedingKps[].deviceId` | `string` | The MLS device ID |
| `devicesNeedingKps[].deviceLabel` | `string?` | Optional human-readable device label |
| `devicesNeedingKps[].availableCount` | `int` | Number of non-consumed KPs currently available |

**When `needsMoreKps` is `true`, clients should:**
1. Generate new key packages for the listed devices
2. Upload them via `PUT /api/e2ee/mls/devices/me/kps`

## KP Depleted WebSocket Notification

When a key package is consumed (via `GET /api/e2ee/mls/keys/{accountId}/devices?consume=true`), the server automatically checks if the device now has fewer than 3 non-consumed KPs. If so, it sends a WebSocket notification to the user.

**Packet Type:** `e2ee.kp.depleted`

**Delivery:** Sent via Ring's `RemoteRingService.SendWebSocketPacketToUser()` using account-level push (since ring device ID != MLS device ID).

**Payload (encoded via `InfraObjectCoder.ConvertObjectToByteString`):**

```json
{
  "mlsDeviceId": "device-uuid-123",
  "deviceId": "device-uuid-123",
  "deviceLabel": "My iPhone",
  "availableCount": 2
}
```

**Fields:**

| Field | Type | Description |
|-------|------|-------------|
| `mlsDeviceId` | `string` | The MLS device ID whose KPs are running low |
| `deviceId` | `string` | Same as `mlsDeviceId` (included for compatibility) |
| `deviceLabel` | `string?` | Human-readable device label |
| `availableCount` | `int` | Remaining non-consumed KPs after consumption |

**Client Behavior on Receiving `e2ee.kp.depleted`:**
1. Parse the payload
2. Identify the `mlsDeviceId` that needs more KPs
3. Generate and upload new key packages for that device

## Threshold Constants

| Constant | Value | Description |
|----------|-------|-------------|
| `MinKeyPackagesPerDevice` | 3 | Minimum non-consumed KPs before notification triggered |
| `MlsKeyPackageDailyLimitPerAccount` | 10 | Max KPs uploadable per account per 24 hours |
| `MlsKeyPackageRetentionDays` | 30 | KPs older than this are auto-purged |

## Example Flow

1. **Client checks status:**
   ```
   GET /api/e2ee/mls/kp/status
   ```
   Response: `{"needsMoreKps": true, "devicesNeedingKps": [{"deviceId": "abc", "availableCount": 2}]}`

2. **Client uploads new KPs:**
   ```
   PUT /api/e2ee/mls/devices/me/kps
   Body: {"keyPackage": <bytes>, "deviceId": "abc", "deviceLabel": "My iPhone"}
   ```

3. **Later, KPs are consumed during group join:**
   ```
   GET /api/e2ee/mls/keys/{accountId}/devices?consume=true
   ```

4. **Server detects KP depletion** (remaining < 3) and sends WS packet:
   - Type: `e2ee.kp.depleted`
   - To: user account
   - Payload: `{"mlsDeviceId": "abc", "availableCount": 2}`

5. **Client receives notification and uploads more KPs** (back to step 2)

# Pins API

This document describes the server-based location sharing feature (Pins) implemented in Passport.

## Overview

Pins allow users to share their real-time location with others based on visibility settings.

- A user creates a pin to broadcast their location
- The user's device periodically updates the pin location (rate limited to 1 update per 5 seconds)
- Location updates are cached in-memory and flushed to the database every 30 seconds
- Other users can discover nearby pins based on visibility settings (no 5km range limit)
- When a user disconnects, the pin can either be kept (with 24h TTL) or removed
- One user can only have one active pin at a time; creating a new one replaces the existing

## Status

`LocationPinStatus`

- `Active` - Pin is actively being updated
- `Offline` - Pin is no longer receiving updates but still visible with last known location
- `Removed` - Pin has been deleted or expired

## Data Shape

The core model is `SnLocationPin`.

Important fields:

- `id`: pin ID
- `account_id`: owner account ID
- `device_id`: device that created the pin
- `visibility`: `public`, `private`, or `unlisted`
- `status`: current pin status
- `last_heartbeat_at`: last time the pin received an update
- `expires_at`: set when pin goes offline (24h from disconnect)
- `location_name`: optional human-friendly label
- `location_address`: optional address text
- `location_wkt`: optional geometry serialized as WKT for API responses
- `keep_on_disconnect`: whether to keep the pin after disconnect
- `metadata`: optional JSON payload for client-defined extra info

## Visibility

`LocationVisibility` (shared with Meet)

- `Public` (0) - Anyone can see the pin
- `Private` (1) - Only the owner can see the pin
- `Unlisted` (2) - Only friends can see the pin

### Public
Public pins are visible to all users regardless of friendship.

### Private
Private pins are only visible to the owner. They do not appear in any discover queries.

### Unlisted
Unlisted pins are visible only to friends of the owner.

## Authentication

All pin endpoints require authentication via the `Authorization` header (Bearer token).

The current user is read from `HttpContext.Items["CurrentUser"]`.

Device ID is passed via the `X-Device-Id` header.

## Endpoints

### Create pin

`POST /api/pins`

Creates a new pin for the current user. If an active pin already exists for this user, it is replaced.

Request headers:
- `Authorization: Bearer <token>`
- `X-Device-Id: <device-id>`

Request body:

```json
{
  "visibility": 0,
  "location_name": "Home",
  "location_address": "123 Main St",
  "location_wkt": "POINT(121.5170 25.0478)",
  "metadata": {
    "emoji": "🏠"
  },
  "keep_on_disconnect": true
}
```

Response: `SnLocationPin` object

### Update pin location

`PUT /api/pins/{id}/location`

Updates the location of an existing pin. Rate limited to 1 update per 5 seconds per user.

Request headers:
- `Authorization: Bearer <token>`
- `X-Device-Id: <device-id>`

Request body:

```json
{
  "location_name": "Office",
  "location_address": "456 Business Ave",
  "location_wkt": "POINT(121.5200 25.0500)"
}
```

Response: `SnLocationPin` object or `404 Not Found`

### Remove pin

`DELETE /api/pins/{id}`

Permanently removes a pin.

Request headers:
- `Authorization: Bearer <token>`
- `X-Device-Id: <device-id>`

Response: `204 No Content` or `404 Not Found`

### List nearby pins

`GET /api/pins/nearby`

Returns pins visible to the current user based on their visibility settings.

Request headers:
- `Authorization: Bearer <token>`

Query parameters:
- `location_wkt` (optional): WKT geometry to filter by location
- `visibility` (optional): filter by specific visibility
- `offset` (optional, default 0): pagination offset
- `take` (optional, default 50, max 100): number of results

Response: List of `SnLocationPin` objects

### Get single pin

`GET /api/pins/{id}`

Gets a specific pin by ID if the requester has access.

Request headers:
- `Authorization: Bearer <token>`

Query parameters:
- `location_wkt` (optional): WKT geometry for distance calculation

Response: `SnLocationPin` object or `404 Not Found`

### Disconnect pin

`POST /api/pins/{id}/disconnect`

Called when the user disconnects. Flushes any cached location to the database and optionally keeps the pin.

Request headers:
- `Authorization: Bearer <token>`
- `X-Device-Id: <device-id>`

Request body:

```json
{
  "keep_on_disconnect": true
}
```

If `keep_on_disconnect` is true, the pin status changes to `Offline` with a 24-hour TTL.
If false, the pin is removed.

Response: `200 OK`

### Subscribe to pin updates (SSE)

`GET /api/pins/{id}/stream`

Opens a Server-Sent Events stream for real-time pin updates.

Request headers:
- `Authorization: Bearer <token>`
- `X-Device-Id: <device-id>`

Response: SSE stream with event types:
- `snapshot`: initial pin state
- `pin_updated`: location or metadata changed
- `pin_offline`: pin went offline
- `pin_removed`: pin was deleted

### Get my pins

`GET /api/pins/my-pins`

Returns all pins owned by the current user.

Request headers:
- `Authorization: Bearer <token>`
- `X-Device-Id: <device-id>`

Response: List of `SnLocationPin` objects

## Caching Strategy

- Location updates are cached in-memory with a dirty flag
- Rate limit: 1 update per 5 seconds per user
- Database flush: every 30 seconds for dirty entries
- Offline detection: pins with no heartbeat for 5+ minutes are marked offline

## Events

Published to EventBus (JetStream):

- `locationpin.created` - When a new pin is created
- `locationpin.updated` - When pin location/name/address changes
- `locationpin.removed` - When pin is deleted
- `locationpin.offline` - When pin goes offline due to disconnect or heartbeat timeout

## Database

Stored in `passport.location_pins` table.

Indexes:
- `(account_id, status)` - For finding user's active pin

## Related Models

- `LocationVisibility` - Shared enum used by both `SnMeet` and `SnLocationPin`
- `SnLocationPin` - Core pin model

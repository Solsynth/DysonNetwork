# Meet API

This document describes the server-side `meet` feature implemented in Passport.

## Overview

A meet is a short-lived session owned by one host account.

- A host creates a meet first.
- If the same host creates again within a short time and there is already an active meet, the existing active meet is returned.
- Other accounts can join with the meet ID.
- The join endpoint returns an SSE stream so clients can react to participant changes and status changes.
- When the host completes the meet, the meet becomes terminal and the SSE stream ends.
- Meets also expire automatically.

## Status

`MeetStatus`

- `Active`
- `Completed`
- `Expired`
- `Cancelled`

Terminal statuses do not change anymore.

## Data shape

The core model is `SnMeet`.

Important fields:

- `id`: meet ID
- `host_id`: host account ID
- `status`: current meet status
- `visibility`: `public` or `private`
- `expires_at`: auto-expiration time
- `completed_at`: set when host completes the meet
- `notes`: optional notes text
- `image`: optional image file reference
- `location_name`: optional human-friendly label
- `location_address`: optional address text
- `location_wkt`: optional geometry serialized as WKT for API responses
- `metadata`: optional JSON payload for client-defined extra info
- `participants`: joined participant accounts

Location storage uses PostGIS geometry in the database and is fully optional.

## Visibility

`MeetVisibility`

- `Public`
- `Private`

Public meets can be fetched by ID without already being a participant.
Private meets still work as share-by-ID sessions, but they are not treated as public resources.

## Authentication

All meet endpoints require authentication.

The current user is read from `HttpContext.Items["CurrentUser"]`.

## Endpoints

### Create meet

`POST /api/meets`

Creates a new meet for the current user, or returns an existing recent active meet from the same host.

Request body:

```json
{
  "visibility": 0,
  "notes": "Meet me near the east gate.",
  "image_id": "01JV6Y7Q7M8P6C3S0X4F8YZZZZ",
  "location_name": "Taipei Main Station",
  "location_address": "No. 3, Beiping W. Rd., Zhongzheng District, Taipei City",
  "location_wkt": "POINT(121.5170 25.0478)",
  "metadata": {
    "topic": "coffee chat"
  },
  "expires_in_seconds": 1800
}
```

Notes:

- `visibility` is optional and defaults to `Private`.
- `notes` is optional.
- `image_id` is optional.
- `location_name` is optional.
- `location_address` is optional.
- `location_wkt` is optional.
- `expires_in_seconds` is optional.
- `image_id` must point to an existing file object.
- `location_wkt` must be valid WKT. The server sets SRID `4326`.
- TTL is clamped by the server to a safe range.

### List meets

`GET /api/meets`

Lists meets visible to the current user.

Query params:

- `status`: optional filter
- `host_only`: optional boolean, only list meets hosted by current user
- `offset`: pagination offset
- `take`: page size

### Get meet

`GET /api/meets/{id}`

Returns a single meet if:

- the current user is the host
- the current user is a participant
- the meet visibility is `Public`

### Join meet and open SSE

`POST /api/meets/{id}/join`

Joins the meet if needed, then returns a `text/event-stream` response.

Behavior:

- If the current user is not already a participant and is not the host, the user is added as a participant.
- The first event is always a `snapshot`.
- Later events are pushed whenever the meet changes.
- When the meet reaches a terminal state, the stream closes.

Example SSE:

```text
event: snapshot
data: {"type":"snapshot","sent_at":"2026-03-20T16:00:00Z","meet":{"id":"...","status":0,"participants":[]}}

event: participant_joined
data: {"type":"participant_joined","sent_at":"2026-03-20T16:00:10Z","meet":{"id":"...","status":0,"participants":[...]}}

event: completed
data: {"type":"completed","sent_at":"2026-03-20T16:05:00Z","meet":{"id":"...","status":1,"participants":[...]}}
```

Current event types:

- `snapshot`
- `participant_joined`
- `completed`
- `expired`

### Complete meet

`POST /api/meets/{id}/complete`

Only the host can complete a meet.

Behavior:

- Changes status from `Active` to `Completed`
- Sets `completed_at`
- Stops future changes
- Emits the final SSE event
- Closes active SSE subscriptions

## Access rules

A private meet is visible only to:

- the host
- joined participants

A public meet can also be fetched by ID by other authenticated users.

Only the host can complete the meet.

## Expiration

Meets expire automatically.

Expiration is enforced in three ways:

- when creating or reading meets
- by an in-process expiration scheduler
- by the database recycling job as a cleanup fallback

When a meet expires:

- status becomes `Expired`
- it becomes terminal
- subscribers receive an `expired` SSE event

## Error cases

Common error scenarios:

- invalid meet ID: `404`
- invalid image ID: `400`
- trying to join a non-active meet: `400`
- non-host trying to complete a meet: `403`
- invalid `location_wkt`: `400`

## Implementation references

- Controller: [MeetController.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Passport/Meet/MeetController.cs)
- Service: [MeetService.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Passport/Meet/MeetService.cs)
- Models: [Meet.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Shared/Models/Meet.cs)
- Migration: [20260320155249_AddMeetSystem.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Passport/Migrations/20260320155249_AddMeetSystem.cs)
- Migration: [20260320170931_UpdateMeetLocationToPostgis.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Passport/Migrations/20260320170931_UpdateMeetLocationToPostgis.cs)
- Migration: [20260321060302_AddMeetImageNotesAndVisibility.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Passport/Migrations/20260321060302_AddMeetImageNotesAndVisibility.cs)

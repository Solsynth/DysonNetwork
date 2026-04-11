# Migration: LocationPin as Sub-Feature of Meet

**Date**: 2026-04-11

## Summary

Location pins are now a sub-feature of Meet. Each meet can have multiple pins (one per participant), and pin updates flow through the meet's realtime channel.

## What Changed

| Before | After |
|--------|-------|
| Pins are standalone - created via `/api/pins` | Pins belong to a meet - created via `/api/meets/{id}/pin` |
| Pin realtime via `/api/pins/{id}/stream` | Pin updates via `/api/meets/{id}/join` SSE |
| One active pin per user | One pin per user per meet |

## Database Migration

Run the following migration to add optional `meet_id` column to `location_pins`:

```sql
ALTER TABLE location_pins 
ADD COLUMN meet_id UUID REFERENCES meets(id) ON DELETE SET NULL;

CREATE INDEX idx_location_pins_meet_status ON location_pins(meet_id, status);
```

## API Changes

### New Endpoints (Meet Integration)

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/meets/{id}/pin` | POST | Create/update pin for a meet |
| `/api/meets/{id}/pin` | DELETE | Remove pin from meet |

### Request: POST /api/meets/{id}/pin

```json
{
  "visibility": 0,
  "location_name": "Here!",
  "location_address": "123 Main St",
  "location_wkt": "POINT(121.5170 25.0478)",
  "metadata": { "emoji": "🏠" },
  "keep_on_disconnect": true
}
```

**Authorization**: Must be host or participant of the meet.

### Request: DELETE /api/meets/{id}/pin

Removes the current user's pin from the meet.

### Realtime Updates (SSE)

Pin updates are now delivered via the meet's SSE channel at `/api/meets/{id}/join`:

| Event Type | Description |
|-----------|-------------|
| `pin_created` | New pin added to meet |
| `pin_updated` | Pin location changed |
| `pin_removed` | Pin removed from meet |
| `pin_offline` | Pin went offline |

### Deprecated (Still Works)

The standalone pin endpoints at `/api/pins/*` continue to work for backward compatibility, but new integrations should use the meet-based endpoints:

| Deprecated Endpoint | Status |
|-------------------|--------|
| `POST /api/pins` | Deprecated |
| `PUT /api/pins/{id}/location` | Deprecated |
| `DELETE /api/pins/{id}` | Deprecated |
| `GET /api/pins/nearby` | Deprecated |
| `GET /api/pins/{id}` | Deprecated |
| `POST /api/pins/{id}/disconnect` | Deprecated |
| `GET /api/pins/{id}/stream` | Deprecated |
| `GET /api/pins/me` | Deprecated |

## Model Changes

### SnLocationPin

Added optional `meet_id` field:

```csharp
public Guid? MeetId { get; set; }
[NotMapped] public SnMeet? Meet { get; set; }
```

### SnMeet

Added pins collection forhydration:

```csharp
[NotMapped] public List<SnLocationPin> Pins { get; set; } = [];
```

When fetching a meet with location, pins are automatically included in the response.

## Behavior Changes

### Pin Lifecycle

- **No longer tied to meet status**: Pins persist even after meet completes/expires
- **Linked to meet**: Pin creation/update/deletion triggers meet realtime events
- **Independent visibility**: Pin visibility can differ from meet visibility

### Access Control

To create/update a pin on a meet, user must be:
- The meet host, OR
- A participant of the meet

### Trail System

When a user moves more than 10 meters from their current pin location, a new pin record is created and the old one becomes a trail (status: `Offline`).

| Distance from current | Action |
|--------------------|--------|
| < 10 meters | Update existing pin in place (no new record) |
| >= 10 meters | Mark old as Offline, create new Active pin |

#### GET /api/meets/{id}/pins

Returns trail history (Offline pins only) for participants to view where the user has been.

Query parameters:
- `offset` (optional, default 0)
- `take` (optional, default 50, max 100)

Response: List of `SnLocationPin` objects sorted by `last_heartbeat_at` ascending.

#### GET /api/meets/{id}

When fetching a meet with location, only returns the latest (Active) pins, not trails. Use `/api/meets/{id}/pins` for trail history.

### Database Constraints

- One Active pin per user per meet
- One standalone Active pin per user (when `meet_id IS NULL`)
- Standalone pins continue to work for backward compatibility
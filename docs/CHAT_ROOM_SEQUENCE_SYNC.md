# Chat Room Sequence Sync

## Overview

`DysonNetwork.Messager` now assigns a monotonic `room_sequence` to every persisted `SnChatMessage` within a chat room.

This is used to make room-scoped WebSocket delivery recoverable when clients detect gaps such as:

```text
[1000, 1001, 1003]
```

In that case, the client knows `1002` is missing and can fetch it from the room sync endpoint.

This does not replace the existing timestamp-based sync model. It adds a room-local ordering cursor specifically for missing-message recovery.

## Data Model

Each persisted `SnChatMessage` now includes:

| Field | Type | Description |
|-------|------|-------------|
| `room_sequence` | int64 | Strictly increasing sequence number within one `chat_room_id`. |

Properties:

- Unique per room.
- Assigned by the backend at persistence time.
- Shared by normal chat messages and persisted sync/event messages such as edits, deletions, and reaction sync rows.
- Intended for room ordering and gap detection, not for cross-room ordering.

## Server Allocation

The backend uses a `chat_room_counters` table to allocate sequence numbers atomically per room.

Behavior:

- On message insert, the backend increments the room counter and assigns the next `room_sequence`.
- If multiple chat messages are inserted in the same save operation for the same room, the backend allocates a contiguous block.
- The field is non-null in the final schema.

## Migration and Old Data

Existing `chat_messages` rows are backfilled during migration.

Backfill strategy:

- Partition rows by `chat_room_id`.
- Order each room by `created_at`, then `id`.
- Assign `ROW_NUMBER()` as `room_sequence`.
- Initialize `chat_room_counters.last_sequence` from the highest assigned sequence per room.

Because of this backfill, old rows do not remain null and clients can rely on `room_sequence` being present after migration is applied.

## WebSocket Behavior

Normal room broadcasts still use the existing packet types such as:

- `messages.new`
- `messages.update`
- `messages.delete`
- `messages.reaction.added`
- `messages.reaction.removed`

The payload object is still `SnChatMessage`, but it now includes `room_sequence`.

Clients should:

1. Track the highest contiguous `room_sequence` they have applied per room.
2. Compare newly received packets against local room state.
3. Trigger room sync recovery when sequence gaps are detected.

Example:

```json
{
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "chat_room_id": "660e8400-e29b-41d4-a716-446655440001",
  "room_sequence": 1003,
  "type": "text",
  "content": "Hello",
  "created_at": "2024-02-02T10:00:00Z"
}
```

## Room Sync Endpoint

Gateway route:

```text
POST /messager/chat/{roomId}/sync
```

Local route:

```text
POST /api/chat/{roomId}/sync
```

## Request Body

The endpoint remains backward compatible with the existing timestamp cursor and now also supports explicit room-sequence recovery.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `last_sync_timestamp` | int64 | Yes | Existing timestamp cursor. Still used when no sequence recovery fields are provided. |
| `missing_sequences` | list<int64> | No | Exact missing `room_sequence` values to fetch. |
| `missing_sequence_ranges` | list<object> | No | Inclusive ranges to fetch when many sequence ids are missing. |

Range object:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `start_sequence` | int64 | Yes | Inclusive lower bound. |
| `end_sequence` | int64 | Yes | Inclusive upper bound. |

### Timestamp Mode

If both `missing_sequences` and `missing_sequence_ranges` are omitted or empty, the endpoint behaves as before:

- return messages where `created_at > last_sync_timestamp`
- order by room sequence / creation time

Example:

```json
{
  "last_sync_timestamp": 1706745600000
}
```

### Missing Sequence Mode

If either `missing_sequences` or `missing_sequence_ranges` is provided, the endpoint switches to room-sequence recovery mode for that request.

Example with explicit ids:

```json
{
  "last_sync_timestamp": 1706745600000,
  "missing_sequences": [1002, 1005]
}
```

Example with ranges:

```json
{
  "last_sync_timestamp": 1706745600000,
  "missing_sequence_ranges": [
    {
      "start_sequence": 2000,
      "end_sequence": 2050
    }
  ]
}
```

Example with both:

```json
{
  "last_sync_timestamp": 1706745600000,
  "missing_sequences": [1002],
  "missing_sequence_ranges": [
    {
      "start_sequence": 2000,
      "end_sequence": 2050
    }
  ]
}
```

Server behavior in sequence recovery mode:

- Exact ids are deduplicated.
- Ranges are normalized if start/end are reversed.
- Overlapping or adjacent ranges are merged.
- Response is ordered by `room_sequence` ascending, then `created_at`.

## Response

The response shape is unchanged:

| Field | Type | Description |
|-------|------|-------------|
| `messages` | list | Matching `SnChatMessage` rows. |
| `current_timestamp` | timestamp | Latest message timestamp in the response, or current server time when empty. |
| `total_count` | int32 | Count before the endpoint limit is applied. |

Example:

```json
{
  "messages": [
    {
      "id": "550e8400-e29b-41d4-a716-446655440000",
      "chat_room_id": "660e8400-e29b-41d4-a716-446655440001",
      "room_sequence": 1002,
      "type": "text",
      "content": "Missed message",
      "created_at": "2024-02-02T10:00:00Z"
    }
  ],
  "current_timestamp": "2024-02-02T10:00:00Z",
  "total_count": 1
}
```

## Recommended Client Flow

1. Continue using the existing websocket subscription flow for real-time delivery.
2. Store `room_sequence` for every persisted message per room.
3. When websocket packets arrive out of sequence, compute the missing ids or ranges.
4. Call `POST /chat/{roomId}/sync` with `missing_sequences`, `missing_sequence_ranges`, or both.
5. Insert the returned messages into local storage and close the gap.
6. Keep timestamp-based global sync and room sync for normal reconciliation.

## Notes

- This feature is aimed at persisted room timeline recovery, not ephemeral packets.
- Placeholder update and placeholder expired packets remain best-effort websocket events.
- Global sync (`POST /messager/chat/sync`) remains timestamp-based.
- Room message listing now sorts by `room_sequence` descending, then `created_at` descending.

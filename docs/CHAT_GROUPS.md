# Chat Groups API

## Overview

The Chat Groups API allows users to organize their chat rooms into custom groups (folders/categories). Each user can create multiple groups and assign their joined chat rooms to at most one group at a time. Groups are per-user — different users can organize the same room into different groups.

Groups support metadata including a display name, color, icon, and sort order.

When using with the gateway, `/api/chat` becomes `/messager/chat` as the base path.

## Model

### SnChatGroup

| Field | Type | Description |
|-------|------|-------------|
| id | guid | Unique group identifier |
| account_id | guid | The user who owns this group |
| name | string | Group display name (max 256 chars) |
| color | string? | Optional hex color for UI theming (max 32 chars) |
| icon | string? | Optional icon identifier (max 64 chars) |
| order | int | Sort order for display (lower values first) |
| room_ids | guid[] | List of chat room IDs assigned to this group (computed on read) |
| created_at | instant | Creation timestamp |
| updated_at | instant | Last update timestamp |

### SnChatMember

The `chat_member` model now includes:

| Field | Type | Description |
|-------|------|-------------|
| chat_group_id | guid? | Optional reference to the group this room is assigned to |
| chat_group | SnChatGroup? | The group object (loaded on demand) |

## Endpoints

### List My Groups

```
GET /api/chat/groups
```

Returns all groups owned by the current user, ordered by `order` ascending. Each group includes a `room_ids` array listing the chat rooms assigned to it.

#### Authentication

```
Authorization: Bearer <token>
```

#### Response

```json
[
  {
    "id": "aa0e8400-e29b-41d4-a716-446655440010",
    "account_id": "880e8400-e29b-41d4-a716-446655440003",
    "name": "Work",
    "color": "#4A90D9",
    "icon": "briefcase",
    "order": 0,
    "room_ids": [
      "550e8400-e29b-41d4-a716-446655440000",
      "550e8400-e29b-41d4-a716-446655440001"
    ],
    "created_at": "2024-02-02T10:00:00Z",
    "updated_at": "2024-02-02T10:00:00Z"
  },
  {
    "id": "aa0e8400-e29b-41d4-a716-446655440011",
    "account_id": "880e8400-e29b-41d4-a716-446655440003",
    "name": "Friends",
    "color": "#7ED321",
    "icon": "heart",
    "order": 1,
    "room_ids": [
      "550e8400-e29b-41d4-a716-446655440002"
    ],
    "created_at": "2024-02-02T10:05:00Z",
    "updated_at": "2024-02-02T10:05:00Z"
  }
]
```

#### Error Responses

| Status Code | Description |
|-------------|-------------|
| 401 Unauthorized | Invalid or missing authentication token |

---

### Create a Group

```
POST /api/chat/groups
```

#### Authentication

```
Authorization: Bearer <token>
```

#### Request Body

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| name | string | Yes | Group display name (max 256 chars) |
| color | string? | No | Hex color for UI theming (max 32 chars) |
| icon | string? | No | Icon identifier (max 64 chars) |
| order | int? | No | Sort order. Defaults to `max(existing orders) + 1` |

Example:
```json
{
  "name": "Work",
  "color": "#4A90D9",
  "icon": "briefcase"
}
```

#### Response

Returns the created `SnChatGroup` object.

#### Error Responses

| Status Code | Description |
|-------------|-------------|
| 401 Unauthorized | Invalid or missing authentication token |
| 400 Bad Request | Invalid name (empty or exceeds 256 chars) |

---

### Update a Group

```
PATCH /api/chat/groups/{groupId}
```

#### Authentication

```
Authorization: Bearer <token>
```

#### Request Body

All fields are optional. Only provided fields are updated.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| name | string? | No | New group name |
| color | string? | No | New color |
| icon | string? | No | New icon |
| order | int? | No | New sort order |

Example:
```json
{
  "name": "Work & Projects",
  "color": "#50E3C2"
}
```

#### Response

Returns the updated `SnChatGroup` object.

#### Error Responses

| Status Code | Description |
|-------------|-------------|
| 401 Unauthorized | Invalid or missing authentication token |
| 404 Not Found | Group not found or does not belong to the current user |

---

### Delete a Group

```
DELETE /api/chat/groups/{groupId}
```

Deletes the group. Chat rooms previously assigned to this group become ungrouped (their `chat_group_id` is set to `null`).

#### Authentication

```
Authorization: Bearer <token>
```

#### Response

Returns `200 OK` on success.

#### Error Responses

| Status Code | Description |
|-------------|-------------|
| 401 Unauthorized | Invalid or missing authentication token |
| 404 Not Found | Group not found or does not belong to the current user |

---

### Move a Room to a Group

```
PATCH /api/chat/rooms/{roomId}/group
```

Assigns a chat room to a group, or removes it from its current group.

#### Authentication

```
Authorization: Bearer <token>
```

#### Request Body

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| group_id | guid? | Yes | The target group ID, or `null` to ungroup |

Assign to a group:
```json
{
  "group_id": "aa0e8400-e29b-41d4-a716-446655440010"
}
```

Remove from group:
```json
{
  "group_id": null
}
```

#### Response

Returns `200 OK` on success.

#### Error Responses

| Status Code | Description |
|-------------|-------------|
| 401 Unauthorized | Invalid or missing authentication token |
| 400 Bad Request | Room membership not found, or target group does not exist |

---

### List Joined Chat Rooms (updated)

```
GET /api/chat
```

**Breaking change:** This endpoint now returns an object instead of a flat array.

#### Response

```json
{
  "rooms": [
    { ... },
    { ... }
  ],
  "groups": [
    { ... }
  ]
}
```

- `rooms`: Array of `SnChatRoom` objects (same as before).
- `groups`: Array of `SnChatGroup` objects, each containing `room_ids`.

Clients should use the `groups` array to organize rooms into folders. Rooms not present in any group's `room_ids` are considered ungrouped.

---

### Chat Room Sync (updated)

```
POST /api/chat/rooms/sync
```

The sync response now includes a `groups` field.

#### Response

```json
{
  "changes": [ ... ],
  "summaries": [ ... ],
  "groups": [
    {
      "id": "aa0e8400-e29b-41d4-a716-446655440010",
      "name": "Work",
      "room_ids": [ ... ],
      ...
    }
  ],
  "current_timestamp": 1735689600000,
  "total_count": 5,
  "summary_total_count": 10
}
```

The `groups` field contains the full list of the user's groups with their current `room_ids`. Clients should use this to reconcile local group state on each sync.

---

## Persistence Model

- `chat_groups` stores user-defined groups. Each group belongs to a single user (`account_id`).
- `chat_members.chat_group_id` is a nullable FK referencing `chat_groups.id`.
- Deleting a group sets `chat_group_id` to `null` on all affected members (`ON DELETE SET NULL`).
- An index on `(account_id, name)` exists on `chat_groups` for fast lookup.
- Groups use soft delete (`deleted_at`). Deleted groups are excluded from all queries.

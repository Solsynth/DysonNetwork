# Status And Chat Online API

## Overview

This document covers two API updates:

- Passport account status APIs now use a unified `type` enum instead of separate `is_invisible` and `is_not_disturb` booleans.
- Messager online member lookup now returns structured presence data instead of only an online count.
- Realtime websocket packets notify friends and subscribed chat rooms when account status or rich presence activities change.

JSON fields are shown in `snake_case`. The naming converter will map server-side model properties automatically.

For routed paths in this document, use:

- `passport/...` for Passport endpoints
- `messager/...` for Messager endpoints

This replaces the old gateway-facing `/api/...` notation in examples.

## Passport Status API

### Routes

- `GET passport/accounts/me/statuses`
- `POST passport/accounts/me/statuses`
- `PATCH passport/accounts/me/statuses`
- `DELETE passport/accounts/me/statuses`
- `GET passport/accounts/{name}/statuses`

### Status Type Enum

| Value | Name | Meaning |
|-------|------|---------|
| `0` | `default` | Normal visible status |
| `1` | `busy` | Visible busy status |
| `2` | `do_not_disturb` | Visible do-not-disturb status |
| `3` | `invisible` | Hidden from online presence |

### Status Request Body

Used by both:

- `POST passport/accounts/me/statuses`
- `PATCH passport/accounts/me/statuses`

```json
{
  "attitude": 2,
  "type": 1,
  "is_automated": false,
  "label": "In a meeting",
  "symbol": "calendar",
  "app_identifier": null,
  "meta": {
    "source": "manual"
  },
  "cleared_at": null
}
```

### Request Fields

| Field | Type | Notes |
|-------|------|-------|
| `attitude` | int | Existing status attitude enum |
| `type` | int | New unified status type enum |
| `is_automated` | bool | Whether the status is app-managed |
| `label` | string or null | Max 1024 chars |
| `symbol` | string or null | Max 128 chars |
| `app_identifier` | string or null | Max 4096 chars |
| `meta` | object or null | Arbitrary metadata |
| `cleared_at` | timestamp or null | Optional expiration time |

### Status Response Shape

```json
{
  "id": "00000000-0000-0000-0000-000000000000",
  "attitude": 2,
  "is_online": true,
  "is_customized": true,
  "type": 1,
  "label": "In a meeting",
  "symbol": "calendar",
  "meta": {
    "source": "manual"
  },
  "cleared_at": null,
  "app_identifier": null,
  "is_automated": false,
  "account_id": "00000000-0000-0000-0000-000000000000",
  "created_at": "2026-03-10T12:00:00Z",
  "updated_at": "2026-03-10T12:00:00Z",
  "deleted_at": null
}
```

### Behavior Notes

- `busy` is a new visible status mode.
- `do_not_disturb` remains visible, but is now represented by `type`.
- `invisible` remains the only status type that hides active online presence.
- `symbol` is optional and can be used for short decorative or semantic status markers.
- Public lookup via `GET passport/accounts/{name}/statuses` will mask `invisible` to `default`.

## Messager Online Members API

### Route

- `GET messager/chat/{room_id}/members/online`

### Previous Behavior

This endpoint previously returned only an integer:

```json
3
```

### New Response Shape

The endpoint now returns a structured object:

```json
{
  "online_count": 3,
  "direct_message_status": null,
  "online_user_names": [
    "alice",
    "bob",
    "carol"
  ],
  "online_accounts": [
    {
      "id": "00000000-0000-0000-0000-000000000001",
      "name": "alice",
      "nick": "alice"
    }
  ]
}
```

### Response Fields

| Field | Type | Notes |
|-------|------|-------|
| `online_count` | int | Number of online joined members |
| `direct_message_status` | object or null | Full status of the other participant for DM rooms |
| `online_user_names` | string[] | Nick/name list for online members in group rooms |
| `online_accounts` | object[] | Full account objects for online members in group rooms |

### Direct Message Behavior

For direct message rooms:

- `online_count` is still returned
- `direct_message_status` contains the other member's full account status
- `online_user_names` is empty
- `online_accounts` is empty

Example:

```json
{
  "online_count": 1,
  "direct_message_status": {
    "id": "00000000-0000-0000-0000-000000000010",
    "attitude": 2,
    "is_online": true,
    "is_customized": true,
    "type": 2,
    "label": "Heads down",
    "symbol": "moon",
    "meta": null,
    "cleared_at": null,
    "account_id": "00000000-0000-0000-0000-000000000002"
  },
  "online_user_names": [],
  "online_accounts": []
}
```

### Group Chat Behavior

For group rooms:

- `online_count` is returned
- `direct_message_status` is `null`
- `online_user_names` contains the online member display names
- `online_accounts` contains full account payloads for online members

Example:

```json
{
  "online_count": 2,
  "direct_message_status": null,
  "online_user_names": [
    "alice",
    "bob"
  ],
  "online_accounts": [
    {
      "id": "00000000-0000-0000-0000-000000000001",
      "name": "alice",
      "nick": "alice"
    },
    {
      "id": "00000000-0000-0000-0000-000000000002",
      "name": "bob",
      "nick": "bob"
    }
  ]
}
```

## Client Migration Notes

- Replace any status request payloads that send `is_invisible` or `is_not_disturb` with a single `type`.
- If clients display status icons or markers, use the new `symbol` field.
- Update consumers of `messager/chat/{room_id}/members/online` to expect an object instead of a raw integer.
- DM chat clients should read `direct_message_status`.
- Group chat clients can use either `online_user_names` for lightweight UI or `online_accounts` for richer member cards.

## Realtime Presence Packets

Detailed websocket lifecycle and presence broadcast behavior is documented in:

```text
docs/WEBSOCKET_PRESENCE_BROADCASTS.md
```

Clients should handle these packet types:

| Packet type | Audience | Purpose |
| --- | --- | --- |
| `account.status.updated` | Friends | Account-level online/status change |
| `chat.presence.updated` | Subscribed chat room members | Room-scoped online/status change with `room_id` and `member_id` |
| `account.presence.activities.updated` | Friends | Account-level rich presence activity change |
| `chat.presence.activities.updated` | Subscribed chat room members | Room-scoped rich presence activity change with `room_id` and `member_id` |

Friend packets and chat-room packets are intentionally independent. If a user is both a friend and a subscribed member of a room containing the changed account, they receive both packet families.

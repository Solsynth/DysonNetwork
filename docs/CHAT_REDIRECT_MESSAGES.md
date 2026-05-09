# Chat Redirect Messages

This document describes the HTTP-only chat redirect API for redirecting a section of chat history into another chat room.

The feature is named `redirect` to avoid conflicting with the existing quote-style forward behavior already represented by `forwarded_message_id`.

## Endpoint

`POST /api/chat/{roomId}/messages/redirect`

- `roomId` is the destination chat room id.
- Requires authentication.
- Requires permission `chat.messages.create`.
- HTTP only. No websocket send path is implemented for redirect.

## Request Body

```json
{
  "message_ids": [
    "11111111-1111-1111-1111-111111111111",
    "22222222-2222-2222-2222-222222222222"
  ]
}
```

`message_ids` must all belong to the same source room. The server rebuilds the redirected transcript snapshot in chronological order by message creation time, regardless of request order.

## Response

Returns `200 OK` with a single newly created destination `SnChatMessage`.

That message is a transcript container for the selected history section. It is not one cloned message per selected source message.

## Current Rules

The current implementation is intentionally limited:

- only text messages can be redirected
- all selected messages must come from the same source room
- source messages must come from a non-E2EE room
- destination room must be a non-E2EE room
- the caller must be an active member of the destination room
- the caller must be an active member of the source room
- up to `100` messages can be redirected per request

If any selected message does not exist or violates the constraints, the request is rejected.

## Redirect Message Shape

The redirect action creates one normal new chat message with:

- `type = "text"`
- `content = null`
- no copied top-level attachments
- `forwarded_message_id` set to the first message id in the selected history section for provenance
- `meta.redirect` containing a frozen transcript snapshot

The snapshot is used so the destination room can render the redirected chat history even if the source messages are later edited or deleted.

## Versioning

The redirect snapshot uses two payload versions:

- `version = 1` when exactly one source message is redirected
- `version = 2` when multiple source messages are redirected as a history segment

This keeps the simpler single-message redirect payload available for clients that already understand the original contract.

## `meta.redirect` Snapshot

When multiple source messages are redirected, `meta.redirect` has this shape:

```json
{
  "redirect": {
    "version": 2,
    "kind": "history_segment",
    "source_room_id": "33333333-3333-3333-3333-333333333333",
    "source_room": {
      "id": "33333333-3333-3333-3333-333333333333",
      "name": "General",
      "description": null,
      "type": "Group",
      "is_community": false,
      "is_public": false,
      "encryption_mode": "None",
      "realm_id": null,
      "account_id": null,
      "picture": null,
      "background": null,
      "created_at": 1712000000000,
      "updated_at": 1712000000000
    },
    "range": {
      "start_message_id": "11111111-1111-1111-1111-111111111111",
      "end_message_id": "22222222-2222-2222-2222-222222222222",
      "message_count": 2,
      "started_at": 1712345678901,
      "ended_at": 1712345680901
    },
    "sender_map": {
      "55555555-5555-5555-5555-555555555555": {
        "id": "55555555-5555-5555-5555-555555555555",
        "chat_room_id": "33333333-3333-3333-3333-333333333333",
        "account_id": "44444444-4444-4444-4444-444444444444",
        "nick": "alice",
        "realm_nick": null,
        "realm_bio": null,
        "realm_experience": null,
        "realm_level": null,
        "realm_leveling_progress": null,
        "notify": "All",
        "joined_at": 1712345600000,
        "leave_at": null,
        "created_at": 1712345600000,
        "updated_at": 1712345600000,
        "account": {
          "id": "44444444-4444-4444-4444-444444444444",
          "name": "alice",
          "nick": "Alice",
          "language": "en",
          "region": "US",
          "activated_at": 1712000000000,
          "is_superuser": false,
          "automated_id": null,
          "profile": null,
          "created_at": 1712000000000,
          "updated_at": 1712000000000
        }
      }
    },
    "messages": [
      {
        "id": "11111111-1111-1111-1111-111111111111",
        "type": "text",
        "content": "hello world",
        "meta": {},
        "nonce": "nonce-value",
        "edited_at": null,
        "replied_message_id": null,
        "forwarded_message_id": null,
        "sender_id": "55555555-5555-5555-5555-555555555555",
        "chat_room_id": "33333333-3333-3333-3333-333333333333",
        "created_at": 1712345678901,
        "updated_at": 1712345678901,
        "deleted_at": null,
        "attachments": [],
        "reactions_count": {},
        "chat_room": {
          "id": "33333333-3333-3333-3333-333333333333",
          "name": "General",
          "description": null,
          "type": "Group",
          "is_community": false,
          "is_public": false,
          "encryption_mode": "None",
          "realm_id": null,
          "account_id": null,
          "picture": null,
          "background": null,
          "created_at": 1712000000000,
          "updated_at": 1712000000000
        }
      }
    ],
    "redirected_by": {
      "id": "77777777-7777-7777-7777-777777777777",
      "chat_room_id": "88888888-8888-8888-8888-888888888888",
      "account_id": "99999999-9999-9999-9999-999999999999",
      "nick": "bob",
      "realm_nick": null,
      "realm_bio": null,
      "realm_experience": null,
      "realm_level": null,
      "realm_leveling_progress": null,
      "notify": "All",
      "joined_at": 1712345600000,
      "leave_at": null,
      "created_at": 1712345600000,
      "updated_at": 1712345600000,
      "account": {
        "id": "99999999-9999-9999-9999-999999999999",
        "name": "bob",
        "nick": "Bob",
        "language": "en",
        "region": "US",
        "activated_at": 1712000000000,
        "is_superuser": false,
        "automated_id": null,
        "profile": null,
        "created_at": 1712000000000,
        "updated_at": 1712000000000
      }
    },
    "redirected_to_room": {
      "id": "88888888-8888-8888-8888-888888888888",
      "name": "Announcements",
      "description": null,
      "type": "Group",
      "is_community": false,
      "is_public": false,
      "encryption_mode": "None",
      "realm_id": null,
      "account_id": null,
      "picture": null,
      "background": null,
      "created_at": 1712000000000,
      "updated_at": 1712000000000
    }
  }
}
```

The most important fields are:

- `source_room`: snapshot of the original chat room
- `range`: summary of the selected history section
- `sender_map`: deduplicated sender snapshot map keyed by source `sender_id`
- `messages`: full preloaded message-entry snapshots in chronological order
- `redirected_by`: snapshot of the member who performed the redirect in the destination room
- `redirected_to_room`: snapshot of the destination room at redirect time

When exactly one source message is redirected, `meta.redirect` stays on `version = 1` and uses the single-message shape with:

- `source_message_id`
- `source_room_id`
- `source_room`
- `source_sender_id`
- `source_sender_name`
- `source_type`
- `source_content`
- `source_created_at`
- `source_attachments`
- `source_meta`
- `sender_map`
- `source_message`
- `redirected_by`
- `redirected_to_room`

## Snapshot Semantics

The redirect snapshot is frozen at redirect time.

That means:

- later edits to source messages do not rewrite redirected messages
- later deletion of source messages does not erase the redirected snapshot
- destination room rendering should prefer `meta.redirect.messages` and `meta.redirect.source_room` for transcript UI

## Notes For Clients

Clients can detect redirected transcript messages by checking for `meta.redirect.version == 2` or `meta.redirect.kind == "history_segment"`.

Clients can detect single-message redirects by checking for `meta.redirect.version == 1`.

Recommended rendering behavior:

- show the destination message as a redirected history card
- use `meta.redirect.source_room` as the original room label
- use `meta.redirect.messages` to render the transcript entries in order
- resolve each message entry's `sender_id` through `meta.redirect.sender_map`
- use each message entry's `attachments` snapshots directly without extra fetches
- use `meta.redirect.redirected_by` if the UI wants to explicitly label who redirected the history section

## Error Cases

Common error responses include:

- `400 Bad Request` when `message_ids` is empty
- `400 Bad Request` when more than `100` messages are requested
- `400 Bad Request` when one or more source messages do not exist
- `400 Bad Request` when selected messages span multiple source rooms
- `400 Bad Request` when any selected message is not a text message
- `400 Bad Request` when source or destination room is encrypted
- `403 Forbidden` when the caller is not a current member of the destination room
- `403 Forbidden` when the caller is not a current member of the source room

## Non-Goals In Current Version

The current implementation does not support:

- websocket redirect send
- redirecting voice messages
- redirecting encrypted messages
- adding a user-authored comment above redirected content
- partial success for mixed-validity batches

These can be added later without changing the basic transcript-style `meta.redirect` contract.

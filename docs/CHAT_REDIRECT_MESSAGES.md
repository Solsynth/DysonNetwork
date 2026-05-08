# Chat Redirect Messages

This document describes the HTTP-only chat redirect API for redirecting one or more existing chat messages into another chat room.

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

## Response

Returns `200 OK` with a JSON array of the newly created destination `SnChatMessage` objects.

Each returned message is a newly created chat message in the destination room. It is not the original message.

## Current Rules

The current implementation is intentionally limited:

- only text messages can be redirected
- source messages must come from non-E2EE rooms
- destination room must be a non-E2EE room
- the caller must be an active member of the destination room
- the caller must be an active member of every source room
- up to `100` messages can be redirected per request

If any selected message does not exist or violates the constraints, the request is rejected.

## Redirect Message Shape

Each redirected message is created as a normal new chat message with:

- `type = "text"`
- `content` copied from the source message
- `attachments` copied from the source message
- `forwarded_message_id` set to the original source message id for provenance
- `meta.redirect` containing a frozen snapshot of the source message

The snapshot is used so the destination room can render redirect information even if the source message is later edited or deleted.

## `meta.redirect` Snapshot

The redirected message contains a `meta.redirect` object with this shape:

```json
{
  "redirect": {
    "version": 1,
    "source_message_id": "11111111-1111-1111-1111-111111111111",
    "source_room_id": "33333333-3333-3333-3333-333333333333",
    "source_sender_id": "44444444-4444-4444-4444-444444444444",
    "source_sender_name": "alice",
    "source_type": "text",
    "source_content": "hello world",
    "source_created_at": 1712345678901,
    "source_attachments": [
      {
        "id": "file_id",
        "name": "photo.png",
        "mime_type": "image/png",
        "size": 123456,
        "url": null,
        "width": 1280,
        "height": 720,
        "blurhash": null,
        "has_compression": false
      }
    ],
    "source_meta": {},
    "source_message": {
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
      "sender": {
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
          "profile": {
            "id": "66666666-6666-6666-6666-666666666666",
            "first_name": "Alice",
            "middle_name": null,
            "last_name": null,
            "bio": null,
            "gender": null,
            "pronouns": null,
            "time_zone": null,
            "location": null,
            "birthday": null,
            "last_seen_at": null,
            "experience": 0,
            "level": 0,
            "leveling_progress": 0,
            "social_credits": 0,
            "social_credits_level": 0,
            "picture": null,
            "background": null,
            "created_at": 1712000000000,
            "updated_at": 1712000000000
          },
          "created_at": 1712000000000,
          "updated_at": 1712000000000
        }
      },
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
    },
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

`source_message` is a preloaded snapshot of the original message entry. It is intended to give clients enough data to render the redirected message card without performing an extra fetch for sender or attachment context.

`redirected_by` is a snapshot of the member who performed the redirect in the destination room.

`redirected_to_room` is a snapshot of the destination room at redirect time.

## Snapshot Semantics

The redirect snapshot is frozen at redirect time.

That means:

- later edits to the source message do not rewrite redirected messages
- later deletion of the source message does not erase the redirected snapshot
- destination room rendering should prefer `meta.redirect.source_message` for redirect card UI

## Notes For Clients

Clients can detect redirected messages by checking for `meta.redirect`.

Recommended rendering behavior:

- show the destination message as a redirected message card
- use `meta.redirect.source_message.sender` as the original sender payload
- use `meta.redirect.source_message.attachments` for attachment rendering
- use `meta.redirect.source_message.content` as the main snapshot body
- use `meta.redirect.redirected_by` if the UI wants to explicitly label who redirected the message
- fall back to `source_sender_name`, `source_content`, and `source_attachments` only if the client wants a simpler contract
- optionally use `forwarded_message_id` as a provenance link when the client knows the user can still access the original room/message

## Error Cases

Common error responses include:

- `400 Bad Request` when `message_ids` is empty
- `400 Bad Request` when more than `100` messages are requested
- `400 Bad Request` when one or more source messages do not exist
- `400 Bad Request` when any selected message is not a text message
- `400 Bad Request` when source or destination room is encrypted
- `403 Forbidden` when the caller is not a current member of the destination room
- `403 Forbidden` when the caller is not a current member of one or more source rooms

## Non-Goals In Current Version

The current implementation does not support:

- websocket redirect send
- redirecting voice messages
- redirecting encrypted messages
- adding a user-authored comment above redirected content
- partial success for mixed-validity batches

These can be added later without changing the basic `meta.redirect` contract.

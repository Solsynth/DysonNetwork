# Meet Embed

This document describes the `meet` embed supported by posts and chat messages.

## Summary

The `meet` embed is a lightweight reference embed.

It stores only:

- `type = "meet"`
- `id`

This is intended for self-contained clients that only need a meet identifier to render or resolve the referenced content.

## Supported APIs

Posts:

- `POST /api/posts`
- `PUT /api/posts/{id}`

Chat:

- `POST /api/chat/{roomId}/messages`
- `PATCH /api/chat/{roomId}/messages/{messageId}`

## Request Fields

All request payloads use snake_case JSON.

Available field:

- `meet_id`: optional UUID

## Stored Embed Shape

The embed is stored in `meta.embeds` or `metadata.embeds` as:

```json
{
  "type": "meet",
  "id": "6d0897de-76f7-4fc3-8cad-210062f4a8d5"
}
```

## Post Example

Create a post with a meet embed:

```json
{
  "content": "See you there",
  "meet_id": "6d0897de-76f7-4fc3-8cad-210062f4a8d5"
}
```

## Chat Example

Send a chat message with a meet embed:

```json
{
  "content": "Join this meet",
  "meet_id": "6d0897de-76f7-4fc3-8cad-210062f4a8d5"
}
```

## Update Behavior

Meet embed updates use remove-and-replace semantics.

If `meet_id` is present in the update request:

- existing `meet` embeds are removed
- the new `meet` embed is added

If `meet_id` is omitted from the update request:

- existing `meet` embeds are removed

This matches the current explicit behavior used for other removable embeds in these endpoints.

## E2EE Chat Behavior

In encrypted chat rooms, plaintext `meet_id` is not allowed.

If a client sends `meet_id` in an E2EE room, the request is rejected with the same plaintext-forbidden rule already used for other embeds.

## Notes

- This embed currently does not perform Passport-side meet validation in Sphere or Messager.
- There is no shared meet lookup client between those services yet.
- The embed is stored as a lightweight reference only.

# Chat Message Search API

Searches plaintext messages across the chat rooms the authenticated account currently belongs to. Results are grouped by chat room.

The local service route is `/api/chat/messages/search`. Through the production gateway, use `/messager/chat/messages/search`.

## Authentication

This endpoint requires a bearer token.

```http
Authorization: Bearer <token>
```

## Request

```http
GET /api/chat/messages/search?query=deployment&offset=0&take=20
```

### Query parameters

| Parameter | Type | Required | Default | Description |
|---|---|---:|---:|---|
| `query` | string | Yes | — | Case-insensitive text to find. Maximum 256 characters. |
| `room_id` | UUID | No | — | Restrict results to one chat room. The caller must be an active member. |
| `sender_id` | UUID | No | — | Restrict results to a chat member ID (`SnChatMessage.sender_id`), not an account ID. |
| `offset` | integer | No | `0` | Number of matching messages to skip. |
| `take` | integer | No | `20` | Number of matching messages to return, up to `100`. |

Pagination applies to the complete cross-room result set before it is grouped. A room can therefore appear on more than one page.

## Response

The endpoint returns a list of room groups. Groups are ordered by the newest matched message in each group, and messages in each group are newest-first.

```json
[
  {
    "room": {
      "id": "2e574d54-c6ac-4e4d-986e-076ca260f8d2",
      "name": "Platform"
    },
    "messages": [
      {
        "id": "55a236ea-0e96-4208-bdf2-1179daa95c8c",
        "chat_room_id": "2e574d54-c6ac-4e4d-986e-076ca260f8d2",
        "sender_id": "9232a709-18ed-4aea-8b4a-22a120673aaf",
        "content": "The deployment is ready for review.",
        "type": "text",
        "created_at": "2026-07-19T04:00:00Z",
        "sender": {
          "id": "9232a709-18ed-4aea-8b4a-22a120673aaf"
        }
      }
    ]
  }
]
```

`room` is an `SnChatRoom`; direct-message rooms include their hydrated member information. Each item in `messages` is an `SnChatMessage` with hydrated sender, reaction counts, and reactions made by the authenticated account.

The `X-Total` response header contains the total count of matching messages before `offset` and `take` are applied.

## Search scope and privacy

- Only messages with `type = "text"`, plaintext content, and no encryption are searched.
- The caller must be an active room member (`joined_at` is set and `leave_at` is not set).
- Deleted messages are excluded by the service's soft-delete filter.
- E2EE/MLS messages are never searched by this endpoint because their content is not available to the server.

## Errors

| Status | Code | Meaning |
|---:|---|---|
| 401 | — | No valid authenticated account is present. |
| 400 | `CHAT_SEARCH_QUERY_REQUIRED` | `query` was empty or contained only whitespace. |
| 400 | validation error | `query` exceeds 256 characters, or another query value is invalid. |

## Example

```bash
curl -G 'https://api.example.com/messager/chat/messages/search' \
  -H 'Authorization: Bearer <token>' \
  --data-urlencode 'query=deployment' \
  --data-urlencode 'take=20'
```

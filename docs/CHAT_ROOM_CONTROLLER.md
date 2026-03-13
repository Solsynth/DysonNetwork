# ChatRoomController API Documentation

This document describes the timeout and member management endpoints in the ChatRoomController.

## Base URL

```
/api/chat
```

## Timeout Endpoints

### Timeout a Member

Temporarily restrict a member's ability to send messages in the chat room.

```
POST /api/chat/{roomId:guid}/members/{memberId:guid}/timeout
```

**Authorization:**
- Realm-owned chat: requires `RealmModerator` role
- Group chat (non-realm): requires room owner
- Direct Message: not allowed (returns 400 Bad Request)

**Request Body:**

```json
{
  "reason": "Optional reason for timeout (max 4096 characters)",
  "timeoutUntil": "2024-01-15T10:30:00Z"
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `reason` | string | No | Reason for the timeout |
| `timeoutUntil` | Instant | Yes | When the timeout expires (must be in the future) |

**Response:** `204 No Content`

**System Message:** Emits `system.member.timed_out` to the chat room.

**Errors:**
- `400 Bad Request`: Timeout time is not in the future
- `403 Forbidden`: User lacks permission to timeout members
- `404 Not Found`: Room or member not found

---

### Remove Member Timeout

Remove an active timeout from a member, restoring their full chat privileges.

```
DELETE /api/chat/{roomId:guid}/members/{memberId:guid}/timeout
```

**Authorization:**
- Realm-owned chat: requires `RealmModerator` role
- Group chat (non-realm): requires room owner
- Direct Message: requires being part of the DM

**Response:** `204 No Content`

**System Message:** Emits `system.member.timeout_removed` to the chat room.

**Errors:**
- `403 Forbidden`: User lacks permission to remove timeout
- `404 Not Found`: Room or member not found

---

## Member Management Endpoints

### Update Notification Settings

Configure notification preferences and Do Not Disturb mode for yourself.

```
PATCH /api/chat/{roomId:guid}/members/me/notify
```

**Authorization:** Required

**Request Body:**

```json
{
  "notifyLevel": 1,
  "breakUntil": "2024-01-15T10:30:00Z"
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `notifyLevel` | ChatMemberNotify | No | Notification level preference |
| `breakUntil` | Instant | No | DND until this time (muted until this time) |

**Response:** `200 OK` - Returns updated `SnChatMember`

---

### Remove Member

Permanently remove a member from the chat room.

```
DELETE /api/chat/{roomId:guid}/members/{memberId:guid}
```

**Authorization:**
- Realm-owned chat: requires `RealmModerator` role
- Group chat (non-realm): requires room owner
- Direct Message: requires being part of the DM

**Response:** `204 No Content`

**Errors:**
- `403 Forbidden`: User lacks permission to remove members
- `404 Not Found`: Room or member not found

---

## Related Endpoints

### List Members

Get a list of chat room members.

```
GET /api/chat/{roomId:guid}/members?take=20&offset=0&withStatus=false
```

**Query Parameters:**
- `take` (int): Number of members to return (default: 20)
- `offset` (int): Number of members to skip (default: 0)
- `withStatus` (bool): Include online status (default: false)

**Response:** `200 OK` - Returns list of `SnChatMember`

**Response Headers:**
- `X-Total`: Total number of members

---

### Get Online Members

Get count and details of online members.

```
GET /api/chat/{roomId:guid}/members/online
```

**Response:** `200 OK`

```json
{
  "onlineCount": 5,
  "directMessageStatus": { ... },
  "onlineUserNames": ["user1", "user2"],
  "onlineAccounts": [...]
}
```

---

### Invite Member

Invite a user to join the chat room.

```
POST /api/chat/invites/{roomId:guid}
```

**Request Body:**

```json
{
  "relatedUserId": "uuid-of-user",
  "role": 0
}
```

---

### Accept Invite

Accept a chat room invitation.

```
POST /api/chat/invites/{roomId:guid}/accept
```

---

### Decline Invite

Decline a chat room invitation.

```
POST /api/chat/invites/{roomId:guid}/decline
```

---

### Join Community Chat

Join a community-type chat room.

```
POST /api/chat/{roomId:guid}/members/me
```

---

## Chat Room CRUD Endpoints

### Get Chat Room

```
GET /api/chat/{id:guid}
```

### List Joined Chat Rooms

```
GET /api/chat
```

### Create Chat Room

```
POST /api/chat
```

### Update Chat Room

```
PATCH /api/chat/{id:guid}
```

### Delete Chat Room

```
DELETE /api/chat/{id:guid}
```

---

## Encryption Endpoints

### Enable MLS Encryption

```
POST /api/chat/{id:guid}/mls/enable
```

**Note:** Legacy endpoint `POST /api/chat/{id}/e2ee/enable` returns `410 Gone`.

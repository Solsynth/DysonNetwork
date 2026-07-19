# Chat Documentation

This is the entry point for all documentation related to the DysonNetwork chat subsystem (Messager service). The chat system covers real-time messaging, room management, voice calls, end-to-end encryption, and WebSocket-based event delivery.

## Architecture Overview

```
Client App
    │
    ├──── HTTP (REST) ──────────► /api/chat/* ──────► ChatController / ChatRoomController / RealtimeCallController
    │
    └──── WebSocket ────────────► Ring WebSocket Gateway
                                      │
                                      ├──► DysonNetwork.Messager (packet handling)
                                      │       └── RemoteWebSocketService
                                      │               └── WebSocketService (gRPC)
                                      │
                                      └──► DysonNetwork.Passport (presence, status)
```

- **Controllers** (`DysonNetwork.Messager/Chat/`): Handle HTTP REST requests.
- **ChatService**: Core business logic for messages, reactions, sync, typing, etc.
- **ChatRoomService**: Room membership, subscriptions, and room-level operations.
- **ChatPinService**: Message pin/unpin operations.
- **RemoteWebSocketService** (`DysonNetwork.Shared.Registry`): Packets to Ring for real-time delivery.
- **IRealtimeService / LiveKitService**: Voice/video call session management.
- **Ring WebSocket Gateway**: External service that holds active WebSocket connections and routes packets.

---

## REST API

### Core API

| Document | Description |
|---|---|
| [CHAT_API](./CHAT_API.md) | ChatController REST API: messages CRUD, summary, unread, subscriptions, sync, autocomplete, MLS devices, pins, bot commands |
| [CHAT_MESSAGE_SEARCH](./CHAT_MESSAGE_SEARCH.md) | Cross-room cloud search for plaintext chat messages |
| [SHARED_API_ERRORS](./SHARED_API_ERRORS.md) | Standard `ApiError` payload and client-handling guidance |
| [CHAT_ROOM_CONTROLLER](./CHAT_ROOM_CONTROLLER.md) | ChatRoomController REST API: room create/update/delete, member management, invites, timeout, groups, moderation |

### Message Types

| Document | Description |
|---|---|
| [CHAT_MESSAGE_REACTIONS](./CHAT_MESSAGE_REACTIONS.md) | Add, remove, and list message reactions |
| [CHAT_MESSAGE_PINS](./CHAT_MESSAGE_PINS.md) | Pin and unpin messages in rooms |
| [CHAT_VOICE_MESSAGES](./CHAT_VOICE_MESSAGES.md) | Upload and stream voice messages |
| [CHAT_PLACEHOLDER_MESSAGES](./CHAT_PLACEHOLDER_MESSAGES.md) | Streaming and upload-progress placeholder messages |
| [CHAT_REDIRECT_MESSAGES](./CHAT_REDIRECT_MESSAGES.md) | Redirect chat history between rooms |
| [CHAT_MARK_ALL_READ](./CHAT_MARK_ALL_READ.md) | Mark all rooms as read |

### Sync

| Document | Description |
|---|---|
| [CHAT_ROOM_SYNC](./CHAT_ROOM_SYNC.md) | Incremental room-list sync (joined rooms, changes, summaries) |
| [CHAT_ROOM_SEQUENCE_SYNC](./CHAT_ROOM_SEQUENCE_SYNC.md) | Room-sequence-based gap recovery for missing messages |
| [CHAT_GLOBAL_SYNC](./CHAT_GLOBAL_SYNC.md) | Cross-room message sync for offline-first clients |

### Room Management

| Document | Description |
|---|---|
| [CHAT_GROUPS](./CHAT_GROUPS.md) | Chat folders/groups for organizing rooms |

### End-to-End Encryption

| Document | Description |
|---|---|
| [CHAT_E2EE_INTEGRATION](./CHAT_E2EE_INTEGRATION.md) | E2EE and MLS encryption integration in chat |
| [CHAT_MLS_MIGRATION](./CHAT_MLS_MIGRATION.md) | Migration to MLS-based encryption |
| [CHAT_MLS_CLIENT_MIGRATION](./CHAT_MLS_CLIENT_MIGRATION.md) | Client-side MLS migration guide |

### System Features

| Document | Description |
|---|---|
| [CHAT_SYSTEM_MESSAGES](./CHAT_SYSTEM_MESSAGES.md) | Backend-generated system messages (member joined, encryption enabled, etc.) |
| [STATUS_AND_CHAT_ONLINE_API](./STATUS_AND_CHAT_ONLINE_API.md) | Account status and online member presence |
| [BOT_CHAT_API](./BOT_CHAT_API.md) | Bot chat integration: commands, webhooks, developer impersonation |
| [BOT_CHAT_MIGRATION](./BOT_CHAT_MIGRATION.md) | Bot chat migration guide |

---

## WebSocket & Real-Time

### Packet Reference

| Document | Description |
|---|---|
| [CHAT_WEBSOCKET_INFRASTRUCTURE](./CHAT_WEBSOCKET_INFRASTRUCTURE.md) | WebSocket infrastructure: RemoteWebSocketService, packet types, gRPC methods, fan-out model |
| [CHAT_WEBSOCKET_SEND_MESSAGE](./CHAT_WEBSOCKET_SEND_MESSAGE.md) | Send messages via WebSocket (`messages.send` / `messages.delivered` / `messages.new`) |
| [CHAT_WEBSOCKET_TYPING](./CHAT_WEBSOCKET_TYPING.md) | Typing indicators via WebSocket (`messages.typing`) |
| [CALL_INVITED_WEBSOCKET](./CALL_INVITED_WEBSOCKET.md) | Incoming call invitation WebSocket packet (`call.invited`) |

### Calls

| Document | Description |
|---|---|
| [REALTIME_CALL_API](./REALTIME_CALL_API.md) | Voice/video call REST API (LiveKit-based): join, leave, participants, moderation |
| [CALL_INVITED_WEBSOCKET](./CALL_INVITED_WEBSOCKET.md) | Call invitation WebSocket packet |

### Delivery & Presence

| Document | Description |
|---|---|
| [WEBSOCKET_PRESENCE_BROADCASTS](./WEBSOCKET_PRESENCE_BROADCASTS.md) | Presence change broadcasts over WebSocket |
| [WEBSOCKET_PUSH_EXCLUSIONS](./WEBSOCKET_PUSH_EXCLUSIONS.md) | Device exclusion patterns for push delivery |

---

## Key Concepts

### Message Flow

1. Client sends message via `POST /api/chat/{roomId}/messages` or `messages.send` WebSocket packet.
2. `ChatService.SendMessageAsync` persists the message and assigns a `room_sequence`.
3. `RemoteWebSocketService.PushWebSocketPacket` broadcasts `messages.new` to subscribed room members.
4. If the sender used WebSocket, their device gets `messages.delivered` as an ack.

### Room Types

| Type | Description |
|---|---|
| `Group` | Multi-user chat room. Can be public or private, realm-linked or standalone. |
| `DirectMessage` | Two-user chat room. Supports auto-approval for bots. |

### Encryption Modes

| Mode | Description |
|---|---|
| `None` | Plaintext messaging. |
| `E2eeMls` | End-to-end encryption using MLS (Message Layer Security). Requires `chat.mls.v2` encryption scheme. |

### Real-Time Delivery Model

- Messager does **not** host WebSocket connections directly.
- Packets are pushed via gRPC to the Ring WebSocket Gateway.
- Ring routes packets to connected devices based on user ID or device ID.
- Push notifications serve as a fallback when no WebSocket connection is active.
- See [CHAT_WEBSOCKET_INFRASTRUCTURE](./CHAT_WEBSOCKET_INFRASTRUCTURE.md) for the full architecture.

### Sync Strategy

| Sync Type | Use Case |
|---|---|
| Room sync (`POST /api/chat/rooms/sync`) | Incremental room-list updates (new rooms, removed rooms, room metadata changes) |
| Room sequence sync (`POST /api/chat/{roomId}/sync`) | Message gap recovery within a single room using `room_sequence` |
| Global sync (`POST /api/chat/sync`) | Cross-room message sync for offline-first clients |

---

## Controllers & Services

| Component | File | Description |
|---|---|---|
| `ChatController` | `Chat/ChatController.cs` | Messages CRUD, reactions, sync, autocomplete, pins, bot commands, MLS devices |
| `ChatRoomController` | `Chat/ChatRoomController.cs` | Room CRUD, members, invites, timeout, groups, moderation |
| `RealtimeCallController` | `Chat/RealtimeCallController.cs` | Call join/leave/participants/moderation |
| `RealmChatController` | `Chat/RealmChatController.cs` | Realm-scoped room listing |
| `ChatService` | `Chat/ChatService.cs` | Core messaging logic, typing, system messages, reactions hydration |
| `ChatRoomService` | `Chat/ChatRoomService.cs` | Room membership, subscriptions, member hydration |
| `ChatPinService` | `Chat/ChatPinService.cs` | Pin/unpin logic |
| `RemoteWebSocketService` | `Shared/Registry/RemoteWebSocketService.cs` | WebSocket packet push via gRPC |
| `IRealtimeService` | `Chat/Realtime/IRealtimeService.cs` | Real-time call provider interface |
| `LiveKitService` | `Chat/Realtime/LiveKitService.cs` | LiveKit-based call implementation |

---

## Permissions Reference

Chat-related permission keys (from `PermissionKeys`):

| Key | Description |
|---|---|
| `chat.create` | Create new chat rooms |
| `chat.update` | Update room settings |
| `chat.delete` | Delete rooms |
| `chat.messages.create` | Send messages |
| `chat.messages.update` | Edit messages |
| `chat.messages.delete` | Delete messages |
| `chat.messages.react` | React to messages |
| `chat.members.manage` | Update member profiles |
| `chat.members.timeout` | Timeout members |
| `chat.members.kick` | Remove members |
| `chat.invites.manage` | Invite members |
| `chat.e2ee.manage` | Manage E2EE settings |
| `chat.sync` | Global sync permission |
| `chat.call.start` | Start calls |
| `chat.call.end` | End calls |
| `chat.call.invite` | Invite to calls |
| `chat.call.kick` | Kick from calls |
| `chat.call.mute` | Mute in calls |
| `chat.groups.manage` | Manage chat groups |
| `chat.pins.manage` | Pin/unpin messages |
| `chat.read.all` | Mark all as read |

---

## Related Documentation

- [Notification Subscriptions](./NOTIFICATION_SUBSCRIPTIONS.md)
- [Realtime Post Updates](./REALTIME_POST_UPDATES.md)
- [Presence Activity API](./PRESENCE_ACTIVITY_API.md) (referenced by online/status)
- [LiveKit Documentation](https://docs.livekit.io)

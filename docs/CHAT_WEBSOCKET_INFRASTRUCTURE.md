# Chat WebSocket Infrastructure

This document describes the WebSocket infrastructure used by `DysonNetwork.Messager` to deliver real-time chat events to clients, including the `RemoteWebSocketService`, packet types, and the gateway routing model.

## Overview

The Messager service does not host WebSocket connections directly. Instead, it delegates all real-time packet delivery to the **Ring WebSocket service** via gRPC. The `RemoteWebSocketService` class wraps this gRPC client.

```
Client App ←→ WebSocket Gateway (Ring) ←→ Messager (via RemoteWebSocketService)
```

Messager calls `RemoteWebSocketService` to push packets, and Ring routes them to the correct connected devices.

### Key Components

| Component | Location | Role |
|---|---|---|
| `RemoteWebSocketService` | `DysonNetwork.Shared.Registry` | C# wrapper around the WebSocket gRPC client |
| `WebSocketService` | `DysonNetwork.Shared.Proto` | Auto-generated gRPC service definition |
| `WebSocketPacket` | Proto definition | The wire-format packet (`type` + `data` bytes) |
| Ring WebSocket service | External service | Holds active WS connections and routes packets |

---

## RemoteWebSocketService

`RemoteWebSocketService` provides methods to push packets at different granularities:

### Targeting Methods

| Method | Target | Description |
|---|---|---|
| `PushWebSocketPacket(accountId, type, data)` | Single user | Push to all of a user's connected devices |
| `PushWebSocketPacket(accountId, type, data, excludedDeviceIds)` | Single user minus devices | Push to a user's devices except those listed |
| `PushWebSocketPacketToUsers(userIds, type, data)` | Multiple users | Fan-out to a list of user IDs |
| `PushWebSocketPacketToDevice(deviceId, type, data)` | Single device | Push to one specific device |
| `PushWebSocketPacketToDevices(deviceIds, type, data)` | Multiple devices | Push to a list of specific devices |

### Connection Status Methods

| Method | Description |
|---|---|
| `GetWebsocketConnectionStatus(deviceIdOrUserId, isUserId)` | Check if a device or user is currently connected |
| `GetWebsocketConnectionStatusBatch(userIds)` | Batch check connection status for multiple users |
| `GetAllConnectedUserIds()` | Get all user IDs with at least one active WS connection |

### Excluding Devices

When a user sends a message from their own device, the server typically excludes that device from the broadcast using the `excludedWebSocketDeviceIds` override. This prevents echo:

```csharp
await ws.PushWebSocketPacket(
    accountId.ToString(),
    "messages.new",
    payload,
    excludedWebSocketDeviceIds: [currentDeviceId]
);
```

> See [WebSocket Push Exclusions](./WEBSOCKET_PUSH_EXCLUSIONS.md) for more patterns.

---

## Packet Format

All WebSocket packets follow the `DyWebSocketPacket` proto structure:

```protobuf
message DyWebSocketPacket {
  string type = 1;
  bytes data = 2;
  string error_message = 3;
}
```

| Field | Type | Description |
|---|---|---|
| `type` | string | Packet type identifier (e.g. `messages.new`, `messages.typing`) |
| `data` | bytes | JSON-encoded payload |
| `error_message` | string | Optional error message for error-type packets |

### Gateway Envelope

When sending packets **from the client to the server**, the payload is wrapped in a gateway envelope:

```json
{
  "type": "<packet-type>",
  "endpoint": "DysonNetwork.Messager",
  "data": { ... }
}
```

The `endpoint` field routes the packet to the correct backend service.

### Broadcast Envelope

When the server pushes packets **to clients**, the `data` field is the raw JSON payload without the envelope wrapper.

---

## Chat Packet Types

### Server → Client Packets

| Type | Description | Trigger |
|---|---|---|
| `messages.new` | New chat message in a room | Message is sent by another user |
| `messages.delivered` | Ack for own message sent via WS | WS send succeeds |
| `messages.typing` | Typing/speaking/uploading indicator | Another user sends a typing packet |
| `call.invited` | Incoming call invitation | Another user invites to a call |

### Client → Server Packets

| Type | Description | Handler |
|---|---|---|
| `messages.send` | Send a chat message over WS | `ChatController.SendMessage` logic |
| `messages.typing` | Report typing/speaking/uploading state | Broadcast to room members |

> See [CHAT_WEBSOCKET_SEND_MESSAGE](./CHAT_WEBSOCKET_SEND_MESSAGE.md) for `messages.send` details.
> See [CHAT_WEBSOCKET_TYPING](./CHAT_WEBSOCKET_TYPING.md) for `messages.typing` details.
> See [CALL_INVITED_WEBSOCKET](./CALL_INVITED_WEBSOCKET.md) for `call.invited` details.

---

## gRPC Service Methods

The underlying `WebSocketService` gRPC contract:

| Method | Request → Response | Description |
|---|---|---|
| `PushWebSocketPacket` | `DyPushWebSocketPacketRequest` → `Empty` | Push to one user |
| `PushWebSocketPacketToUsers` | `DyPushWebSocketPacketToUsersRequest` → `Empty` | Push to multiple users |
| `PushWebSocketPacketToDevice` | `DyPushWebSocketPacketToDeviceRequest` → `Empty` | Push to one device |
| `PushWebSocketPacketToDevices` | `DyPushWebSocketPacketToDevicesRequest` → `Empty` | Push to multiple devices |
| `GetWebsocketConnectionStatus` | `DyGetWebsocketConnectionStatusRequest` → `DyGetWebsocketConnectionStatusResponse` | Check connection |
| `GetWebsocketConnectionStatusBatch` | `DyGetWebsocketConnectionStatusBatchRequest` → `DyGetWebsocketConnectionStatusBatchResponse` | Batch check |
| `GetAllConnectedUserIds` | `Empty` → `DyGetAllConnectedUserIdsResponse` | All connected users |
| `ReceiveWebSocketPacket` | `DyReceiveWebSocketPacketRequest` → `Empty` | Receive from client |

### PushWebSocketPacketRequest

```protobuf
message DyPushWebSocketPacketRequest {
  string user_id = 1;
  DyWebSocketPacket packet = 2;
  repeated string excluded_websocket_device_ids = 3;
}
```

### DyPushWebSocketPacketToUsersRequest

```protobuf
message DyPushWebSocketPacketToUsersRequest {
  repeated string user_ids = 1;
  DyWebSocketPacket packet = 2;
}
```

### DyPushWebSocketPacketToDeviceRequest

```protobuf
message DyPushWebSocketPacketToDeviceRequest {
  string device_id = 1;
  DyWebSocketPacket packet = 2;
}
```

### DyPushWebSocketPacketToDevicesRequest

```protobuf
message DyPushWebSocketPacketToDevicesRequest {
  repeated string device_ids = 1;
  DyWebSocketPacket packet = 2;
}
```

---

## Delivery Semantics

### Fan-out Model

When a message is sent in a chat room:

1. The message is persisted to the database.
2. Messager resolves the set of subscribed members (subscribed = has an active push subscription or WS connection).
3. For each subscribed member, `PushWebSocketPacket` is called with the `messages.new` payload.
4. Ring delivers the packet to all connected devices for that user.

### Subscription Model

Chat subscriptions are separate from WebSocket connections:

- A **subscription** indicates "this user wants real-time updates for this room."
- A **WebSocket connection** indicates "this device is currently online."
- If a user is subscribed but not connected, Ring falls back to push notification delivery.
- If a user is connected but not subscribed, no WS packets are sent (but push may still fire).

Subscription lifecycle is typically managed by the client by receiving `messages.subscribe` / `messages.unsubscribe` packets on the gateway. The server tracks these in its subscription/cache layer.

### Multidevice Delivery

A single user may have multiple devices connected:

- By default, packets are sent to **all** devices.
- For the sending device, the server uses `excludedWebSocketDeviceIds` to prevent echo.
- Clients can target specific devices with `PushWebSocketPacketToDevice`.

---

## Code Usage in Messager

### Sending a Message (ChatService)

When `SendMessageAsync` succeeds:

```csharp
var serialized = InfraObjectCoder.ConvertObjectToByteString(message);
// Broadcast to room members
await ws.PushWebSocketPacket(
    memberId.ToString(),
    "messages.new",
    serialized.ToByteArray()
);
```

### Typing Indicator (ChatService)

When a `messages.typing` packet is received from a client:

```csharp
// Validate membership, then broadcast to room
await ws.PushWebSocketPacket(
    accountId.ToString(),
    "messages.typing",
    payload
);
```

### Call Invitation (RealtimeCallController)

When a user invites another to a call:

```csharp
// VoIP push (best-effort)
_ = ringClient.SendPushNotificationToUserAsync(request);

// WebSocket push (best-effort)
_ = ws.PushWebSocketPacket(
    targetAccountId.ToString(),
    WebSocketPacketType.CallInvited,
    InfraObjectCoder.ConvertObjectToByteString(invitePayload).ToByteArray()
);
```

### Chat Status (ChatController)

The `/accounts/me/status` endpoint checks WS connection status per device:

```csharp
var isConnected = await webSocket.GetWebsocketConnectionStatus(device.DeviceToken);
var userHasAny = await webSocket.GetWebsocketConnectionStatus(accountId.ToString(), isUserId: true);
```

---

## Error Handling

### Failed Pushes

Push operations are generally **fire-and-forget**. If a gRPC call fails:

- The method throws an RPC exception.
- Callers typically wrap calls in try/catch and log, but do not fail the parent request.
- Example from `RealtimeCallController`:

```csharp
catch (Exception)
{
    // VoIP push is best-effort, don't fail the endpoint
}
```

### Error Packets

When the server needs to notify a specific client of an error (e.g. WS message validation failure), it sends:

```json
{
  "type": "error",
  "error_message": "Description of what went wrong"
}
```

The `error_message` field of `DyWebSocketPacket` is used for this purpose.

---

## Connection Status Messaging

The `GetWebsocketConnectionStatus` methods are used to:

1. Show online indicators in member lists.
2. Determine if push notifications should be suppressed (user is already seeing updates via WS).
3. The chat status endpoint (`GET /accounts/me/status`) exposes this to clients.

Batch status checks are used to populate the `withStatus` flag on `GET /{roomId}/members`:

```csharp
var memberStatuses = await remoteAccountsHelper.GetAccountStatusBatch(members);
```

---

## Related Documentation

- [Chat Documentation Index](./CHAT_INDEX.md) — full list of chat docs
- [Chat API](./CHAT_API.md)
- [Chat WebSocket Send Message](./CHAT_WEBSOCKET_SEND_MESSAGE.md)
- [Chat WebSocket Typing](./CHAT_WEBSOCKET_TYPING.md)
- [Call Invited WebSocket](./CALL_INVITED_WEBSOCKET.md)
- [WebSocket Presence Broadcasts](./WEBSOCKET_PRESENCE_BROADCASTS.md)
- [WebSocket Push Exclusions](./WEBSOCKET_PUSH_EXCLUSIONS.md)
- [Chat Room Sync](./CHAT_ROOM_SYNC.md)
- [Chat Global Sync](./CHAT_GLOBAL_SYNC.md)
- [Notification Subscriptions](./NOTIFICATION_SUBSCRIPTIONS.md)

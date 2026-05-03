# WebSocket Presence Broadcasts

The websocket gateway publishes lifecycle events when devices connect and disconnect. Passport and Messager consume those lifecycle events to notify different audiences about account online status changes.

## Gateway Events

Lifecycle subjects use the configured websocket subject prefix. With the default `nats.websocketSubjectPrefix = websocket_`, the gateway publishes:

| Lifecycle | NATS subject |
| --- | --- |
| Connected | `websocket_connected` |
| Disconnected | `websocket_disconnected` |

Both subjects use the `websocket_connections` stream.

Example connected payload:

```json
{
  "event_id": "3f72f70d-674e-4d61-b578-4aa856d48e1b",
  "timestamp": "2026-05-04T10:15:30Z",
  "stream_name": "websocket_connections",
  "event_type": "connected",
  "account_id": "7f7ce7e7-ff9a-44ec-b77a-9ad7ac1b4235",
  "device_id": "device-token"
}
```

Example disconnected payload:

```json
{
  "event_id": "7267130d-c75e-4f72-bfd5-d51cb1311450",
  "timestamp": "2026-05-04T10:18:30Z",
  "stream_name": "websocket_connections",
  "event_type": "disconnected",
  "account_id": "7f7ce7e7-ff9a-44ec-b77a-9ad7ac1b4235",
  "device_id": "device-token",
  "is_offline": true
}
```

`is_offline` is only present on disconnect events. Consumers must ignore disconnect events where `is_offline` is `false`, because the account still has another connected websocket device.

## Friend Status Broadcast

Passport broadcasts account status updates to friends.

Packet type:

```text
account.status.updated
```

Audience:

```text
All friend account IDs returned by RelationshipService.ListAccountFriends(account_id)
```

Payload:

```json
{
  "account_id": "7f7ce7e7-ff9a-44ec-b77a-9ad7ac1b4235",
  "status": {
    "account_id": "7f7ce7e7-ff9a-44ec-b77a-9ad7ac1b4235",
    "is_online": true,
    "is_customized": false,
    "label": "Online",
    "type": 0,
    "attitude": 2
  },
  "device_id": "device-token",
  "timestamp": "2026-05-04T10:15:30Z"
}
```

Passport uses `AccountEventService.GetStatus` before sending, so invisible status is applied consistently with the existing account status API. When a previous status snapshot exists, Passport compares it with the new status through `BroadcastEventHandler.StatusesEqual` and skips unchanged friend broadcasts.

## Chat Room Subscriber Broadcast

Messager broadcasts room-scoped presence updates to active websocket subscribers of rooms containing the changed account.

Packet type:

```text
chat.presence.updated
```

Audience:

```text
For each active room containing account_id, all currently subscribed room members except account_id
```

Room subscriptions are the existing cache-backed subscriptions created by `messages.subscribe` and removed by `messages.unsubscribe`. They expire unless clients keep refreshing the subscription.

Payload:

```json
{
  "room_id": "846ffb52-7522-4d5f-8998-9f62f39c7ac4",
  "member_id": "bff110bd-30d3-493b-a264-8d6e9ce60106",
  "account_id": "7f7ce7e7-ff9a-44ec-b77a-9ad7ac1b4235",
  "status": {
    "account_id": "7f7ce7e7-ff9a-44ec-b77a-9ad7ac1b4235",
    "is_online": true,
    "is_customized": false,
    "label": "Online",
    "type": 0,
    "attitude": 2
  },
  "device_id": "device-token",
  "timestamp": "2026-05-04T10:15:30Z"
}
```

This packet is intentionally room-scoped even though the event is account-level. Clients can update presence in a specific open room without resolving the account to an in-room member locally.

## Duplicate Delivery Rules

Friend and room subscriber broadcasts are independent.

## Presence Activity Broadcasts

Presence activities are rich presence records from `PresenceActivityController` and `IPresenceService` implementations such as Steam. Mutations are centralized in `AccountEventService`, so realtime updates are emitted for both manual API changes and scheduled provider refreshes.

Passport broadcasts activity changes to friends.

Packet type:

```text
account.presence.activities.updated
```

Audience:

```text
All friend account IDs returned by RelationshipService.ListAccountFriends(account_id)
```

Payload:

```json
{
  "account_id": "7f7ce7e7-ff9a-44ec-b77a-9ad7ac1b4235",
  "activities": [
    {
      "id": "4753f715-a1dc-48c2-9d14-622f16457b72",
      "account_id": "7f7ce7e7-ff9a-44ec-b77a-9ad7ac1b4235",
      "type": 1,
      "manual_id": "steam",
      "title": "Dyson Sphere Program",
      "subtitle": "Playing on Steam",
      "lease_expires_at": "2026-05-04T10:25:30Z"
    }
  ],
  "timestamp": "2026-05-04T10:15:30Z"
}
```

Messager also broadcasts activity changes to active room subscribers.

Packet type:

```text
chat.presence.activities.updated
```

Audience:

```text
For each active room containing account_id, all currently subscribed room members except account_id
```

Payload:

```json
{
  "room_id": "846ffb52-7522-4d5f-8998-9f62f39c7ac4",
  "member_id": "bff110bd-30d3-493b-a264-8d6e9ce60106",
  "account_id": "7f7ce7e7-ff9a-44ec-b77a-9ad7ac1b4235",
  "activities": [
    {
      "id": "4753f715-a1dc-48c2-9d14-622f16457b72",
      "account_id": "7f7ce7e7-ff9a-44ec-b77a-9ad7ac1b4235",
      "type": 1,
      "manual_id": "steam",
      "title": "Dyson Sphere Program",
      "subtitle": "Playing on Steam",
      "lease_expires_at": "2026-05-04T10:25:30Z"
    }
  ],
  "timestamp": "2026-05-04T10:15:30Z"
}
```

Scheduled provider refreshes only emit realtime updates when the visible activity content changes or an activity becomes active/inactive. Lease-only refreshes with unchanged visible content are ignored to avoid noisy broadcasts.

## Duplicate Delivery Rules

Friend and room subscriber broadcasts are independent.

If a recipient is both a friend and a subscribed member of a room containing the account, the recipient receives both account-level and chat-room-level packets:

| Packet | Reason |
| --- | --- |
| `account.status.updated` | Friend-level account status update |
| `chat.presence.updated` | Room-scoped member presence update |
| `account.presence.activities.updated` | Friend-level rich presence activity update |
| `chat.presence.activities.updated` | Room-scoped member rich presence activity update |

Clients should handle these as separate update channels. The friend packets update global account presence. The chat packets update the account's member presence in a room.

## Disconnect Handling

Disconnect lifecycle events are only treated as offline transitions when:

```json
{
  "is_offline": true
}
```

If `is_offline` is `false`, services do not broadcast presence updates. This avoids incorrectly marking an account offline when only one of multiple devices disconnected.

## Implementation Points

Shared packet constants live in:

```text
DysonNetwork.Shared/Models/WebSocket.cs
```

Shared lifecycle event models live in:

```text
DysonNetwork.Shared/Queue/WebSocketPacketEvent.cs
```

Shared account presence activity events live in:

```text
DysonNetwork.Shared/Queue/AccountEvent.cs
```

Passport friend broadcasts are wired in:

```text
DysonNetwork.Passport/Startup/ServiceCollectionExtensions.cs
```

Passport presence activity mutation broadcasts are wired in:

```text
DysonNetwork.Passport/Account/AccountEventService.cs
```

Messager room subscriber broadcasts are wired in:

```text
DysonNetwork.Messager/Startup/ServiceCollectionExtensions.cs
```

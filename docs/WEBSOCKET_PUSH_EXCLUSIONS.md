# WebSocket Push Exclusions

This note describes the notification delivery change between Ring and the websocket gateway.

## Ring Side

Ring keeps using the existing user-level websocket push call.

One device only gets one provider push even if it has multiple active subscriptions.

Provider priority per device is:

1. SOP
2. FCM / Google
3. UnifiedPush
4. APNs

Ring still sends notification websocket packets to the websocket gateway, but it now passes a device exclusion list for the target user when needed.

## Gateway Contract

The websocket gateway must support a new field on the existing `DyPushWebSocketPacketRequest`:

```proto
repeated string excluded_websocket_device_ids = 3;
```

Meaning:

- `user_id` is still the websocket delivery target.
- `excluded_websocket_device_ids` contains gateway device IDs that must not receive the packet.
- If the list is empty, behavior stays unchanged.

## Gateway Behavior

When the gateway receives `PushWebSocketPacket`:

1. Resolve all live websocket connections for `user_id`.
2. Skip any connection whose device ID appears in `excluded_websocket_device_ids`.
3. Deliver the packet to the remaining matching connections.

This lets Ring say "push to the user, but not these devices" without sending separate packets per device.

## Example

If a user has three active websocket devices and Ring wants to suppress one of them:

```json
{
  "user_id": "1f84667d-9ac4-46e1-8db0-b197c16a6c18",
  "excluded_websocket_device_ids": ["device-a"]
}
```

The gateway should send the packet to the other connected devices only.

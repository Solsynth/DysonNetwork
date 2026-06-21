# VoIP Notification Subscriptions

This document explains how Ring expects clients to register subscriptions for incoming VoIP call notifications.

## Short Answer

Yes. On Apple platforms, VoIP notifications should be registered as a separate push subscription with a separate token.

In the current Ring implementation, that separate subscription uses:

- `provider = 4`
- provider name `Appk`
- token source: Apple PushKit VoIP token

This is distinct from the normal Apple alert push subscription:

- `provider = 1`
- provider name `Apple`
- token source: normal APNs device token

## Why A Separate Subscription Exists

Ring stores push subscriptions by:

- account
- device ID
- provider

That means one device session can hold multiple subscriptions at the same time, for example:

1. normal APNs alert push
2. PushKit VoIP push
3. SOP fallback

When a notification has `push_type = "VoIP"`, Ring selects the best provider for that device and prefers `Appk` over normal Apple push.

## Registration Endpoint

VoIP subscriptions use the normal push subscription endpoint:

```text
PUT /api/notifications/subscription
```

There is no dedicated `/voip/subscription` endpoint today.

The request is authenticated with the normal bearer token for the current session.

## Request Format

Register the PushKit token like this:

```json
{
  "device_token": "<pushkit-voip-token>",
  "device_name": "iPhone 16 Pro",
  "provider": 4
}
```

Successful subscription responses now include:

- `provider`: numeric enum value

## Provider Values

| Provider | Enum Value | Meaning |
|---|---|---|
| `Apple` | `1` | Standard APNs alerts |
| `Appk` | `4` | Apple PushKit VoIP token |
| `Sop` | `2` | Ring-native SSE/token push |

## Recommended Apple Client Flow

For an iOS client that supports incoming calls:

1. Register the normal APNs token as `provider = 1`.
2. Register the PushKit VoIP token as `provider = 4`.
3. Use the same authenticated device session for both registrations.
4. Keep both subscriptions updated when either token changes.

This lets Ring deliver:

- normal chat/message notifications through standard push
- incoming call notifications through PushKit VoIP push

## Server Selection Rules

For one device, Ring groups subscriptions by device ID and chooses one provider per notification.

For VoIP notifications, current priority is:

1. `Sop` if an active SOP stream is connected for that device
2. `Appk`
3. `Apple`
4. `Google`
5. `UnifiedPush`

For non-VoIP notifications, `Appk` is excluded and Ring prefers normal providers.

## Relationship To `call.invited`

`DysonNetwork.Messager` sends incoming call invites with:

- `topic = "call.incoming"`
- `push_type = "VoIP"`

Ring uses `push_type` to decide that this notification is a VoIP-class delivery and should prefer the VoIP-capable subscription.

Separately, `Messager` also sends a websocket packet:

- packet type: `call.invited`

Clients should handle both signals and deduplicate by call identity.

See [CALL_INVITED_WEBSOCKET](./CALL_INVITED_WEBSOCKET.md).

## Important Current Caveat

The current Ring code strongly implies that real Apple VoIP delivery should use `provider = 4` (`Appk`).

There is also a current implementation detail in `PushService`:

- `PushProvider.Appk` sends APNs requests with `apn-push-type = voip`
- normal `PushProvider.Apple` does not currently switch into VoIP mode even when `push_type = "VoIP"`

So in practice, if the client wants reliable Apple VoIP behavior, it should register the separate PushKit token as `Appk`.

## Example cURL

```bash
curl -X PUT "/api/notifications/subscription" \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{
    "device_token": "<pushkit_voip_token>",
    "device_name": "iPhone 16 Pro",
    "provider": 4
  }'
```

## Suggested Client Behavior

- Treat APNs and PushKit as two different registrations.
- Re-register when the PushKit token rotates.
- Do not replace the normal APNs subscription with the VoIP one.
- Keep the websocket `call.invited` path enabled even when VoIP push is available.

## Related Docs

- [NOTIFICATION_SUBSCRIPTIONS](./NOTIFICATION_SUBSCRIPTIONS.md)
- [SOP_PUSH_API](./SOP_PUSH_API.md)
- [CALL_INVITED_WEBSOCKET](./CALL_INVITED_WEBSOCKET.md)
- [REALTIME_CALL_API](./REALTIME_CALL_API.md)

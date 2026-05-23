# Passport Idle Presence

This document describes the websocket-driven idle presence flow implemented in Passport.

## Overview

Passport now derives three presence fields for `SnAccountStatus`:

- `is_online`: the account has at least one active websocket connection and the status is not invisible
- `is_idle`: the account is online and every tracked active websocket connection for that account is idle
- `idle_since`: the instant when the account became fully idle

Idle is tracked per websocket device as a timestamp, then reduced to an account-level value.

## Event Bus Integration

Passport listens to these event bus subjects in `DysonNetwork.Passport/Startup/ServiceCollectionExtensions.cs`:

- `websocket_connected`
- `websocket_disconnected`
- `websocket_passport`

The existing connected/disconnected events are used to maintain the per-device presence map:

- connect: add device state with `idle_since = null`
- disconnect: remove device state when `is_offline = true`

The `websocket_passport` listener now parses websocket packets and handles:

- `status.idle`

Accepted packet payloads:

```json
true
```

```json
false
```

```json
{
  "is_idle": true
}
```

```json
{
  "is_idle": false
}
```

If the packet does not contain an explicit boolean payload, Passport treats `status.idle` as idle and uses the websocket event timestamp as `idle_since`.

## Account Status Resolution

`DysonNetwork.Passport/Account/AccountEventService.cs` now keeps a cache entry per account:

- key prefix: `account:connection-idle-state:`
- value: `Dictionary<string, WebSocketConnectionPresenceState>` where key is websocket `device_id` and value contains `idle_since`

Status resolution rules:

1. Query websocket connectivity for the account.
2. If offline, clear the tracked idle state and return `is_idle = false`, `idle_since = null`.
3. If online, inspect the tracked device map.
4. Return `is_idle = true` only when:
   - at least one device is tracked
   - all tracked devices are idle
5. When all tracked devices are idle, account `idle_since` is the latest device-level `idle_since`.

This means:

- one active device prevents account idle
- mixed idle/active devices prevent account idle
- all idle devices make the account idle
- account `idle_since` is when the last active device became idle

## Broadcast Behavior

Passport already broadcasts `account.status.updated` to friends when account status changes.

Idle transitions and `idle_since` changes now participate in the equality check, so these changes also trigger a broadcast:

- online -> idle
- idle -> active
- mixed-device state changes that flip the account-level idle result

The websocket status payload now includes `status.is_idle` and `status.idle_since` in JSON responses and pushed websocket packets because `SnAccountStatus` contains non-mapped `IsIdle` and `IdleSince` properties.

## Proto Contract

`DysonSpec/proto/profile.proto` now includes:

```proto
bool is_idle = 18;
google.protobuf.Timestamp idle_since = 19;
```

Generated C# code in `DysonNetwork.Shared/Proto/` was intentionally not updated in this change.

## Required Follow-Up

After editing the proto, regenerate the shared protobuf outputs, then update the C# mapping in:

- `DysonNetwork.Shared/Models/AccountEvent.cs`

The required mapping changes are:

- `SnAccountStatus.ToProtoValue()`: set `IsIdle`
- `SnAccountStatus.ToProtoValue()`: set `IdleSince`
- `SnAccountStatus.FromProtoValue()`: read `IsIdle`
- `SnAccountStatus.FromProtoValue()`: read `IdleSince`

Until regeneration happens, gRPC consumers will not see `is_idle` or `idle_since`, but Passport HTTP responses and websocket broadcasts already include both fields.

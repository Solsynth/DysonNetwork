# Auth Session Revoked Event

The auth service now publishes an explicit event when a session is actively revoked or logged out. This event is intended for the websocket gateway so it can disconnect live connections tied to revoked auth sessions immediately.

This event does not cover passive session expiry. Expiry handling remains the gateway's responsibility.

## Event Contract

**NATS subject**:

```text
auth.session.revoked
```

**JetStream stream**:

```text
auth_session_events
```

**Shared model**:

[`DysonNetwork.Shared/Queue/AuthSessionEvent.cs`](</Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Shared/Queue/AuthSessionEvent.cs>)

## Payload

Example payload:

```json
{
  "event_id": "6b0d11e0-4af0-48a2-90d9-cde3c4d07c32",
  "timestamp": "2026-05-10T08:30:00Z",
  "stream_name": "auth_session_events",
  "event_type": "auth.session.revoked",
  "session_id": "7d739728-bb2f-4d17-8d9b-c3f0ac969d09",
  "account_id": "1f84667d-9ac4-46e1-8db0-b197c16a6c18",
  "client_id": "b0f74c11-73b5-4a81-84dc-0f91c781b9ef",
  "device_id": "device-token",
  "revoked_at": "2026-05-10T08:30:00Z"
}
```

Field meanings:

| Field | Type | Description |
| --- | --- | --- |
| `event_id` | string | Event bus generated unique ID |
| `timestamp` | string | Event creation timestamp |
| `stream_name` | string | Always `auth_session_events` |
| `event_type` | string | Always `auth.session.revoked` |
| `session_id` | string | Revoked auth session ID |
| `account_id` | string | Account that owned the revoked session |
| `client_id` | string or null | Internal auth client ID tied to the session |
| `device_id` | string or null | Gateway-facing device ID from `SnAuthClient.DeviceId` |
| `revoked_at` | string | Timestamp when Padlock revoked the session |

## When It Fires

This event is published only for explicit revocation flows in `AuthService`:

- `RevokeSessionAsync(Guid sessionId)`
- `RevokeAllSessionsForAccountAsync(Guid accountId)`

Current examples include:

- `POST /api/auth/logout`
- Flows that revoke a single session tree
- Flows that revoke all sessions for an account
- API key revocation when it revokes the underlying session through `RevokeSessionAsync`

This event is not emitted for:

- Natural token/session expiry
- Gateway-side expiry detection
- Generic access token timeout without a Padlock revoke operation

## Gateway Consumption Rules

Recommended gateway behavior:

1. Subscribe to `auth.session.revoked`.
2. Find active websocket connections matching the revoked session.
3. Disconnect the matching connection immediately.
4. Ignore the event if no matching live connection exists.

Preferred matching order:

1. `session_id` if the gateway tracks active sockets by auth session ID.
2. `account_id` + `device_id` if the gateway tracks sockets by account/device key.
3. `account_id` + `client_id` if the gateway stores the internal client identifier.

`device_id` can be null for sessions that are not associated with a client record. Consumers should not assume it is always present.

## Disconnect Scope

The event is scoped to the revoked session records returned by Padlock:

- Revoking one session publishes one event per revoked session in that revoke tree.
- Revoking all sessions publishes one event per active session owned by that account.

Consumers should disconnect only the matching live socket for each event, not every socket for the account unless the gateway mapping requires that fallback.

## Source of Truth

Padlock remains the source of truth for explicit logout and revocation.

The websocket gateway should treat `auth.session.revoked` as an immediate disconnect signal, but should continue to enforce its own token/session validation rules for passive expiry and reconnect attempts.

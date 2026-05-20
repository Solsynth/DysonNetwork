# Subscription Queue and Tier Switching

## Overview

Wallet subscriptions now support two related behaviors within the same subscription group:

- repeated purchases are kept as separate subscription records instead of extending the current record in place
- users can immediately switch which already-purchased tier is active inside a group

This is primarily used for the Stellar Program style tiered subscriptions, but the behavior is group-based and applies to any subscription definitions that share the same `group_identifier`.

If you use it through the gateway, the `/api` should be replaced with the `/{service}` prefix for Wallet routes.

## Behavior Summary

### 1. Repeated purchases create a queue

When a user purchases the same subscription again, or purchases another tier in the same group, Wallet creates a new `wallet_subscriptions` record instead of merging the duration into the current one.

The records are ordered by time:

- one record may be active now
- zero or more records may be queued for future activation
- queued records begin when the previous record ends

This preserves purchase history and allows later switching between purchased tiers.

### 2. Pending activations

A queued subscription is a record with:

- `is_active = true`
- `status = active`
- `begun_at > now`

Such a record is already owned by the user, but its entitlement is not active yet.

### 3. Immediate tier switching

Users can select another already-purchased subscription in the same group and make it active immediately.

When that happens:

- the selected subscription starts now
- the previously active subscription is moved behind it in the queue
- the previous active subscription keeps its remaining time
- later queued subscriptions are re-sequenced after the switched records

Wallet does not create a new purchase during this action. The user can only switch to an existing subscription record they already own.

## Important Fields

`SnWalletSubscription` already exposes the main state needed by clients:

- `identifier`: the concrete subscription tier
- `group_identifier`: the shared subscription family
- `begun_at`: when the subscription starts
- `ended_at`: when the subscription ends
- `status`: subscription status
- `is_active`: logical activation flag
- `is_available`: whether the entitlement is active right now
- `is_pending_activation`: whether the record is queued for future activation

`is_pending_activation` is true when the subscription is active in principle but has a future `begun_at`.

## API Endpoints

All subscription endpoints below require authentication.

Base path: `/api/subscriptions`

### 1. List all subscriptions

```http
GET /api/subscriptions?offset=0&take=20
Authorization: Bearer <token>
```

Returns all subscription records for the current user.

Response headers:

- `X-Total`: total record count

### 2. List pending activations

```http
GET /api/subscriptions/pending-activations?offset=0&take=20
Authorization: Bearer <token>
```

Returns queued subscription records for the current user.

Example response:

```json
{
  "total_count": 2,
  "next_activation_at": "2026-06-01T00:00:00Z",
  "subscriptions": [
    {
      "id": "fd8d2e0e-8d82-4bc2-8d8d-95d3a7b78f3e",
      "identifier": "solian.stellar.nova",
      "group_identifier": "solian.stellar",
      "is_pending_activation": true,
      "begun_at": "2026-06-01T00:00:00Z",
      "ended_at": "2026-07-01T00:00:00Z"
    }
  ]
}
```

### 3. Get subscription group state

```http
GET /api/subscriptions/groups/{groupIdentifier}
Authorization: Bearer <token>
```

This endpoint returns:

- `catalog`: tiers in the group
- `current`: currently active subscription, if any
- `next`: next queued subscription, if any
- `subscriptions`: all records in the group

This is the main read endpoint for rendering tier switch UI.

### 4. Activate another purchased tier in the group

```http
POST /api/subscriptions/groups/{groupIdentifier}/activate
Authorization: Bearer <token>
Content-Type: application/json

{
  "subscription_id": "fd8d2e0e-8d82-4bc2-8d8d-95d3a7b78f3e"
}
```

The request must target an existing subscription record that:

- belongs to the current user
- belongs to the specified group
- is still active as an owned entitlement record
- is not cancelled, unpaid, or expired

Response:

- `SubscriptionGroupStateResponse`

The response is the refreshed group state after reordering and activating the selected tier.

## Switching Rules

The current implementation uses these rules:

- switching is limited to already-purchased subscription records
- switching is group-local and cannot move a subscription across groups
- switching does not create a new external provider payment
- switching does not discard the remaining time of the current active tier
- if the selected subscription is already active, the operation is a no-op

When a switch succeeds:

1. the selected tier becomes active immediately
2. its full duration is preserved from its own record
3. the previously active tier is moved after it with its remaining duration preserved
4. any other queued subscriptions are moved after those two records in queue order

## Provider Notes

Provider restores and webhooks now create queueable subscription records instead of always extending the current subscription.

This applies to:

- Afdian
- Paddle
- Apple App Store
- internal wallet purchases

Wallet uses provider-reported timing where available, so restored or webhook-created subscriptions keep their actual cycle duration instead of always defaulting to 30 days.

## Client Guidance

For a tier management page:

1. call `GET /api/subscriptions/groups/{groupIdentifier}`
2. render `current`
3. render queued items using `subscriptions` or `pending-activations`
4. allow the user to pick a queued subscription they already own
5. call `POST /api/subscriptions/groups/{groupIdentifier}/activate`
6. refresh the group state

Clients should treat `subscription_id` as the switch target, not just `identifier`, because a user may own multiple records of the same tier at different queue positions.

## Error Cases

Wallet returns `400 Bad Request` for invalid switch attempts such as:

- group not found
- subscription record not found in that group
- selecting a subscription owned by another user
- selecting a cancelled, unpaid, or expired record

`404 Not Found` is still used by the group read endpoints when the group itself does not exist.

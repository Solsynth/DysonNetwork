# Calendar Event Subscriptions

## Overview

Calendar Event Subscriptions allow users to see other users' events in their own calendar and countdown views. There are two mechanisms:

1. **Friends (automatic)**: Friends' events with `Friends` visibility automatically appear in each other's calendar/countdown — no subscription needed.
2. **Public (explicit subscription)**: For non-friend users, explicit subscription is required to see their `Public` events.

This service is handled by the DysonNetwork.Passport service. When using with the gateway, replace `/api` with `/pass`.

## Visibility Rules

| Event Visibility | Owner | Friends | Subscribers (non-friends) | Anonymous |
|-----------------|-------|---------|--------------------------|-----------|
| `Private` | ✅ | ❌ | ❌ | ❌ |
| `Friends` | ✅ | ✅ (automatic) | ❌ | ❌ |
| `Public` | ✅ | ✅ (automatic) | ✅ (subscription) | ❌ |

## API Endpoints

### Base URL: `/api/accounts/me/calendar/subscriptions`

### Authentication
All endpoints require `[Authorize]` header.

---

## List Subscriptions

Get a list of account IDs the current user has subscribed to.

**Endpoint:** `GET /api/accounts/me/calendar/subscriptions`

**Response:**
```json
[
  "550e8400-e29b-41d4-a716-446655440000",
  "6ba7b810-9dad-11d1-80b4-00c04fd430c8"
]
```

---

## Subscribe to Events

Subscribe to another user's public events. Idempotent — subscribing again returns the existing subscription.

**Endpoint:** `POST /api/accounts/me/calendar/subscriptions/{accountId}`

**Path Parameters:**
- `accountId` (Guid, required) - The target account to subscribe to

**Response:** `201 Created`
```json
{
  "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "subscriber_id": "550e8400-e29b-41d4-a716-446655440000",
  "target_account_id": "6ba7b810-9dad-11d1-80b4-00c04fd430c8",
  "notify": true,
  "created_at": "2026-06-04T12:00:00Z",
  "updated_at": "2026-06-04T12:00:00Z"
}
```

**Errors:**
- `400 Bad Request` - Cannot subscribe to your own events

---

## Unsubscribe from Events

Remove a subscription to another user's events.

**Endpoint:** `DELETE /api/accounts/me/calendar/subscriptions/{accountId}`

**Path Parameters:**
- `accountId` (Guid, required) - The target account to unsubscribe from

**Response:** `204 No Content`

**Errors:**
- `404 Not Found` - Subscription does not exist

---

## List Subscribers

Get a list of account IDs subscribed to the current user's events.

**Endpoint:** `GET /api/accounts/me/calendar/subscriptions/subscribers`

**Response:**
```json
[
  "7c9e6679-7425-40de-944b-e07fc1f90ae7"
]
```

---

## Effect on Calendar and Countdown

Subscribed events automatically appear in the following endpoints:

- `GET /api/accounts/me/calendar` — Monthly calendar view
- `GET /api/accounts/me/calendar/merged` — Merged calendar view
- `GET /api/accounts/me/calendar/countdown` — Countdown view

Events from friends and subscribed accounts are deduplicated and sorted alongside the user's own events. The `account_id` field on each event item indicates the source user.

## Cache Behavior

- Calendar cache is invalidated for the subscriber when a subscription is created or deleted
- 24-hour cache TTL per account/month

# Multi-Tenant Notifications

Ring supports multiple apps (tenants) sharing a single notification service. Each app has its own FCM/APNS credentials and isolated notification data.

## App Identifier

Tenants are identified by a reverse-DNS app identifier (e.g. `dev.solsynth.solian`, `dev.solsynth.watt`). This identifier ties push credentials, subscriptions, and stored notifications together.

## How It Works

1. **Push credentials** are configured per-app in `appsettings.json` under `Notifications:Push:Apps`
2. **Subscriptions** store the `app_id` — the device knows which app it belongs to
3. **Delivery** resolves FCM/APNS senders from the subscription's `app_id`
4. **Listing/counting** is scoped by `?app=` query param — no cross-app data leaks

If `app_id` is null or omitted, the first configured app is used as fallback (backwards compatible with single-app setups).

## Configuration

```json
{
  "Notifications": {
    "Push": {
      "Apps": {
        "dev.solsynth.solian": {
          "Production": true,
          "FcmKeyPath": "./Keys/Solian.json",
          "Apns": {
            "PrivateKeyPath": "./Keys/Solian.p8",
            "PrivateKeyId": "4US4KSX4W6",
            "TeamId": "W7HPZ53V6B"
          }
        },
        "dev.solsynth.watt": {
          "Production": false,
          "FcmKeyPath": "./Keys/Watt.json",
          "Apns": {
            "PrivateKeyPath": "./Keys/Watt.p8",
            "PrivateKeyId": "ABC123",
            "TeamId": "TEAM01"
          }
        }
      }
    }
  }
}
```

The first app in the config is the default when no `app` / `app_id` is specified.

## API Changes

### Subscription Endpoints

#### Register Push Subscription

```
PUT /api/notifications/subscription
```

New field in request body:

| Field | Type | Description |
|-------|------|-------------|
| `app_id` | string? | App identifier (e.g. `dev.solsynth.solian`) |

```json
{
  "device_token": "fcm_token_here",
  "provider": 0,
  "app_id": "dev.solsynth.solian"
}
```

When `app_id` is provided, the subscription is tagged with it. All future push deliveries for this subscription use that app's credentials.

#### Register SOP Subscription

```
POST /api/notifications/sop/subscription
```

New field in request body:

```json
{
  "device_name": "My Phone",
  "app_id": "dev.solsynth.solian"
}
```

#### List Subscriptions

```
GET /api/notifications/subscription?app=dev.solsynth.solian
```

| Query Param | Type | Description |
|-------------|------|-------------|
| `app` | string? | Filter by app identifier. Omit for default app. |

### Notification Endpoints

All notification listing/counting endpoints accept `?app=`:

#### Count Unread

```
GET /api/notifications/count?app=dev.solsynth.solian
```

#### List Notifications

```
GET /api/notifications?app=dev.solsynth.solian&offset=0&take=8
```

#### Mark All Read

```
POST /api/notifications/all/read?app=dev.solsynth.solian
```

#### Send Notification

```
POST /api/notifications/send?app=dev.solsynth.solian
```

The `app` param is stored on saved notifications and used for delivery routing.

### SOP Endpoints

#### List (SOP token auth)

```
GET /api/notifications/sop?app=dev.solsynth.solian&token=<sop_token>
```

#### Stream (SOP token auth)

```
GET /api/notifications/sop/stream?token=<sop_token>
```

Stream is app-agnostic — it delivers all notifications for the account regardless of app.

## gRPC Changes

`DyPushNotification` now has an optional `app_id` field:

```protobuf
message DyPushNotification {
    // ...existing fields...
    optional string app_id = 9;
}
```

Used by `SendPushNotificationToUser` and `SendPushNotificationToUsers`. When set, the notification is saved with that `app_id` and delivery resolves credentials from it.

## Data Model Changes

### SnNotification

```diff
+ [MaxLength(1024)] public string? AppId { get; set; }
```

### SnNotificationPushSubscription

```diff
+ [MaxLength(1024)] public string? AppId { get; set; }
```

Both columns are nullable (`app_id varchar(1024)`) in the database.

## Migration

```bash
dotnet ef migrations add AddAppIdToNotifications
```

Adds `app_id` to `notifications` and `push_subscriptions` tables. Existing rows have `app_id = NULL` and fall back to the default app.

## Backwards Compatibility

- Old `appsettings.json` with flat `Google`/`Apple` keys still works — mapped to a `_default` internal app
- Omitting `app_id` on subscriptions or `?app=` on queries falls back to the first configured app
- Existing subscriptions with `app_id = NULL` continue to work with the default app's credentials

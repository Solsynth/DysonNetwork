# Admin Stats API

Each data-owning service exposes a lightweight global summary at:

```text
GET /api/admin/stats
```

The production gateway prefixes the route with the owning service:

| Service | Production route | Permission |
| --- | --- | --- |
| Padlock | `/padlock/admin/stats/users/geography` | `accounts.view` |
| Passport | `/passport/admin/stats` | `accounts.view` |
| Sphere | `/sphere/admin/stats` | `posts.moderate` |
| Wallet | `/wallet/admin/stats` | `wallets.transactions.manage` |
| Ring | `/ring/admin/stats` | `notifications.send` |

All fields are serialized as `snake_case`. Count fields are 64-bit integers and each response has a UTC `calculated_at` timestamp.

## Padlock account geography stats

`GET /api/admin/stats/users/geography` returns an aggregate map distribution derived from each account's latest GeoIP-bearing auth session in the selected window. It never returns account identifiers, IP addresses, session identifiers, or unrounded source coordinates.

Query parameters:

- `since`: optional UTC timestamp; defaults to the previous 30 days and cannot be in the future.
- `precision`: `country` (default) or `city`.

Only buckets with at least 10 accounts are returned. Coordinates are the average GeoIP point in the bucket, rounded to one decimal degree. The response includes `accounts_with_location`, `visible_account_count`, and `suppressed_account_count` so dashboards can show coverage without revealing suppressed buckets.

Example:

```json
{
  "calculated_at": "2026-07-13T12:00:00Z",
  "since": "2026-06-13T12:00:00Z",
  "precision": "country",
  "minimum_bucket_size": 10,
  "accounts_with_location": 1420,
  "visible_account_count": 1392,
  "suppressed_account_count": 28,
  "buckets": [
    {
      "country_code": "TW",
      "country": "Taiwan",
      "city": null,
      "latitude": 23.7,
      "longitude": 121.0,
      "user_count": 380
    }
  ]
}
```

## Passport account stats

Includes `total_profiled_accounts`, active-user windows (`active_users_last_day`, `active_users_last_week`, `active_users_last_month`), and registration windows (`registered_users_last_day`, `registered_users_last_week`, `registered_users_last_month`). The 1, 7, and 30 day windows are rolling backwards from `calculated_at`.

Accounts and activity are measured from Passport profiles. An account without a profile is excluded.

## Sphere post stats

Includes total, published, and draft post counts; post creation windows; publisher count; reaction count; and bookmark count.

## Wallet stats

Includes wallet, transaction, order, and subscription totals; confirmed and pending transaction counts; paid order count; and rolling transaction creation windows.

## Ring notification stats

Includes stored recipient-notification counts, unread count, rolling notification creation windows, total and activated push subscriptions, and all-time notification send-request and delivery-attempt record totals.

For outcome, provider, source, or topic breakdowns over a selected period, use Ring's existing `GET /api/admin/delivery-observability/notifications` endpoint.

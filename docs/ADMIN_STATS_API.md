# Admin Stats API

Each data-owning service exposes a lightweight global summary at:

```text
GET /api/admin/stats
```

The production gateway prefixes the route with the owning service:

| Service | Production route | Permission |
| --- | --- | --- |
| Passport | `/passport/admin/stats` | `accounts.view` |
| Sphere | `/sphere/admin/stats` | `posts.moderate` |
| Wallet | `/wallet/admin/stats` | `wallets.transactions.manage` |
| Ring | `/ring/admin/stats` | `notifications.send` |

All fields are serialized as `snake_case`. Count fields are 64-bit integers and each response has a UTC `calculated_at` timestamp.

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

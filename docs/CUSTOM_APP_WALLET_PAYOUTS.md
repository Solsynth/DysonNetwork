# Custom App Wallet Payouts

This document describes how custom apps integrate with `DysonNetwork.Wallet` for:

- app-created payment orders
- configurable app payout wallets
- app-level incoming metrics
- app-secret-authenticated payouts to other users

The feature spans `DysonNetwork.Develop` and `DysonNetwork.Wallet`.

## Overview

Each custom app can optionally configure a payout wallet through the Develop service.

When the app creates a wallet order using its app credentials:

- if a `payee_wallet_id` is explicitly provided, that wallet is used
- otherwise, if the app has a configured `payment_wallet_id`, that wallet is used
- otherwise, the order remains system-held and the payment does not go to a developer wallet

Orders created by an app are attributed using the app resource identifier instead of the app slug.

Example:

```text
developer.app:6c6c8d1d-4d89-4c46-a22c-7f62f7b3a6c1
```

This value is stored in `SnWalletOrder.AppIdentifier`.

## Data Model

`SnCustomApp` now includes:

```csharp
public Guid? PaymentWalletId { get; set; }
```

Database column in `custom_apps`:

```text
payment_wallet_id uuid null
```

Proto field in `DyCustomApp`:

```proto
string payment_wallet_id = 14;
```

## Develop API

Custom app create and update requests now accept `payment_wallet_id`.

Route:

```text
/api/developers/{pubName}/projects/{projectId}/apps
```

### Create app

**POST** `/api/developers/{pubName}/projects/{projectId}/apps`

Request example:

```json
{
  "slug": "mini-pay",
  "name": "Mini Pay",
  "description": "Wallet-enabled app",
  "payment_wallet_id": "6f7413d0-44d8-4fd3-84b5-d2603fb0e5a2",
  "status": 3,
  "links": {
    "home_page": "https://example.com"
  },
  "oauth_config": null
}
```

### Update app

**PATCH** `/api/developers/{pubName}/projects/{projectId}/apps/{appId}`

Request example:

```json
{
  "payment_wallet_id": "6f7413d0-44d8-4fd3-84b5-d2603fb0e5a2"
}
```

Set `payment_wallet_id` to `null` to remove the configured payout wallet.

## Wallet Order Creation

Route:

```text
POST /api/orders
```

This route is app-authenticated with `client_id` and `client_secret`.

### Request fields

| Field | Type | Required | Notes |
| --- | --- | --- | --- |
| `currency` | string | Yes | Wallet currency, for example `points` |
| `amount` | decimal | Yes | Positive amount |
| `remarks` | string | No | Human-readable order remark |
| `product_identifier` | string | No | App-defined product id |
| `meta` | object | No | Arbitrary metadata |
| `duration_hours` | int | No | Defaults to `24` |
| `payee_wallet_id` | guid | No | Explicit wallet override |
| `client_id` | string | Yes | App slug |
| `client_secret` | string | Yes | AppConnect secret |

### Payee wallet resolution

The server resolves the order payee in this order:

1. explicit `payee_wallet_id` from the request
2. configured `payment_wallet_id` on the custom app
3. `null`, which leaves the order system-held

### App attribution

Orders created through this endpoint store:

- `AppIdentifier = client.ResourceIdentifier`

That means the stored value is stable even if the app slug changes later.

### Example request

```json
{
  "currency": "points",
  "amount": 15,
  "remarks": "Premium feature unlock",
  "product_identifier": "premium.unlock",
  "meta": {
    "feature": "advanced_filter"
  },
  "duration_hours": 24,
  "client_id": "mini-pay",
  "client_secret": "appconnect-secret"
}
```

## Order Status Updates

Route:

```text
PATCH /api/orders/{id}
```

The app credential check resolves the app, then validates ownership by comparing:

- `order.AppIdentifier`
- `client.ResourceIdentifier`

This avoids using the slug as the long-term ownership key.

## App Incoming Metrics

Route:

```text
POST /api/orders/metrics
```

This route is also app-authenticated with `client_id` and `client_secret`.

### Request fields

| Field | Type | Required | Notes |
| --- | --- | --- | --- |
| `client_id` | string | Yes | App slug |
| `client_secret` | string | Yes | AppConnect secret |
| `start_date` | datetime | No | Inclusive lower bound |
| `end_date` | datetime | No | Inclusive upper bound |

### Response fields

| Field | Type | Meaning |
| --- | --- | --- |
| `app_identifier` | string | Stored app resource identifier |
| `total_orders` | int | Total matched orders |
| `paid_orders` | int | Orders in `Paid` state |
| `unpaid_orders` | int | Orders in `Unpaid` state |
| `finished_orders` | int | Orders in `Finished` state |
| `cancelled_orders` | int | Orders in `Cancelled` state |
| `expired_orders` | int | Orders in `Expired` state |
| `total_incoming_amount` | decimal | Sum of all matched order amounts |
| `paid_incoming_amount` | decimal | Sum of `Paid` and `Finished` order amounts |
| `product_incoming_amounts` | map | Amount grouped by `product_identifier` |
| `product_order_counts` | map | Count grouped by `product_identifier` |

### Example request

```json
{
  "client_id": "mini-pay",
  "client_secret": "appconnect-secret",
  "start_date": "2026-05-01T00:00:00Z",
  "end_date": "2026-05-31T23:59:59Z"
}
```

## App-Authenticated Payouts

Custom apps can spend from their configured payout wallet to pay other users.

Route:

```text
POST /api/orders/payouts
```

This route is authenticated by app secret, not by end-user session.

### Preconditions

- the custom app credentials must be valid
- the custom app must have a configured `payment_wallet_id`
- the configured payout wallet must exist
- the payee account must have a wallet
- the payer and payee cannot be the same account

### Request fields

| Field | Type | Required | Notes |
| --- | --- | --- | --- |
| `client_id` | string | Yes | App slug |
| `client_secret` | string | Yes | AppConnect secret |
| `payee_account_id` | guid | Yes | Recipient account id |
| `currency` | string | Yes | Wallet currency |
| `amount` | decimal | Yes | Positive amount |
| `remarks` | string | No | Optional payout remark |

### Example request

```json
{
  "client_id": "mini-pay",
  "client_secret": "appconnect-secret",
  "payee_account_id": "74bcf00d-f38d-438e-bc0d-8a0f45ce3d39",
  "currency": "points",
  "amount": 25,
  "remarks": "Challenge reward"
}
```

### Behavior

The route uses the configured app payout wallet as the payer wallet and creates a wallet transaction to the recipient wallet.

Current implementation records these as `TransactionType.Transfer`.

## Authentication Notes

These wallet app routes use the custom app secret validation flow:

- `client_id` resolves the app by slug
- `client_secret` is validated using `DyCustomAppService.CheckCustomAppSecret`

This is intended for trusted server-to-server or backend app scenarios where the app can safely store an `AppConnect` secret.

For secret types, see:

```text
docs/CUSTOM_APP_SECRET_TYPES.md
```

## Current Limitations

1. App-authenticated payouts currently reuse `TransactionType.Transfer`
2. There is no separate dedicated transaction type for app payouts yet
3. If no payout wallet is configured and no explicit `payee_wallet_id` is supplied during order creation, funds remain system-held

## Suggested Follow-Up

Potential future improvements:

1. add a dedicated `TransactionType` for app payouts
2. add app payout history endpoints separate from order metrics
3. add app-level spending metrics in addition to incoming metrics
4. optionally add stricter permission rules per app secret

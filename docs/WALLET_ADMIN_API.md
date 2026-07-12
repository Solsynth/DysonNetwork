# Wallet Admin API

This document describes the admin-facing Wallet APIs in `DysonNetwork.Wallet`.

All routes below are local development routes. In production, the gateway rewrites them to:

- `/wallet/admin/payments/...`
- `/wallet/admin/subscriptions/...`
- `/wallet/admin/wallet-products/...`

All JSON fields are serialized as `snake_case`.

## Overview

The Wallet admin surface is split into three controllers:

- `DysonNetwork.Wallet/Payment/PaymentAdminController.cs`
- `DysonNetwork.Wallet/Payment/SubscriptionAdminController.cs`
- `DysonNetwork.Wallet/Payment/WalletProductAdminController.cs`

Use them for:

- browsing payment transactions and orders globally
- adjusting user wallet balances
- browsing and maintaining subscription records and catalog entries
- running subscription maintenance jobs
- inspecting configured wallet-product mappings and manually applying paid wallet-product orders

## Permissions

| Permission | Purpose |
| --- | --- |
| `wallets.transactions.manage` | View admin transaction lists and individual transactions |
| `orders.view` | View admin order lists, individual orders, and wallet-product config |
| `orders.pay` | Apply an already-paid wallet-product order |
| `wallets.balance.modify` | Add or remove wallet balance |
| `subscriptions.order.manage` | View all subscriptions and run subscription maintenance jobs |
| `subscriptions.groups.manage` | View, create, update, and delete subscription catalog definitions |

## Payment Admin Routes

Base route:

```text
/api/admin/payments
```

Wallet-wide totals are available from `GET /api/admin/stats`; see [Admin Stats API](./ADMIN_STATS_API.md).

### GET /api/admin/payments/transactions

Returns wallet transactions across the system.

Required permission:

- `wallets.transactions.manage`

Query parameters:

- `wallet_id` optional wallet filter
- `account_id` optional account-owned wallet filter
- `status` optional `TransactionStatus`
- `type` optional `TransactionType`
- `currency` optional currency filter
- `offset` default `0`
- `take` default `50`, max `200`

Response headers:

- `X-Total` total matching transaction count

Notes:

- `account_id` expands to all wallets owned by that account
- returned transactions include `payer_wallet` and `payee_wallet`
- account-owned wallets are hydrated with account data when available

### GET /api/admin/payments/transactions/{id}

Returns a single transaction by id.

Required permission:

- `wallets.transactions.manage`

### GET /api/admin/payments/orders

Returns wallet orders across the system.

Required permission:

- `orders.view`

Query parameters:

- `wallet_id` optional wallet filter
- `account_id` optional account-owned wallet filter
- `status` optional `OrderStatus`
- `app_identifier` optional app filter
- `product_identifier` optional product filter
- `currency` optional currency filter
- `offset` default `0`
- `take` default `50`, max `200`

Response headers:

- `X-Total` total matching order count

Notes:

- wallet filtering matches:
  - direct `payee_wallet_id`
  - payer wallet on the attached payment transaction
  - payee wallet on the attached payment transaction
- responses include `transaction`, `items`, and `payee_wallet`

### GET /api/admin/payments/orders/{id}

Returns a single order by id.

Required permission:

- `orders.view`

### POST /api/admin/payments/balance

Adds or removes balance from a wallet.

Required permission:

- `wallets.balance.modify`

Request body:

```json
{
  "account_id": "550e8400-e29b-41d4-a716-446655440000",
  "currency": "golds",
  "amount": 120,
  "remark": "Manual balance correction",
  "force_operation": false
}
```

Supported targeting:

- `account_id`
- `wallet_id`

Behavior:

- positive `amount` credits the target wallet from system balance
- negative `amount` debits the target wallet to system balance
- `force_operation=true` bypasses the normal insufficient-funds guard on debits

Common errors:

- `400` when neither `account_id` nor `wallet_id` is provided
- `400` when the amount or transaction arguments are invalid
- `404` when the target wallet cannot be resolved

## Subscription Admin Routes

Base route:

```text
/api/admin/subscriptions
```

### GET /api/admin/subscriptions

Returns subscriptions across all accounts.

Required permission:

- `subscriptions.order.manage`

Query parameters:

- `account_id` optional account filter
- `identifier` optional subscription identifier
- `status` optional `SubscriptionStatus`
- `is_active` optional boolean filter
- `is_testing` optional boolean filter
- `offset` default `0`
- `take` default `50`, max `200`

Response headers:

- `X-Total` total matching subscription count

Notes:

- responses include the linked `coupon` when present

### GET /api/admin/subscriptions/catalog

Lists all subscription catalog definitions.

Required permission:

- `subscriptions.groups.manage`

### GET /api/admin/subscriptions/catalog/{identifier}

Returns one subscription catalog definition.

Required permission:

- `subscriptions.groups.manage`

### POST /api/admin/subscriptions/catalog

Creates or updates a subscription catalog definition.

Required permission:

- `subscriptions.groups.manage`

Request body fields:

- `identifier`
- `group_identifier`
- `display_name`
- `currency`
- `base_price`
- `perk_level`
- `minimum_account_level`
- `experience_multiplier`
- `golden_point_reward`
- `display_config`
- `payment_policy`
- `gift_policy`
- `provider_mappings`
- `app_identifier`

Behavior:

- creates a new definition when `identifier` does not exist
- updates the existing definition when `identifier` already exists
- returns `201 Created` for create and `200 OK` for update

### DELETE /api/admin/subscriptions/catalog/{identifier}

Deletes a subscription catalog definition.

Required permission:

- `subscriptions.groups.manage`

Response:

- `204 No Content` on success
- `404 Not Found` when the definition does not exist

### POST /api/admin/subscriptions/maintenance/update-expired

Runs the expired-subscription sweep.

Required permission:

- `subscriptions.order.manage`

Optional request body:

```json
{
  "batch_size": 100
}
```

Response:

```json
{
  "affected_count": 12
}
```

### POST /api/admin/subscriptions/maintenance/activate-pending

Activates queued subscriptions whose `begun_at` has arrived.

Required permission:

- `subscriptions.order.manage`

Optional request body:

```json
{
  "batch_size": 100
}
```

Response:

```json
{
  "affected_count": 5
}
```

### POST /api/admin/subscriptions/maintenance/cancel-unavailable-in-app-wallet

Cancels legacy in-app-wallet subscriptions that are no longer allowed by the current catalog payment policy.

Required permission:

- `subscriptions.order.manage`

Response:

```json
{
  "affected_count": 2
}
```

## Wallet Product Admin Routes

Base route:

```text
/api/admin/wallet-products
```

### GET /api/admin/wallet-products/golds-resupply-pack

Returns the current `Golds Resupply Pack` configuration from `Payment:Product:GoldCurrency`.

Required permission:

- `orders.view`

Response fields:

- `key`
- `identifier`
- `display_name`
- `currency`
- `provider_mappings`

This endpoint is read-only. It reflects configuration, not a database row.

### POST /api/admin/wallet-products/orders/{order_id}/apply

Manually applies an already-paid wallet-product order.

Required permission:

- `orders.pay`

Behavior:

- validates that the target order is a supported wallet-product order
- creates the deposit transaction if it has not already been created
- marks the order as finished
- sends the normal wallet-product notification flow

Common errors:

- `400` when the order is not paid
- `400` when the order is not a supported wallet-product order
- `400` when the order is missing its payee wallet

## Related Docs

- `docs/ORDER_API.md`
- `docs/WALLET_PROVIDER_PURCHASE_APIS.md`
- `docs/MERCHANT_SETTLEMENT.md`
- `docs/SUBSCRIPTION_QUEUE_AND_SWITCHING.md`

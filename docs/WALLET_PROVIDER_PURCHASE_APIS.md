# Wallet Provider Purchase APIs

This document describes the current provider-facing purchase APIs in `DysonNetwork.Wallet` after the webhook and restore flows were merged.

It covers two purchase categories:

- subscription purchases
- one-time `GoldCurrency` purchases, currently used for `Golds Resupply Pack`

## Overview

Provider purchase handling is split into two layers:

- provider checkout routes
- provider callback / recovery routes

Checkout routes are still separated by product type:

- subscriptions use `/api/subscriptions/.../checkout/...`
- gold currency uses `/api/wallet-products/golds-resupply-pack/checkout/...`

But callback and recovery routes are now unified under the subscription controller:

- webhooks use `/api/subscriptions/order/handle/...`
- restore APIs use `/api/subscriptions/order/restore/...`

Those unified routes inspect the provider payload and then dispatch internally to:

- subscription application, or
- wallet product application

## Canonical Routes

### Unified provider webhook routes

These are the only webhook routes that external payment handlers should call.

- `POST /api/subscriptions/order/handle/afdian`
- `POST /api/subscriptions/order/handle/paddle`
- `POST /api/subscriptions/order/handle/apple`

Behavior:

- fetches or parses the provider order payload
- determines whether the payload is a subscription purchase or a `GoldCurrency` purchase
- applies the matching internal flow

### Unified provider restore routes

These are the only restore / manual recovery routes that clients should call.

- `POST /api/subscriptions/order/restore/afdian`
- `POST /api/subscriptions/order/restore/paddle`
- `POST /api/subscriptions/order/restore/apple`

Behavior:

- validates the provider purchase using the platform payload
- determines whether the purchase is a subscription or a `GoldCurrency` purchase
- applies it through the same internal dispatch path used by webhook handling

## Request Formats

### Afdian restore

Endpoint:

```text
POST /api/subscriptions/order/restore/afdian
```

Body:

```json
{
  "order_id": "afdian_order_number"
}
```

### Paddle restore

Endpoint:

```text
POST /api/subscriptions/order/restore/paddle
```

Body:

```json
{
  "order_id": "txn_xxx"
}
```

### Apple restore

Apple restore does not use an order number lookup. It requires the signed transaction payload.

Endpoint:

```text
POST /api/subscriptions/order/restore/apple
```

Body:

```json
{
  "signed_transaction_info": "eyJ..."
}
```

Notes:

- the current authenticated user must match the Apple `appAccountToken`
- if the token does not match, the API returns `400`

## Response Shape

The restore APIs return one of two resource types depending on the provider payload:

- `SnWalletSubscription` for subscription purchases
- `SnWalletOrder` for `GoldCurrency` purchases

This is intentional. The restore route is unified by provider, not by response schema.

## GoldCurrency purchase flow

The `GoldCurrency` product is configured under:

```json
"Payment": {
  "Product": {
    "GoldCurrency": {
      "Identifier": "wallet.golds_resupply_pack",
      "DisplayName": "Golds Resupply Pack",
      "Currency": "golds",
      "ProviderMappings": {
        "Afdian": {
          "sku_golds_resupply_pack": 120
        },
        "AppleStore": {
          "golds.resupply.pack": 120
        },
        "Paddle": {
          "pri_golds_resupply_pack": 120
        }
      }
    }
  }
}
```

During restore or webhook handling:

1. provider payload is matched against `Payment:Product:GoldCurrency:ProviderMappings`
2. if matched, Wallet creates or reuses an internal order
3. Wallet applies the order by depositing `golds` into the user wallet
4. Wallet sends a notification to the user with the deposited amount and current balance

## GoldCurrency checkout routes

The gold currency product still has dedicated checkout routes:

- `POST /api/wallet-products/golds-resupply-pack/checkout/afdian`
- `POST /api/wallet-products/golds-resupply-pack/checkout/paddle`

Both routes require auth and `orders.create`.

### Afdian checkout response

```json
{
  "checkout_url": "https://ifdian.net/order/create?...",
  "provider_reference_id": "sku_golds_resupply_pack",
  "plan_id": "04fd1386206f11f184ec52540025c377",
  "gold_amount": 120
}
```

### Paddle checkout response

```json
{
  "transaction_id": "txn_xxx",
  "checkout_url": "https://checkout.paddle.com/...",
  "provider_reference_id": "pri_golds_resupply_pack",
  "gold_amount": 120
}
```

## Subscription checkout routes

Subscription checkout remains separate from the wallet product checkout:

- `POST /api/subscriptions/{identifier}/checkout/afdian`
- `POST /api/subscriptions/{identifier}/checkout/paddle`

These routes resolve provider references from the subscription catalog and create provider checkout sessions for subscription purchases.

## Internal dispatch rules

Unified webhook and restore routes use the same internal rule:

- if the provider payload matches the configured `GoldCurrency` provider mapping, treat it as a wallet product purchase
- otherwise, treat it as a subscription purchase

This keeps external payment handler integration simple:

- one webhook URL per provider
- one restore URL per provider

## Route ownership summary

Use these routes:

- webhook callbacks: `/api/subscriptions/order/handle/{provider}`
- purchase recovery: `/api/subscriptions/order/restore/{provider}`
- gold currency checkout: `/api/wallet-products/golds-resupply-pack/checkout/{provider}`
- subscription checkout: `/api/subscriptions/{identifier}/checkout/{provider}`

Do not use old wallet-product restore routes. Those were merged into the subscription restore routes.

# Order API Reference

All order operations. Custom apps authenticate with an `ApiKey` secret (client_id + client_secret).
Users authenticate with a Bearer token.

---

## Order Statuses

| Status | Meaning |
|--------|---------|
| `Unpaid` | Created, awaiting payment |
| `Paid` | Payment received |
| `Finished` | App marked as delivered |
| `Cancelled` | Cancelled by app or user |
| `Expired` | Duration exceeded without payment |

---

## Create Order

```
POST /api/orders
```

Creates an unpaid order. Supports two modes: line items (with product validation)
or legacy (direct amount + currency).

**Auth:** ApiKey (`client_id` + `client_secret` in body).

### With items (recommended)

```json
{
  "client_id": "my-app",
  "client_secret": "<api_key_secret>",
  "duration_hours": 24,
  "payee_wallet_id": "00000000-0000-0000-0000-000000000000",
  "remarks": "Purchase via in-game shop",
  "items": [
    { "product_identifier": "premium_boost", "quantity": 2 },
    { "product_identifier": "sticker_pack", "quantity": 1 }
  ]
}
```

**Response `200`:**
```json
{
  "id": "a1b2c3d4-...",
  "status": "unpaid",
  "currency": "golds",
  "amount": 1500,
  "app_identifier": "developer.app:{guid}",
  "remarks": "Purchase via in-game shop",
  "items": [
    { "product_identifier": "premium_boost", "quantity": 2, "unit_price": 500, "currency": "golds" },
    { "product_identifier": "sticker_pack", "quantity": 1, "unit_price": 500, "currency": "golds" }
  ],
  "expired_at": "2026-06-29T12:00:00Z",
  "payee_wallet_id": "00000000-...",
  "meta": null
}
```

Validation:
- Each `product_identifier` must exist in the app's product catalog
- All items must use the same currency
- `amount` = Σ(product.price × item.quantity)
- For subscription products (`recurrence != none`), meta carries subscription parameters
  and a `SnWalletSubscription` is created after payment

**Errors:**
- `400` — invalid credentials, product not found, currency mismatch
- `400` — payee wallet not found (falls back to app's configured `payment_wallet_id`)

### Legacy (no items)

```json
{
  "client_id": "my-app",
  "client_secret": "<api_key_secret>",
  "currency": "points",
  "amount": 100,
  "duration_hours": 24,
  "product_identifier": "ads.sponsor",
  "remarks": "Sponsorship"
}
```

`currency` + `amount` are required when `items` is absent. `product_identifier` is a freeform tag.

---

## Get Order

```
GET /api/orders/{id}
```

**Auth:** none.

```json
{
  "id": "a1b2c3d4-...",
  "status": "paid",
  "currency": "golds",
  "amount": 1500,
  "app_identifier": "developer.app:{guid}",
  "items": [
    { "product_identifier": "premium_boost", "quantity": 2, "unit_price": 500, "currency": "golds" }
  ],
  "transaction_id": "txn-...",
  "expired_at": "2026-06-29T12:00:00Z",
  "payee_wallet_id": "00000000-...",
  "created_at": "2026-06-28T12:00:00Z",
  "updated_at": "2026-06-28T12:05:00Z"
}
```

**Errors:** `404` — order not found.

---

## Pay Order

```
POST /api/orders/{id}/pay
```

User pays the order from their wallet. After payment, subscription records are
created automatically for recurring products.

**Auth:** Bearer (user token).

```json
{
  "pin_code": "1234",
  "payer_wallet_id": "00000000-0000-0000-0000-000000000000"   // optional, defaults to user's primary
}
```

- `pin_code` — optional. If the user has a pin set, it must match.
- `payer_wallet_id` — optional. Which wallet to pay from. Defaults to user's primary wallet.

**Response `200`:**
```json
{
  "id": "a1b2c3d4-...",
  "status": "paid",
  "currency": "golds",
  "amount": 1500,
  "transaction_id": "txn-...",
  "transaction": { "id": "txn-...", "type": "order", "amount": "1500", "currency": "golds", ... },
  "items": [ ... ],
  ...
}
```

**Errors:**
- `400` — wallet not found, insufficient funds, order already paid/expired/cancelled
- `401` — invalid or missing Bearer token

---

## Update Order Status

```
PATCH /api/orders/{id}/status
```

App marks the order as finished (goods delivered) or cancelled.

**Auth:** ApiKey.

```json
{
  "client_id": "my-app",
  "client_secret": "<api_key_secret>",
  "status": "Finished"
}
```

- `status` — `Finished` or `Cancelled` only.
- Order must belong to the app (app_identifier match).

**Response `200`:**
```json
{
  "id": "a1b2c3d4-...",
  "status": "finished",
  ...
}
```

**Errors:**
- `400` — invalid credentials, order doesn't belong to this app, invalid status
- `404` — order not found

---

## Order Metrics

```
POST /api/orders/metrics
```

Aggregate stats for an app's orders.

**Auth:** ApiKey.

```json
{
  "client_id": "my-app",
  "client_secret": "<api_key_secret>",
  "start_date": "2026-06-01T00:00:00Z",   // optional
  "end_date": "2026-07-01T00:00:00Z"       // optional
}
```

**Response `200`:**
```json
{
  "app_identifier": "developer.app:{guid}",
  "total_orders": 42,
  "paid_orders": 35,
  "unpaid_orders": 3,
  "finished_orders": 30,
  "cancelled_orders": 4,
  "expired_orders": 3,
  "total_incoming_amount": 52500,
  "paid_incoming_amount": 45000,
  "product_incoming_amounts": { "premium_boost": 30000, "sticker_pack": 15000 },
  "product_order_counts": { "premium_boost": 20, "sticker_pack": 15 }
}
```

Date filters are optional. Product breakdowns use the `product_identifier` from
order items (line-item orders) or the legacy `product_identifier` field.

---

## App Payout

```
POST /api/orders/payouts
```

Transfer funds from the app's configured payout wallet to a user account.

**Auth:** ApiKey. App must have a `payment_wallet_id` configured.

```json
{
  "client_id": "my-app",
  "client_secret": "<api_key_secret>",
  "payee_account_id": "00000000-0000-0000-0000-000000000000",
  "currency": "golds",
  "amount": 1000,
  "remarks": "Weekly creator payout"
}
```

**Response `200`:**
```json
{
  "id": "txn-...",
  "payer_wallet_id": "app-wallet-...",
  "payee_wallet_id": "user-wallet-...",
  "currency": "golds",
  "amount": "1000",
  "type": "transfer",
  "remarks": "Weekly creator payout",
  "status": "confirmed",
  ...
}
```

**Errors:**
- `400` — no payout wallet configured, wallet not found, cannot pay self

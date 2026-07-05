# Order API Reference

All order operations.
Custom apps authenticate with an `ApiKey` secret (`client_id` + `client_secret`).
Users authenticate with a Bearer token.

Related docs:
- `docs/APP_PRODUCTS.md`
- `docs/CUSTOM_APP_WALLET_PAYOUTS.md`

---

## Order statuses

| Status | Meaning |
|---|---|
| `Unpaid` | Created, awaiting payment |
| `Paid` | Payment received |
| `Finished` | App marked as delivered |
| `Cancelled` | Cancelled by app or user |
| `Expired` | Duration exceeded without payment |

---

## Create order

```http
POST /api/orders
```

Creates an unpaid order.
Supports two modes:
- item-based orders using app products
- legacy direct amount orders

Auth: `ApiKey` in the request body.

### Create order with items

```json
{
  "client_id": "shop-app",
  "client_secret": "<api_key_secret>",
  "duration_hours": 24,
  "remarks": "Purchase via shop",
  "items": [
    { "product_identifier": "physical-poster", "quantity": 1 }
  ],
  "meta": {
    "address_contact_id": "00000000-0000-0000-0000-000000000001",
    "address_snapshot": "John Doe, 1 Main St, City, Country"
  }
}
```

Response `200`:

```json
{
  "id": "a1b2c3d4-...",
  "status": "unpaid",
  "currency": "golds",
  "amount": 250,
  "app_identifier": "developer.app:{guid}",
  "remarks": "Purchase via shop",
  "items": [
    {
      "product_identifier": "physical-poster",
      "quantity": 1,
      "unit_price": 250,
      "currency": "golds"
    }
  ],
  "expired_at": "2026-07-05T12:00:00Z",
  "meta": {
    "fulfillment": {
      "address_contact_id": "00000000-0000-0000-0000-000000000001",
      "address_snapshot": "John Doe, 1 Main St, City, Country",
      "requires_address": true
    }
  }
}
```

### Validation rules for item-based orders

- each `product_identifier` must exist in the app's catalog
- each item `quantity` must be greater than zero
- all items must use the same currency
- `amount` is calculated from product price snapshots
- disabled products cannot be ordered
- stock must be available for the product's stock mode/window
- if any selected product requires an address, `meta.address_contact_id` and `meta.address_snapshot` are required
- recurring products inject subscription metadata automatically

### Stock behavior

Stock is reserved on order creation.

Availability is derived from:
- the product's `state`
- currently active orders in the matching stock window

Orders with status `cancelled` or `expired` do not consume stock.
Expired unpaid orders are marked expired before new stock checks run.

### Fulfillment metadata

Wallet stores normalized fulfillment metadata under `meta.fulfillment`.

It keeps both:
- `address_contact_id`
- `address_snapshot`

That keeps historical orders readable even if the user later edits their saved address.

### Recurring products

If a product has `recurrence != none`:
- order metadata receives subscription parameters
- payment creates the wallet subscription record
- renewal continues through the existing Wallet subscription engine

### Errors

`400` for:
- invalid credentials
- product not found
- quantity <= 0
- currency mismatch
- disabled product
- insufficient stock
- missing required address metadata
- payee wallet not found

---

## Create legacy order

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

Use this only when the app is not purchasing from the app product catalog.

---

## Get order

```http
GET /api/orders/{id}
```

Auth: none.

The endpoint also normalizes overdue unpaid orders to `expired` before returning the order.

---

## Pay order

```http
POST /api/orders/{id}/pay
Authorization: Bearer <user_token>
```

```json
{
  "pin_code": "1234",
  "payer_wallet_id": "00000000-0000-0000-0000-000000000000"
}
```

Rules:
- payer wallet must be accessible to the current user
- if the order carries required address fulfillment data, the selected contact must still belong to the current account
- the stored fulfillment snapshot is refreshed from the current contact before payment succeeds

Errors:
- `400` wallet not found, insufficient funds, order already paid/expired/cancelled, invalid fulfillment contact
- `401` invalid or missing Bearer token

---

## Update order status

```http
PATCH /api/orders/{id}/status
```

Auth: `ApiKey`.

```json
{
  "client_id": "my-app",
  "client_secret": "<api_key_secret>",
  "status": "Finished"
}
```

Allowed terminal updates:
- `Finished`
- `Cancelled`

---

## Order metrics

```http
POST /api/orders/metrics
```

Auth: `ApiKey`.

Returns app-level aggregate counts and amounts, including `expired_orders`.

---

## App payouts

```http
POST /api/orders/payouts
```

Auth: `ApiKey`.

Creates a payout from the merchant wallet associated with the app's publisher.

---

## Order item snapshot

`SnWalletOrderItem` freezes these values at order creation:

| Field | Snapshot? |
|---|---|
| `product_identifier` | yes |
| `quantity` | yes |
| `unit_price` | yes |
| `currency` | yes |

Display metadata is still resolved from the product catalog, not copied into order items.

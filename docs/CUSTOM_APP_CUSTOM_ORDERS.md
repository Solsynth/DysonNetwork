# Custom App Custom Orders

Developers can create a product-independent payment order for a custom app. This is useful for invoices, variable-price purchases, donations, and app-defined entitlements.

The order is associated with the app, but no app-product lookup, catalog-price lookup, stock reservation, or product fulfillment is performed.

## Create a custom order

```http
POST /api/private/apps/{app_id}/orders?dev={developer_slug}&proj={project_id}
Authorization: Bearer <developer_token>
```

The caller must be an editor of the developer and hold the `custom_apps.update` permission.

```json
{
  "identifier": "invoice:2026-07:acme",
  "currency": "points",
  "amount": 19.5,
  "expiration_hours": 24,
  "remarks": "July consulting invoice",
  "meta": {
    "invoice_id": "2026-07-acme",
    "seat_count": 5
  }
}
```

| Field | Required | Notes |
| --- | --- | --- |
| `identifier` | Yes | App-defined identifier, stored as the order `product_identifier`. It is not validated against the app product catalog. |
| `currency` | Yes | Wallet currency identifier. |
| `amount` | Yes | Must be at least `0.001`; Wallet stores amounts at three decimal places. |
| `expiration_hours` | No | Expiration duration from `1` to `720` hours. Defaults to `24`. |
| `remarks` | No | Human-readable order description. |
| `meta` | No | Arbitrary JSON metadata retained with the order. |

The response is the Wallet order. It has the following stable associations:

- `app_identifier` is the app resource identifier (`developer.app:{app_id}`)
- `product_identifier` is the supplied `identifier`
- `payee_wallet_id` is the merchant payout wallet associated with the app developer's publisher, when one exists

The endpoint creates a fresh order on every call; it does not reuse a matching unpaid order.

In production, the gateway exposes this Develop route as:

```text
/develop/private/apps/{app_id}/orders?dev={developer_slug}&proj={project_id}
```

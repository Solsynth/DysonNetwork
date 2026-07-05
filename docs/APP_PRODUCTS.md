# App Products

App products belong to a custom app and are managed from Develop.

They support:
- one-time purchases
- recurring subscription products
- required shipping/contact address for physical-style goods
- product selling state and stock limits
- order-time price snapshots through Wallet

Related docs:
- `docs/APP_PRODUCTS_AND_APIKEY.md`
- `docs/ORDER_API.md`
- `docs/AUTHORIZED_APPS.md`
- `docs/CUSTOM_APP_NOTIFICATIONS.md`

---

## Product model

`SnAppProduct` now has three parts:

### 1. Catalog fields

| Field | Type | Notes |
|---|---|---|
| `id` | uuid | Product id |
| `identifier` | string | SKU, unique per app |
| `display_name` | string | Human-readable name |
| `description` | string? | Optional |
| `currency` | string | e.g. `golds`, `points` |
| `price` | decimal | Unit price |
| `picture` | object? | Optional file reference |
| `background` | object? | Optional file reference |
| `recurrence` | string | `none`, `weekly`, `monthly`, `yearly` |
| `group_identifier` | string? | Subscription tier group |

### 2. Fulfillment config

`fulfillment`

| Field | Type | Notes |
|---|---|---|
| `is_address_required` | bool | Buyer must provide an address before paying |
| `required_scopes` | string[] | Extra app scopes expected by the client, usually permission-node names like `contacts.read` |

Notes:
- `is_address_required` is for product purchase validation.
- Broader merchant access to saved contacts is **not** implied by the product itself.
- The client should separately ask the user to authorize app scopes like `contacts.read`.

### 3. Mutable product state

`state`

| Field | Type | Notes |
|---|---|---|
| `is_enabled` | bool | If `false`, the product is not sellable |
| `stock_mode` | string | `unlimited`, `daily`, `weekly`, `monthly`, `yearly`, `manual` |
| `stock_quantity` | int? | Stock capacity for the active window |
| `last_restocked_at` | instant? | Used mainly for manual stock windows |
| `last_restocked_quantity` | int? | Last manual restock amount |

Stock is reserved on **order creation**.
Expired unpaid orders stop consuming stock.

---

## Developer endpoints

Base route:

- `/api/private/apps/{appId}/products?dev={developer_slug}&proj={project_id}`

Requires developer membership on the owning publisher.
- `viewer` for reads
- `editor` for create/update/delete

### List products

```http
GET /api/private/apps/{appId}/products?dev=acme&proj={project_id}
Authorization: Bearer <developer_token>
```

### Get product

```http
GET /api/private/apps/{appId}/products/{productId}?dev=acme&proj={project_id}
Authorization: Bearer <developer_token>
```

### Create product

```http
POST /api/private/apps/{appId}/products?dev=acme&proj={project_id}
Authorization: Bearer <developer_token>
Content-Type: application/json

{
  "identifier": "physical-poster",
  "display_name": "Physical Poster",
  "description": "Printed poster",
  "currency": "golds",
  "price": 250,
  "fulfillment": {
    "is_address_required": true,
    "required_scopes": ["contacts.read"]
  },
  "state": {
    "is_enabled": true,
    "stock_mode": "manual",
    "stock_quantity": 100
  }
}
```

### Update product

```http
PATCH /api/private/apps/{appId}/products/{productId}?dev=acme&proj={project_id}
Authorization: Bearer <developer_token>
Content-Type: application/json

{
  "price": 300,
  "state": {
    "stock_mode": "manual",
    "stock_quantity": 80
  }
}
```

For manual stock, updating `state.stock_quantity` acts as a restock point and updates `last_restocked_at` / `last_restocked_quantity`.

### Delete product

```http
DELETE /api/private/apps/{appId}/products/{productId}?dev=acme&proj={project_id}
Authorization: Bearer <developer_token>
```

---

## Public product lookup

These endpoints are public catalog reads:

```http
GET /api/apps/{slug}/products
GET /api/apps/{slug}/products/{identifier}
```

They return the current product payload, including fulfillment and state.

---

## Order behavior

Orders are created through Wallet.

```http
POST /api/orders
```

With product items:

```json
{
  "client_id": "shop-app",
  "client_secret": "<api_key>",
  "items": [
    { "product_identifier": "physical-poster", "quantity": 1 }
  ],
  "meta": {
    "address_contact_id": "00000000-0000-0000-0000-000000000001",
    "address_snapshot": "John Doe, 1 Main St, City, Country"
  }
}
```

### Validation rules

On order creation:
- product must exist
- quantity must be greater than zero
- product must be enabled
- all items must use the same currency
- stock must be available in the current stock window
- if any selected product requires an address, `meta.address_contact_id` and `meta.address_snapshot` must be present

### Fulfillment metadata stored on the order

Wallet normalizes order fulfillment data into:

```json
{
  "fulfillment": {
    "address_contact_id": "00000000-0000-0000-0000-000000000001",
    "address_snapshot": "John Doe, 1 Main St, City, Country",
    "requires_address": true
  }
}
```

The stored payload intentionally keeps both:
- a contact id reference
- a copied address snapshot

That keeps old orders readable even if the user later edits or deletes the saved address.

### Stock semantics

Stock is not decremented into a dedicated counter column.
Instead, availability is derived from:
- product `state`
- active orders in the current stock window

Active stock consumers are orders that are not:
- `cancelled`
- `expired`

Unpaid expired orders are marked expired before availability checks and before order reads.

### Recurring products

If `recurrence != none`, the product is also synced into Wallet's subscription catalog.

---

## Address sharing and app authorization

A required address for checkout and broader merchant contact access are separate concerns.

### Required address for purchase

Set:

```json
{
  "fulfillment": {
    "is_address_required": true
  }
}
```

This only means the order cannot be paid without address metadata.

### Broader merchant contact access

If the merchant app needs to read the user's saved contacts, the client should ask the user to grant app scopes explicitly.

Padlock endpoint:

```http
POST /api/authorized-apps/{appId}/scopes
Authorization: Bearer <user_token>
Content-Type: application/json

{
  "scopes": ["contacts.read"]
}
```

The requested scopes must already be allowed by the app's OAuth config.

---

## App-authorized contact access

After the user grants `contacts.read`, the app can call:

```http
GET /api/private/apps/{appId}/accounts/{accountId}/contacts?type=Address&verifiedOnly=false
X-Api-Key: <custom_app_api_key>
```

Rules:
- the API key must be valid for the app
- the user must have an active `SnAuthorizedApp` record for that app
- the authorized scopes must include `contacts.read`

This is intentionally separate from product management routes.

---

## Notifications

Notifications use the same authorization pattern, but require `notifications.send`.

```http
POST /api/private/apps/{appId}/notifications
X-Api-Key: <custom_app_api_key>
```

See `docs/CUSTOM_APP_NOTIFICATIONS.md`.

---

## API key note

Custom apps use secrets of type `ApiKey` for app-to-platform calls such as:
- creating orders
- reading authorized contacts
- sending notifications

Developer CRUD routes still use developer Bearer tokens, not app API keys.

---

## Migration

Develop migration added for the new product state table and fulfillment column:
- `20260705070703_AddAppProductState`

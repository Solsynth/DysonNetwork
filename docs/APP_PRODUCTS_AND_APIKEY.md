# App Products & Extended Orders

Custom apps can now define their own product catalog and create orders with
product line items. The old `AppConnect` secret type has been renamed to
`ApiKey` — a plain secret string, no HMAC challenge needed.

---

## App Products

Products belong to a custom app and are managed via the Develop API.

### Model

| Field | Type | Notes |
|-------|------|-------|
| `id` | uuid | Auto-generated |
| `identifier` | string (1024) | SKU, unique per app |
| `display_name` | string (1024) | Human-readable name |
| `description` | string? (4096) | Optional |
| `currency` | string (128) | e.g. `points`, `golds` |
| `price` | decimal | Unit price |
| `picture` | jsonb | `SnCloudFileReferenceObject`, optional |
| `background` | jsonb | `SnCloudFileReferenceObject`, optional |
| `app_id` | uuid | Owning app |

### Endpoints

All under `/api/developers/{pubName}/projects/{projectId}/apps/{appId}/products`.
Requires editor role on the developer publisher.

**List products**
```
GET /api/developers/{pubName}/projects/{projectId}/apps/{appId}/products
Authorization: Bearer <token>
```
→ `200`: `SnAppProduct[]`

**Get product**
```
GET /api/developers/{pubName}/projects/{projectId}/apps/{appId}/products/{productId}
Authorization: Bearer <token>
```
→ `200`: `SnAppProduct`

**Create product**
```
POST /api/developers/{pubName}/projects/{projectId}/apps/{appId}/products
Authorization: Bearer <token>
Content-Type: application/json

{
  "identifier": "premium_boost",
  "display_name": "Premium Boost",
  "description": "One-time account boost",
  "currency": "golds",
  "price": 500,
  "picture_id": "<file_id>",       // optional
  "background_id": "<file_id>"     // optional
}
```
→ `201`: `SnAppProduct`
→ `409`: Product with this identifier already exists

**Update product**
```
PATCH /api/developers/{pubName}/projects/{projectId}/apps/{appId}/products/{productId}
Authorization: Bearer <token>
Content-Type: application/json

{ "price": 600, "display_name": "Super Boost" }
```
→ `200`: `SnAppProduct`

**Delete product**
```
DELETE /api/developers/{pubName}/projects/{projectId}/apps/{appId}/products/{productId}
Authorization: Bearer <token>
```
→ `204`

### Public lookup

No auth required.

```
GET /api/apps/{slug}/products/{identifier}
```
→ `200`: `SnAppProduct`
→ `404`: App or product not found

---

## ApiKey Secrets (was AppConnect)

Secrets of type `ApiKey` are plain strings used as client credentials when
calling Wallet order endpoints. The old HMAC challenge-signature flow
(`POST /api/connect/{appId}/validate`) has been removed.

**Create an ApiKey secret**
```
POST /api/developers/{pubName}/projects/{projectId}/apps/{appId}/secrets
Authorization: Bearer <token>
Content-Type: application/json

{
  "description": "Order API key",
  "type": "ApiKey"
}
```
→ `201`: `SecretResponse` (the `secret` field is only returned once)

Use the returned `secret` value as `client_secret` when creating orders. The
`CheckCustomAppSecret` gRPC validates it directly — no challenge/signature
required.

---

## Orders with Products

`POST /api/orders` now accepts an optional `items` array. Each item references
a product by identifier. The Wallet validates each product exists for the app
via gRPC before creating the order. The order amount is auto-calculated from
line items.

### Create order with products (new)

```
POST /api/orders
Content-Type: application/json

{
  "client_id": "my-app",
  "client_secret": "<api_key_secret>",
  "duration_hours": 24,
  "payee_wallet_id": "00000000-0000-0000-0000-000000000000",
  "remarks": "Purchase of premium boost",
  "items": [
    {
      "product_identifier": "premium_boost",
      "quantity": 2
    }
  ]
}
```
→ `200`: `SnWalletOrder` with `items` populated and `amount` = sum of line totals

Validation:
- Each `product_identifier` must match a product on the app
- All products must use the same `currency`
- `amount` = Σ(product.price × item.quantity)

### Create order without products (legacy, unchanged)

```
POST /api/orders
Content-Type: application/json

{
  "client_id": "internal",
  "client_secret": "...",
  "currency": "points",
  "amount": 100,
  "duration_hours": 24,
  "product_identifier": "ads.sponsor",
  "remarks": "Sponsorship"
}
```
→ `200`: `SnWalletOrder` — no items, uses legacy `currency`/`amount` directly

### Pay order

Unchanged. User pays via their wallet:
```
POST /api/orders/{id}/pay
Authorization: Bearer <token>
Content-Type: application/json

{ "pin_code": "1234" }
```

### Metrics & payouts

`POST /api/orders/metrics` and `POST /api/orders/payouts` are unchanged. The
metrics endpoint groups by `product_identifier` — for item-based orders each
product SKU appears in results.

---

## gRPC

### `DyCustomAppService.GetAppProduct`

```
DyGetAppProductRequest {
  string app_id = 1;
  string identifier = 2;
}
→ DyGetAppProductResponse {
  DyAppProduct product = 1;
}
```

Returns the product if it belongs to the specified app. Used by Wallet for
validation during order creation.

### `DyCreateOrderRequest` / `DyOrder`

Both now carry `repeated DyOrderItem items`. Each `DyOrderItem` has:

```
string product_identifier = 1;
int32 quantity = 2;
string unit_price = 3;
string currency = 4;
```

---

## Migrations

| Project | Migration | Table |
|---------|-----------|-------|
| Develop | `AddAppProducts` | `app_products` |
| Wallet | `AddWalletOrderItems` | `wallet_order_items` |

Run `dotnet ef database update` in each project to apply.

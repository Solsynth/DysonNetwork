# App Products & Extended Orders

Custom apps can now define their own product catalog and create orders with
product line items. The old `AppConnect` secret type has been renamed to
`ApiKey` — a plain secret string, no HMAC challenge needed.

---

## Behavior Changes

### AppConnect → ApiKey

| Before | After |
|--------|-------|
| `CustomAppSecretType.AppConnect` | `CustomAppSecretType.ApiKey` |
| `AuthorizedAppType.AppConnect` | `AuthorizedAppType.ApiKey` |
| `POST /api/connect/{appId}/validate` | **Removed** |
| HMAC-SHA256 challenge-signature auth | Plain secret checked via `CheckCustomAppSecret` gRPC |
| `CustomAppService.ValidateAppConnectChallengeSignatureAsync()` | **Removed** |
| `CustomAppChallengeController` | **Removed** (entire file) |

Secrets of type `ApiKey` are now validated by a direct string comparison
against the stored secret hash (via the existing `CheckCustomAppSecret` gRPC).
No challenge, no signature, no base64url/hex decoding. Just pass the secret
as `client_secret` in order requests.

### Orders: line items vs legacy

| Before | After |
|--------|-------|
| `POST /api/orders` — single `currency` + `amount` + optional `product_identifier` string tag | Same fields still supported **(backward compat)** |
| — | `POST /api/orders` — new optional `items[]` array of `{ product_identifier, quantity }` |
| — | Amount auto-calculated from items: Σ(product.price × item.quantity) |
| — | Currency auto-derived from first item, must be uniform |
| — | Each item validated against app's product catalog via `GetAppProduct` gRPC |
| `SnWalletOrder.ProductIdentifier` (single string) | Still present on legacy orders; null on item-based orders |
| — | `SnWalletOrderItem` table stores per-line pricing snapshot |

Internal services (SponsorService, Passport, etc.) continue using the legacy
`currency` + `amount` + `product_identifier` path unchanged.

### Product identifier uniqueness

Product identifiers (SKUs) are unique **per app** — two apps can each have
a product named `"premium_boost"`. The uniqueness constraint is `(app_id, identifier)`.
When an external caller (like Wallet) references a product, it passes both
`app_id` and `identifier` to the `GetAppProduct` gRPC.

For globally-unique external references, build a runtime namespace:
`{project_slug}.{app_slug}.{sku}` — this is constructed at read time, not
stored in the database.

---

## Developer Workflow

End-to-end example: a developer creates an app, adds a product, and has a
user purchase it.

### 1. Create an ApiKey secret

```bash
POST /api/developers/my-studio/projects/{projectId}/apps/{appId}/secrets
Authorization: Bearer <dev_token>

{
  "description": "Order signing key",
  "type": "ApiKey"
}

# Response (201):
{
  "id": "sec_xxx",
  "secret": "dGhpcyBpcyBhIHNlY3JldC4uLg",  // ← shown once, save it
  "description": "Order signing key",
  "type": "ApiKey",
  "created_at": "2026-06-28T12:00:00Z",
  "updated_at": "2026-06-28T12:00:00Z"
}
```

### 2. Define products

```bash
POST /api/developers/my-studio/projects/{projectId}/apps/{appId}/products
Authorization: Bearer <dev_token>

{
  "identifier": "premium_boost",
  "display_name": "Premium Boost",
  "description": "One-time account experience boost",
  "currency": "golds",
  "price": 500
}

# Response (201):
{
  "id": "prod_xxx",
  "identifier": "premium_boost",
  "display_name": "Premium Boost",
  "description": "One-time account experience boost",
  "currency": "golds",
  "price": 500,
  "app_id": "{appId}",
  ...
}
```

### 3. Create an order with that product

```bash
POST /api/orders

{
  "client_id": "my-app",          // app slug
  "client_secret": "dGhpcyBpcyBhIHNlY3JldC4uLg",  // ApiKey from step 1
  "duration_hours": 24,
  "remarks": "Purchase via in-game shop",
  "items": [
    {
      "product_identifier": "premium_boost",
      "quantity": 2
    }
  ]
}

# Response (200):
{
  "id": "order_xxx",
  "status": "unpaid",
  "currency": "golds",
  "amount": 1000,           // 500 × 2
  "app_identifier": "developer.app:{appId}",
  "items": [
    {
      "product_identifier": "premium_boost",
      "quantity": 2,
      "unit_price": 500,
      "currency": "golds"
    }
  ],
  "expired_at": "2026-06-29T12:00:00Z",
  ...
}
```

### 4. User pays the order

```bash
POST /api/orders/{orderId}/pay
Authorization: Bearer <user_token>

{ "pin_code": "1234" }

# Response (200): status → "paid", transaction populated
```

### 5. Check order status (optional)

```bash
GET /api/orders/{orderId}
# status: "paid" | "finished" | "cancelled" | "expired"
```

### 6. App marks order as finished (after delivering goods)

```bash
PATCH /api/orders/{orderId}/status

{
  "client_id": "my-app",
  "client_secret": "dGhpcyBpcyBhIHNlY3JldC4uLg",
  "status": "Finished"
}
```

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

### Order item snapshot behavior

Each `SnWalletOrderItem` captures pricing at order time:

| Field | Source | Frozen? |
|-------|--------|---------|
| `product_identifier` | Product SKU | No — links back to catalog |
| `quantity` | Request | Yes |
| `unit_price` | `product.Price` at order creation | Yes — price changes won't affect paid orders |
| `currency` | `product.Currency` | Yes |

Product metadata (display name, description, picture, background) is **not**
stored on the order. To display it, resolve the product by identifier through
the app's product endpoints.

The order's `amount` = Σ(`unit_price` × `quantity`) across all items.
The order's `currency` must be uniform — cross-currency item orders are rejected.

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

# App Products, Subscriptions & Extended Orders

Custom apps can now define products (one-time + recurring subscriptions),
create orders with line items, and leverage the Wallet's full subscription
engine. The old `AppConnect` secret type has been renamed to `ApiKey`.

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

### Orders: line items vs legacy

| Before | After |
|--------|-------|
| `POST /api/orders` — single `currency` + `amount` + optional `product_identifier` | Same fields still supported **(backward compat)** |
| — | New optional `items[]` array of `{ product_identifier, quantity }` |
| — | Amount auto-calculated: Σ(product.price × item.quantity) |
| — | Currency auto-derived from first item, must be uniform |
| — | Each item validated via `GetAppProduct` gRPC |
| — | `SnWalletOrderItem` table stores per-line pricing snapshot |

### Subscriptions

| Before | After |
|--------|-------|
| Subscriptions only for platform Stellar Program | Custom apps can offer recurring products |
| Catalog seeded from `appsettings.json` only | Runtime registration via gRPC from Develop |
| `SnWalletSubscriptionDefinition` — platform-owned | `AppIdentifier` field distinguishes app subs |
| Renewal job: platform subs only | App subs auto-renew from user wallet too |

---

## Developer Workflow

### 1. Create an ApiKey secret

```bash
POST /api/developers/my-studio/projects/{projectId}/apps/{appId}/secrets
Authorization: Bearer <dev_token>

{
  "description": "Order signing key",
  "type": "ApiKey"
}
```
→ `201`: `SecretResponse` — save the `secret` field, shown only once.

### 2a. Define a one-time product

```bash
POST /api/developers/my-studio/projects/{projectId}/apps/{appId}/products
Authorization: Bearer <dev_token>

{
  "identifier": "premium_boost",
  "display_name": "Premium Boost",
  "description": "One-time experience boost",
  "currency": "golds",
  "price": 500
}
```
→ `201`: `SnAppProduct`

### 2b. Define a subscription product

```bash
POST /api/developers/my-studio/projects/{projectId}/apps/{appId}/products
Authorization: Bearer <dev_token>

{
  "identifier": "monthly_vip",
  "display_name": "Monthly VIP",
  "description": "Premium membership, renewed every 30 days",
  "currency": "golds",
  "price": 1200,
  "recurrence": "monthly",
  "group_identifier": "myapp.vip"      # optional, for tier groups
}
```
→ `201`: `SnAppProduct` (auto-registered as a subscription definition in Wallet)

### 3. Create an order

```bash
POST /api/orders

{
  "client_id": "my-app",
  "client_secret": "<api_key_secret>",
  "duration_hours": 24,
  "remarks": "Purchase via in-game shop",
  "items": [
    { "product_identifier": "monthly_vip", "quantity": 1 }
  ]
}
```
→ `200`: `SnWalletOrder` with `amount` = 1200, `items` populated

For subscription products, the order meta carries subscription info.
After payment, a `SnWalletSubscription` record is auto-created.

### 4. User pays the order

```bash
POST /api/orders/{orderId}/pay
Authorization: Bearer <user_token>

{ "pin_code": "1234" }
```
→ `200`: status → `paid`, transaction populated, subscription created (if recurring)

### 5. Check subscription state

```bash
GET /api/subscriptions/groups/myapp.vip
Authorization: Bearer <user_token>
```
→ `200`: group state with current active subscription, next tier, catalog

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
| `price` | decimal | Unit price (base price for subscriptions) |
| `picture` | jsonb | `SnCloudFileReferenceObject`, optional |
| `background` | jsonb | `SnCloudFileReferenceObject`, optional |
| `recurrence` | int | `0`=None, `1`=Weekly, `2`=Monthly, `3`=Yearly |
| `group_identifier` | string? (4096) | Subscription group for tier upgrades |
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
  "currency": "golds",
  "price": 500,
  "recurrence": "monthly",          // none | weekly | monthly | yearly
  "group_identifier": "myapp.vip",  // optional
  "picture_id": "<file_id>",
  "background_id": "<file_id>"
}
```
→ `201`: `SnAppProduct` (recurring products auto-sync to Wallet subscription catalog)
→ `409`: Product with this identifier already exists for this app

**Update product**
```
PATCH /api/developers/{pubName}/projects/{projectId}/apps/{appId}/products/{productId}
Authorization: Bearer <token>

{ "price": 600, "display_name": "Super Boost" }
```
→ `200`: `SnAppProduct` (catalog synced if recurrence fields change)

**Delete product**
```
DELETE /api/developers/{pubName}/projects/{projectId}/apps/{appId}/products/{productId}
Authorization: Bearer <token>
```
→ `204` (subscription definition removed from Wallet if recurring)

### Public lookup

```
GET /api/apps/{slug}/products/{identifier}
```
→ `200`: `SnAppProduct`
→ `404`: App or product not found

---

## Subscriptions

When a product has `recurrence != none`, it becomes a subscription product:

- A corresponding `SnWalletSubscriptionDefinition` is registered in Wallet's catalog
- Orders for that product tag meta with subscription parameters
- On payment, a `SnWalletSubscription` record is created for the user
- `SubscriptionRenewalJob` auto-renews from user wallet every cycle
- Subscription groups enable tier upgrades via existing endpoints

### Subscription definition sync

| Trigger | Action |
|---------|--------|
| Create product with recurrence | `RegisterAppSubscriptionDefinition` gRPC → Wallet upserts definition |
| Update product | Definition updated (price, name, cycle, group) |
| Delete product | `Remove=true` → definition removed from Wallet catalog |

The sync is best-effort; if Wallet is unreachable the product still saves.

### Subscription lifecycle

```
Order created → User pays → Subscription created (Active)
                                  ↓
                            RenewalAt reached → RenewalJob creates order
                                  ↓
                            Wallet charged → Subscription extended
                            Insufficient funds → Subscription expires
```

### Existing Wallet subscription endpoints

All work for app subscriptions:

| Endpoint | Description |
|----------|-------------|
| `GET /api/subscriptions` | List user's subscriptions (includes app subs) |
| `GET /api/subscriptions/{identifier}` | Get single subscription |
| `GET /api/subscriptions/groups/{group}` | Group state (current tier, next tier, catalog) |
| `POST /api/subscriptions/groups/{group}/activate` | Activate/upgrade tier within group |
| `GET /api/subscriptions/catalog` | Full catalog (platform + app definitions) |

---

## ApiKey Secrets (was AppConnect)

Secrets of type `ApiKey` are plain strings used as client credentials when
calling Wallet order endpoints.

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

---

## Orders

### Create order with products

```
POST /api/orders
Content-Type: application/json

{
  "client_id": "my-app",
  "client_secret": "<api_key_secret>",
  "duration_hours": 24,
  "payee_wallet_id": "00000000-0000-0000-0000-000000000000",
  "remarks": "Purchase",
  "items": [
    { "product_identifier": "premium_boost", "quantity": 2 }
  ]
}
```
→ `200`: `SnWalletOrder` with `amount` = Σ items

### Create order (legacy)

```
POST /api/orders

{ "client_id": "internal", "client_secret": "...", "currency": "points", "amount": 100 }
```

### Pay order

```
POST /api/orders/{id}/pay
Authorization: Bearer <token>
{ "pin_code": "1234" }
```

### Metrics & payouts

`POST /api/orders/metrics` and `POST /api/orders/payouts` are unchanged.

### Order item snapshot

| Field | Frozen at order time? |
|-------|----------------------|
| `product_identifier` | No — links back to catalog |
| `quantity` | Yes |
| `unit_price` | Yes — price changes won't affect paid orders |
| `currency` | Yes |

Product display metadata is NOT stored on order items — resolve via product endpoints.

---

## gRPC

### `DyCustomAppService.GetAppProduct`

```
DyGetAppProductRequest { string app_id; string identifier; }
→ DyGetAppProductResponse { DyAppProduct product; }
```

`DyAppProduct` now includes `recurrence` (string) and `group_identifier`.

### `DyPaymentService.RegisterAppSubscriptionDefinition`

```
DyRegisterAppSubscriptionDefinitionRequest {
  string identifier;           // product SKU
  string app_identifier;       // "developer.app:{id}"
  string display_name;
  string currency;
  string base_price;
  string group_identifier;     // optional
  int32 cycle_duration_days;   // 7, 30, 365
  bool remove;                 // true = delete definition
}
→ DyRegisterAppSubscriptionDefinitionResponse { bool created; }
```

Called by Develop when products with recurrence are created/updated/deleted.

### `DyCreateOrderRequest` / `DyOrder`

Both carry `repeated DyOrderItem items`:
```
string product_identifier; int32 quantity; string unit_price; string currency;
```

---

## Migrations

| Project | Migration | Table / Column |
|---------|-----------|----------------|
| Develop | `AddAppProducts` | `app_products` |
| Develop | `AddProductRecurrence` | `app_products.recurrence`, `app_products.group_identifier` |
| Wallet | `AddWalletOrderItems` | `wallet_order_items` |
| Wallet | `AddAppIdentifierToSubscriptionDef` | `wallet_subscription_definitions.app_identifier` |

Run `dotnet ef database update` in each project to apply.

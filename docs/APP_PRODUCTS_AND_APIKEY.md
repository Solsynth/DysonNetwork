# App Products, Orders, Subscriptions & ApiKey

This is the short index doc for custom-app commerce.

See:
- `docs/APP_PRODUCTS.md`
- `docs/ORDER_API.md`
- `docs/CUSTOM_APP_CUSTOM_ORDERS.md`
- `docs/CUSTOM_APP_NOTIFICATIONS.md`
- `docs/AUTHORIZED_APPS.md`
- `docs/CUSTOM_APP_WALLET_PAYOUTS.md`

---

## Summary

Custom apps can:
- define products in Develop
- sell one-time or recurring products
- require an address for physical-style goods
- limit selling by stock windows (`daily`, `weekly`, `monthly`, `yearly`, `manual`, `unlimited`)
- create Wallet orders with line items using an `ApiKey`
- ask users to authorize extra scopes like `contacts.read`
- send notifications to users who granted `notifications.send`

The old `AppConnect` naming has been replaced by `ApiKey`.

---

## Main routes

### Developer-managed product routes

```http
GET    /api/private/apps/{appId}/products?dev={developer_slug}&proj={project_id}
POST   /api/private/apps/{appId}/products?dev={developer_slug}&proj={project_id}
GET    /api/private/apps/{appId}/products/{productId}?dev={developer_slug}&proj={project_id}
PATCH  /api/private/apps/{appId}/products/{productId}?dev={developer_slug}&proj={project_id}
DELETE /api/private/apps/{appId}/products/{productId}?dev={developer_slug}&proj={project_id}
```

### Public product routes

```http
GET /api/apps/{slug}/products
GET /api/apps/{slug}/products/{identifier}
```

### Wallet order routes

```http
POST /api/orders
GET  /api/orders/{id}
POST /api/orders/{id}/pay
PATCH /api/orders/{id}/status
POST /api/orders/metrics
POST /api/orders/payouts
```

### User consent route

```http
POST /api/authorized-apps/{id}/scopes
Authorization: Bearer <user_token>
```

Path `{id}` is the authorized-app record id (not the custom app id).

### App-authorized routes

```http
GET  /api/private/apps/{appId}/accounts/{accountId}/contacts
POST /api/private/apps/{appId}/notifications
```

Both app-authorized routes use:

```http
X-Api-Key: <custom_app_api_key>
```

---

## Rules worth remembering

- Product CRUD uses **developer Bearer tokens**, not app API keys.
- Orders, authorized contact reading, and notifications use **app API keys**.
- Product-required address and broad contact sharing are separate.
- Required address is enforced on the order.
- Broad contact access is enforced by user-granted scopes such as `contacts.read`.
- Stock is reserved on order creation.
- Unpaid expired orders stop consuming stock.
- Recurring products auto-sync into Wallet subscription definitions.

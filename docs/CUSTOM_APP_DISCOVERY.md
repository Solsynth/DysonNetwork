# Custom App Discovery

This document describes the public discovery endpoint for active custom apps in Develop.

Related docs:
- `docs/APP_PRODUCTS.md`
- `docs/APP_PRODUCTS_AND_APIKEY.md`
- `docs/ACCOUNT_BOARD.md`

---

## Endpoint

Public catalog listing:

```http
GET /api/apps?take=20&offset=0&search=shop
```

The endpoint returns only active apps. In Develop, that means apps with `status == production`.

---

## Query parameters

| Field | Type | Notes |
|---|---|---|
| `take` | int | Page size. Default `20`. |
| `offset` | int | Zero-based offset. Default `0`. |
| `search` | string? | Optional search string matched against `slug`, `title`, and `description`. |

Search is case-insensitive and applies to all three fields.

---

## Response headers

| Header | Notes |
|---|---|
| `X-Total` | Total number of matching apps before pagination. |

---

## Response shape

The endpoint returns `CustomAppDiscoveryResponse[]`.

| Field | Type | Notes |
|---|---|---|
| `id` | uuid | Custom app id |
| `slug` | string | Public app slug |
| `title` | string | App title |
| `description` | string? | Optional description |
| `products_count` | int | Total products attached to the app |
| `widgets_count` | int | Total board widgets attached to the app |

---

## Example

```http
GET /api/apps?take=2&offset=0&search=shop
```

```http
X-Total: 1
```

```json
[
  {
    "id": "1b9d6d7f-4d92-4af6-b4ec-7b8d2f9e0c91",
    "slug": "shop",
    "title": "Shop",
    "description": "Marketplace app",
    "products_count": 12,
    "widgets_count": 3
  }
]
```

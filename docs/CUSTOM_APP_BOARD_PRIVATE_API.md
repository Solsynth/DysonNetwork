# Custom App Board — Private API

Secret-authenticated APIs for custom apps that provide account profile board widgets.

Related docs:

- `docs/ACCOUNT_BOARD.md` — full board model, user APIs, discovery, gRPC
- `docs/CUSTOM_APP_SECRET_TYPES.md` — secret types
- `docs/CUSTOM_APP_DISCOVERY.md` — public discovery

All JSON fields use `snake_case`.

**Gateway:** in production use the service prefix instead of `/api` (e.g. `/develop/...` instead of `/api/...`).

---

## Ownership model

| Concern | Owner | Who writes it |
|--------|--------|----------------|
| Board layout (order, which widgets, enabled) | Passport | User (`PUT /accounts/me/board`) |
| Custom-app widget **payload** | Develop → Passport | **This app only**, via private API |
| Widget **manifest** (schema) | Develop | Developer console APIs (user JWT + editor role) |

Rules:

1. Users place a custom-app widget with an **empty** payload.
2. Only this custom app’s private API (app secret) can set/update **that app’s** widget payloads.
3. The private API cannot update prebuilt widgets or another app’s board items.
4. Replacing the board layout **preserves** existing custom-app payloads and **ignores** any client-supplied payload for custom-app items.

---

## Authentication

All private board endpoints below require a valid **API secret** for the path `app_id` (non-OIDC secret).

```http
Authorization: Bearer <custom_app_secret>
```

or:

```http
X-App-Secret: <custom_app_secret>
```

Additionally, the app must declare the board capability scope in OAuth config:

```text
accounts.profile.board
```

(`CustomApp.oauth_config.allowed_scopes`)

---

## Endpoints

Base path (Develop):

```text
/api/private/apps/{app_id}
```

### List this app’s widget manifests

```http
GET /api/private/apps/{app_id}/board/widgets
```

Returns **only** board widget definitions belonging to this `app_id`. Other apps’ widgets, prebuilt widgets, and user board layouts are never included.

**Auth:** app secret (see above).

**Requirements:**

- App exists
- Secret valid for `app_id`
- App declares `accounts.profile.board`

**Response** `200` — array of widget manifests:

```json
[
    {
        "key": "summary_card",
        "is_enabled": true,
        "renderer_type": "hero",
        "payload_type": "object",
        "field_types": [
            {
                "name": "title",
                "type": "string",
                "label": "Title",
                "format": "",
                "required": true
            },
            {
                "name": "image",
                "type": "string",
                "label": "Image",
                "format": "",
                "required": true
            }
        ],
        "required_fields": ["title", "image"],
        "max_payload_bytes": 2048,
        "allow_multiple": false
    }
]
```

**Errors:**

| Status | When |
|--------|------|
| `401` | Missing/invalid secret |
| `403` | App does not declare `accounts.profile.board` |
| `404` | App not found |

---

### Push payload to a user’s board item

```http
POST /api/private/apps/{app_id}/board/payload
```

Updates payload for a board item that already uses **this app’s** widget. Validates against the widget manifest, then persists via Passport gRPC.

**Auth:** app secret.

**Requirements:**

- Secret valid for `app_id`
- App declares `accounts.profile.board`
- Target user has authorized this app with `accounts.profile.board` (Padlock `AuthorizedApp`)
- Widget key exists and is enabled for this app
- Board item already exists on the user’s board for this app + widget key
- Non-empty payload must satisfy `required_fields`, field types, size limit, and the universal envelope

**Request:**

```json
{
    "account_id": "550e8400-e29b-41d4-a716-446655440000",
    "widget_key": "summary_card",
    "board_item_id": null,
    "payload": {
        "title": {
            "value": "Updated from app backend",
            "label": "Title"
        },
        "image": {
            "value": "https://cdn.example/img.png",
            "label": "Image"
        }
    }
}
```

| Field | Required | Notes |
|-------|----------|--------|
| `account_id` | yes | Target user |
| `widget_key` | yes | Widget definition key on **this** app |
| `board_item_id` | no | Target instance id. Omit to update the first matching item for account + app + widget key. Required when `allow_multiple` and multiple instances exist |
| `payload` | yes | Envelope-shaped object; empty `{}` is allowed only as a clear/unconfigured state — non-empty payloads must include all `required_fields` |

**Universal field envelope** (every field):

```json
{
    "value": "<any JSON value>",
    "label": "<non-empty string>",
    "format": "<optional client hint>"
}
```

**Response** `200` — updated `SnAccountBoardItem` (Passport model).

**Errors:**

| Status | When |
|--------|------|
| `400` | Invalid payload / missing required fields / bad `account_id` |
| `401` | Missing/invalid secret |
| `403` | Missing board scope on app, or user has not authorized the app |
| `404` | App or board item not found |

**Scope of update:**

- Updates **payload only**
- Cannot change order, enabled flag, or placement
- Cannot update items whose `custom_app_id` is not this app
- Cannot update prebuilt widgets

---

## Recommended integration flow

```text
1. Developer creates widget manifests (console JWT APIs)
2. User discovers app widgets (GET /api/apps/board)
3. User authorizes app with accounts.profile.board (OIDC)
4. User adds widget to board (PUT /api/accounts/me/board) with empty payload
5. App backend lists its manifests (GET .../board/widgets)
6. App backend pushes data (POST .../board/payload)
```

Example placement body (user client) — custom-app `payload` is ignored server-side:

```json
[
    {
        "order": 0,
        "kind": "custom_app",
        "custom_app_id": "1b9d6d7f-4d92-4af6-b4ec-7b8d2f9e0c91",
        "custom_app_widget_key": "summary_card",
        "is_enabled": true,
        "payload": {}
    }
]
```

On layout replace, Passport:

- Checks the widget exists and is enabled (`GetBoardWidget`)
- Ignores client `payload` for custom-app items
- Preserves any previously pushed payload (by item id, then by app + widget key)

---

## What is not available on the private API

| Action | Use instead |
|--------|-------------|
| Create/update/delete widget **manifests** | Developer JWT APIs: `POST/PUT/DELETE /api/private/apps/{app_id}/board...` with `dev` + `proj` |
| Change user’s board order | User `PUT /api/accounts/me/board` |
| List another app’s widgets | Not allowed |
| Read arbitrary user’s full board | Not allowed (push only targets matching items) |
| Push payload without user consent | Not allowed (`AuthorizedApp` + `accounts.profile.board` required) |

---

## Widget manifest config (developer console)

These require user JWT + developer editor role + `CustomAppsUpdate` — **not** app secret:

| Method | Path |
|--------|------|
| `GET` | `/api/private/apps/{app_id}/board?dev=...&proj=...` |
| `POST` | `/api/private/apps/{app_id}/board?dev=...&proj=...` |
| `PUT` | `/api/private/apps/{app_id}/board/{widget_key}?dev=...&proj=...` |
| `DELETE` | `/api/private/apps/{app_id}/board/{widget_key}?dev=...&proj=...` |

See `docs/ACCOUNT_BOARD.md` for request bodies and field models.

---

## Validation summary

### Placement (`PUT` user board)

- Custom app id + widget key present
- Widget exists, enabled, app has board scope
- `allow_multiple = false` enforced for duplicates
- Payload content **not** taken from client; empty or preserved from DB

### Payload push (this private API)

- Full schema validation when payload is non-empty:
  - `required_fields`
  - field envelope `{value, label, format?}`
  - `field_types`
  - `max_payload_bytes`
- Ownership: board item must belong to this `app_id` + `widget_key`
- User consent: `AuthorizedApp` with `accounts.profile.board`

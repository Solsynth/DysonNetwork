# Account Board

This document describes the `Board` customization surface added across:

- `DysonNetwork.Passport` for per-account board persistence and public/self APIs
- `DysonNetwork.Develop` for custom widget metadata and payload validation
- `DysonNetwork.Shared` / `Spec` for shared proto and model contracts

All JSON fields are serialized as `snake_case`.

## Universal Payload Contract

Every board widget payload — whether prebuilt or custom-app — uses **one** envelope format. No other payload shape is accepted anywhere in the system.

```json
{
    "field_name": {
        "value": "<any JSON value>",
        "label": "<non-empty human-readable string>",
        "format": "<optional client formatter enum>"
    }
}
```

- `value` — required. Any JSON value (`string`, `number`, `boolean`, `null`, `object`, `array`). Objects and arrays may contain arbitrarily nested JSON values.
- `label` — required. A non-empty string describing the field.
- `format` — optional. An enum hint for the client renderer (e.g. `"boolean"`, `"number"`, `"date"`, `"currency"`). When absent, the client infers rendering from the value type.

This contract is enforced on every write path that accepts payload content:

- `PUT /api/accounts/me/board` — **prebuilt** payloads only (custom-app payloads are app-owned; see below)
- `POST /api/private/apps/{app_id}/board/payload` — custom-app secret push
- Admin board payload push
- Internally by `Develop.CustomAppService.ValidateBoardWidgetPayload` for non-empty custom-app payloads

Any non-empty payload field that does not conform to this envelope is rejected.

For custom-app widgets, see also `docs/CUSTOM_APP_BOARD_PRIVATE_API.md`.

## Overview

`Board` is a public account-profile extension that lets each account arrange profile widgets in a custom order.

There are two widget sources:

- prebuilt widgets owned by Passport
- custom widgets backed by Develop `CustomApp`

Each custom app can publish multiple board widget definitions. Passport stores the selected app identity plus the selected widget definition key for each board item.

Passport owns:

- which widgets an account has on its board
- widget ordering
- per-widget enabled state
- stored widget payload (persisted rows)
- prebuilt widget payload writes from the user client

Develop owns:

- whether a `CustomApp` is board-widget capable
- widget renderer/type metadata (manifests)
- payload schema validation for custom-app widgets
- **custom-app payload writes** via the app secret private API

### Custom-app payload ownership

| Writer | Can set custom-app payload? |
|--------|-----------------------------|
| User `PUT /api/accounts/me/board` | **No** — client payload ignored; existing server payload preserved |
| App secret `POST .../board/payload` | **Yes** — only widgets for that `app_id` |
| Admin push | **Yes** (admin override) |

Initial placement uses an empty payload. The custom app configures the widget later with its secret.

Inter-service communication for board widgets is gRPC only.

## Data Model

Passport stores board items in a separate table instead of embedding the whole board into `account_profiles`.

Table:

- `account_board_items`

Model:

- `SnAccountBoardItem`

Fields:

- `id`
- `account_id`
- `order`
- `kind`
- `widget_key`
- `custom_app_id`
- `custom_app_widget_key`
- `is_enabled`
- `payload` as `jsonb`
- standard `created_at`, `updated_at`, `deleted_at`

Uniqueness:

- `(account_id, order)` must be unique

The public/shared profile model `SnAccountProfile` exposes:

- `board`

but that collection is hydrated from `account_board_items`, not persisted inline on `account_profiles`.

## Public Profile Contract

The shared proto `DyAccountProfile` now includes:

- `repeated DyAccountBoardItem board`

`DyAccountBoardItem` contains:

- `id`
- `order`
- `kind`
- `widget_key`
- `custom_app_id`
- `custom_app_widget_key`
- `payload`
- `is_enabled`

`payload` uses `google.protobuf.Struct` so Passport can store flexible widget configuration without defining per-widget typed proto fields.

**NOTICE: service need to use the services name instead of the /api when accessing in production via Gateway. e.g. `/passport` instead `/api` for Passport APIs**

## Passport APIs

Base route:

```text
/api/accounts/me
```

OAuth / authorized-app scope for self board access:

- `accounts.profile.board.manage`

Custom apps that want to expose board widgets must declare in OAuth **allowed scopes**:

- `accounts.profile.board`

That is the app's capability declaration (`CustomApp.OauthConfig.AllowedScopes`), not the per-user authorized-app grant.

To push payload updates for a user, that user must also have authorized the app with the same scope (stored on Padlock `AuthorizedApp.Scopes` after OIDC consent).

### GET /api/accounts/me/board

Returns the caller's board items ordered by `order`.

Required permission or OAuth scope:

- `accounts.profile.board.manage`

Response shape:

```json
[
    {
        "id": "de305d54-75b4-431b-adb2-eb6b9e546014",
        "account_id": "550e8400-e29b-41d4-a716-446655440000",
        "order": 0,
        "kind": "prebuilt",
        "widget_key": "badges",
        "custom_app_id": null,
        "custom_app_widget_key": null,
        "is_enabled": true,
        "payload": {
            "display_count": {
                "value": 5,
                "label": "Display count",
                "format": "number"
            }
        }
    }
]
```

### PUT /api/accounts/me/board

Replaces the full board layout for the caller.

This is the v1 write model. There are no granular move/add/remove endpoints yet.

Required permission or OAuth scope:

- `accounts.profile.board.manage`

Request shape:

```json
[
    {
        "id": "de305d54-75b4-431b-adb2-eb6b9e546014",
        "order": 0,
        "kind": "prebuilt",
        "widget_key": "badges",
        "is_enabled": true,
        "payload": {
            "display_count": {
                "value": 5,
                "label": "Display count",
                "format": "number"
            }
        }
    },
    {
        "order": 1,
        "kind": "custom_app",
        "custom_app_id": "1b9d6d7f-4d92-4af6-b4ec-7b8d2f9e0c91",
        "custom_app_widget_key": "summary_card",
        "is_enabled": true,
        "payload": {}
    }
]
```

Behavior:

- validates duplicate `order` values
- prebuilt widgets: accepts client payload (envelope enforced where applied); clears custom-app fields
- custom-app widgets: validates widget exists and is enabled via Develop `GetBoardWidget` gRPC
- custom-app widgets: **ignores** client-supplied `payload`
- custom-app widgets: **preserves** previously stored payload across replace (match by item `id`, then by `custom_app_id` + `custom_app_widget_key`)
- new custom-app placements start with empty payload until the app pushes data
- replaces all existing board items in one transaction
- purges cached account hydration after mutation

## User Widget Discovery & Installation

Users discover and install custom-app board widgets through a public discovery API, then add them to their board via the self-board PUT endpoint.

### Step 1: Discover available widgets

Public endpoint — no auth required:

```
GET /api/apps/board?take=20&offset=0
```

Optional filter by app slug:

```
GET /api/apps/board?slug=my-app-slug
```

Response:

```json
[
    {
        "id": "1b9d6d7f-4d92-4af6-b4ec-7b8d2f9e0c91",
        "slug": "my-app",
        "name": "My App",
        "description": "An app that provides board widgets",
        "publisher_name": "littlesheep",
        "board_widgets": [
            {
                "key": "summary_card",
                "slug": "littlesheep.my-app.summary_card",
                "is_enabled": true,
                "renderer_type": "hero",
                "field_types": [
                    {"name": "title", "type": "string", "label": "Title", "format": "", "required": true}
                ],
                "required_fields": ["title"],
                "max_payload_bytes": 2048,
                "allow_multiple": false
            }
        ]
    }
]
```

Only returns apps that:

- declare the `accounts.profile.board` OAuth scope
- have at least one enabled board widget

### Widget slug format

Each widget slug is dynamically built as:

```text
{publisher_name}.{app_slug}.{widget_key}
```

- `publisher_name` — the publisher's unique identifier (not nick/display name)
- `app_slug` — the custom app's slug
- `widget_key` — the widget definition key within the app

This mirrors the product slug pattern and gives every widget a globally unique, human-readable identifier without requiring a separate stored field.

### Step 2: Add widget to board

Once a user knows the `custom_app_id` and `custom_app_widget_key`, they include it in their board layout via `PUT /api/accounts/me/board`:

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

The board service validates the custom widget against Develop gRPC (manifest exists, is enabled, board scope, `allow_multiple`). Payload content is **not** supplied by the user; the custom app fills it later via the private payload API.

## Profile Hydration

Board data is included in:

- `GET /api/accounts/me`
- `GET /api/accounts/{name}`
- profile gRPC payloads that serialize `DyAccountProfile`

Board items are always returned ordered by `order`.

Only the dedicated self board endpoints require the board-management scope. Public profile hydration stays public.

## Custom App Private API (app secret)

Focused guide: `docs/CUSTOM_APP_BOARD_PRIVATE_API.md`.

These endpoints live in Develop and authenticate with a custom app API secret (not developer JWT).

Authentication for both:

- `Authorization: Bearer <custom_app_secret>`
- or `X-App-Secret: <custom_app_secret>`

The secret must be a valid non-OIDC custom app secret for the path `app_id`. The app must declare `accounts.profile.board` in `oauth_config.allowed_scopes`.

### GET /api/private/apps/{app_id}/board/widgets

Lists **only this app’s** board widget manifests. Does not return other apps’ widgets, prebuilt widgets, or user board layouts.

Response: array of `SnBoardWidgetManifest` (key, enabled, renderer, field types, required fields, etc.).

### POST /api/private/apps/{app_id}/board/payload

Pushes payload into an already-installed board widget instance for a user.

Additional requirements:

- the target user must have an active OIDC `AuthorizedApp` record for this app that includes `accounts.profile.board` (user consent)
- the target widget manifest must exist and be enabled
- the target board item must already exist on the user's board
- non-empty payload must satisfy `required_fields`, field types, size limit, and the universal envelope

Request shape:

```json
{
    "account_id": "550e8400-e29b-41d4-a716-446655440000",
    "widget_key": "summary_card",
    "payload": {
        "title": {
            "value": "Updated from app backend",
            "label": "Title"
        },
        "show_points": {
            "value": false,
            "label": "Show points",
            "format": "boolean"
        }
    }
}
```

Or, when `allow_multiple` is `true` and a specific instance must be targeted:

```json
{
    "account_id": "550e8400-e29b-41d4-a716-446655440000",
    "board_item_id": "de305d54-75b4-431b-adb2-eb6b9e546014",
    "widget_key": "summary_card",
    "payload": { }
}
```

Behavior:

- `board_item_id` is optional. When omitted, Passport auto-finds the first matching board item for the given account, custom app, and widget key.
- When `allow_multiple` is `true` and multiple instances exist, include `board_item_id` to target a specific instance.
- Develop validates and normalizes the payload before sending it to Passport
- Passport verifies that the board item belongs to the specified account, custom app, and widget key before updating only the payload
- board order, enabled state, and board placement remain Passport-owned and are not changed by this endpoint
- this API cannot update another app’s widgets or prebuilt widgets

## Developer Board Widget Config API

Custom apps manage their board widget definitions through these endpoints. Each endpoint operates on a single widget definition identified by `key`.

Base route:

```text
/api/private/apps/{app_id}
```

Authentication:

- standard developer auth (`dev` + `proj` query params, editor role required)
- `CustomAppsUpdate` permission

These are **not** the app-secret private APIs. Use them from the developer console to create/update/delete manifests. Runtime backends should use `GET .../board/widgets` + `POST .../board/payload` with the app secret.

### GET /api/private/apps/{app_id}/board

Lists widget manifests for the app (developer JWT). Same data as the secret `GET .../board/widgets` list, but requires editor access to the developer project.

### POST /api/private/apps/{app_id}/board

Creates a single board widget definition for the app.

Request shape:

```json
{
    "key": "littlesheep_mood",
    "is_enabled": true,
    "renderer_type": "hero",
    "field_types": [
        {"name": "image", "type": "string", "label": "Image", "format": "", "required": true},
        {"name": "background", "type": "string", "label": "Background", "format": "", "required": true},
        {"name": "mood", "type": "string", "label": "Mood", "format": "", "required": false}
    ],
    "required_fields": ["image", "background"],
    "max_payload_bytes": 2048,
    "allow_multiple": false
}
```

Behavior:

- `key` must be unique within the app's board widgets
- `renderer_type` defaults to `"data"` when omitted
- `payload_type` is fixed to `object`
- returns `400` if a widget with the same `key` already exists
- returns the created widget on success

### PUT /api/private/apps/{app_id}/board/{widget_key}

Updates an existing board widget definition by key.

Request shape — same as POST:

```json
{
    "key": "littlesheep_mood",
    "is_enabled": true,
    "renderer_type": "hero",
    "field_types": [
        {"name": "image", "type": "string", "label": "Image", "format": "", "required": true},
        {"name": "background", "type": "string", "label": "Background", "format": "", "required": true},
        {"name": "mood", "type": "string", "label": "Mood", "format": "", "required": false}
    ],
    "required_fields": ["image", "background"],
    "max_payload_bytes": 2048,
    "allow_multiple": false
}
```

Behavior:

- `widget_key` in the path identifies which widget to update
- all fields on the existing widget are replaced with the request values
- returns `404` if no widget with the given key exists
- returns the updated widget on success

### DELETE /api/private/apps/{app_id}/board/{widget_key}

Removes a board widget definition by key.

Behavior:

- returns `204 No Content` on success
- returns `404` if no widget with the given key exists

### Field types model

`field_types` is an array of field definitions, each with:

- `name` — the field key in payload data (e.g. `"image"`)
- `type` — the expected JSON type (`"string"`, `"number"`, `"boolean"`, `"object"`, `"array"`)
- `label` — human-readable label for the field
- `format` — optional client formatter hint (e.g. `"date"`, `"currency"`)
- `required` — whether this field must be present in payloads

The corresponding field in the stored payload must conform to this type during `ValidateBoardWidgetPayload`.

## Develop gRPC Contract

Service:

- `DyCustomAppService`

New `CustomApp` metadata:

- `board_widgets`

Manifest model:

- `DyBoardWidgetManifest`

Fields:

- `key`
- `is_enabled`
- `renderer_type`
- `field_types` — array of `{name, type, label, format, required}` (stored as JSONB; proto serializes as `map<string,string>` of `name→type`)
- `required_fields` — list of field names that must be present in payloads
- `max_payload_bytes` — optional payload size limit
- `allow_multiple` — whether multiple instances of this widget are allowed per board

`payload_type` is fixed to `object` for Board widgets in this v1 shape.

### GetBoardWidget

Request:

```proto
message DyGetBoardWidgetRequest {
  string app_id = 1;
  string widget_key = 2;
}
```

Response:

- resolved `DyCustomApp`
- resolved `DyBoardWidgetManifest`

Use this when a caller needs board-widget capability metadata for a custom app.

### ValidateBoardWidgetPayload

Request:

- `app_id`
- `widget_key`
- `payload` as `google.protobuf.Struct`

Response:

- `valid`
- `message`
- `normalized_payload`
- `widget`

Passport uses `GetBoardWidget` when placing custom-app widgets on a board (existence / enabled / scope).

Develop uses `ValidateBoardWidgetPayload` before forwarding app-secret (and admin) payload pushes to Passport.

Empty payload is valid (widget not yet configured). Non-empty payloads must include all `required_fields` and pass envelope / type checks.

## Board Discovery gRPC Contract

Service:

- `DyBoardAuthService`

Server: Padlock (owns `AuthorizedApps`). Client: Develop.

### QueryAuthorizedBoardApps

Returns the list of apps that a user has authorized (via OAuth) that also expose board widgets. Payloads are JSON-serialized DTOs wrapped in `ByteString`.

Request: `DyQueryAuthorizedBoardAppsRequest`

- `account_id` — the user's account ID
- `take` — max results (default 20)
- `offset` — pagination offset
- `app_slug` — optional: filter by app slug

Response: `DyQueryAuthorizedBoardAppsResponse`

- `apps` — list of `DyAuthorizedBoardAppDto` (`app_id`, `app_slug`, `app_name`, `publisher_name`)
- `total_count` — total matching results

This endpoint is called by the Develop `BoardDiscoveryController` when a logged-in user requests `GET /api/apps/board`. When the user is unauthenticated, the controller falls back to listing all board-capable apps without authorization filtering.

## Validation Rules

### Prebuilt widgets

Prebuilt widgets are controlled by the client. The server does not validate prebuilt widget keys, singleton usage, or payload structure — it only clears `custom_app_id` and `custom_app_widget_key` to keep the data clean. Any widget key is accepted.

### Custom-app widgets — placement (`PUT` board)

- `custom_app_id` and `custom_app_widget_key` required
- app OAuth config includes `accounts.profile.board` in **allowed scopes**
- matching widget exists and `is_enabled` is `true`
- singleton behavior respects `allow_multiple = false`
- client payload is ignored; server payload is empty (new) or preserved (existing)

### Custom-app widgets — payload push (app secret)

- widget exists, enabled, `payload_type` is `object`
- non-empty payload: size ≤ `max_payload_bytes`, all `required_fields` present, envelope + field types
- board item belongs to this app + widget key
- target user has authorized the app with `accounts.profile.board` (Padlock `AuthorizedApp`)

## Migrations

Passport migration:

- `AddAccountBoardItems`

Develop migrations:

- `AddBoardWidgetManifest` (original JSONB approach)
- `SupportMultipleBoardWidgets`
- `AddBoardWidgetTable` (migrates from JSONB to separate table)

These must be applied together when deploying the full feature.

## Board Widget Data Model

Board widget definitions are stored in a separate table (`board_widgets`) instead of JSONB inside `custom_apps`. This avoids EF Core translation issues with JSON-baked columns and enables efficient querying.

Table: `board_widgets`

| Column | Type | Notes |
|--------|------|-------|
| `id` | uuid PK | |
| `app_id` | uuid FK → custom_apps.id | cascade delete |
| `key` | varchar(128) | widget identifier |
| `is_enabled` | bool | |
| `renderer_type` | varchar(128) | default `"data"` |
| `payload_type` | varchar(128) | fixed `"object"` |
| `field_types` | jsonb | `[{name, type, label, format, required}]` |
| `required_fields` | jsonb | string array |
| `max_payload_bytes` | int | optional |
| `allow_multiple` | bool | default true |
| standard timestamps | | `created_at`, `updated_at`, `deleted_at` |

Unique constraint: `(app_id, key)` — one widget definition per key per app.

Proto serialization still uses `repeated DyBoardWidgetManifest board_widgets` on `DyCustomApp`, with `SnBoardWidget.ToProtoValue()` handling the conversion.

## Implementation Notes

- Passport persistence is intentionally relational for ordering and replacement semantics.
- Flexible widget instance data stays in `jsonb`.
- Develop remains the source of truth for custom-widget schema without forcing Passport to understand app-specific payload structure.
- If a future version needs multiple widget definitions per `CustomApp`, add that on the Develop side rather than overloading Passport persistence.

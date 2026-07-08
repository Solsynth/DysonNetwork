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

- `value` — required. Any JSON value (`string`, `number`, `boolean`, `null`, `object`, `array`).
- `label` — required. A non-empty string describing the field.
- `format` — optional. An enum hint for the client renderer (e.g. `"boolean"`, `"number"`, `"date"`, `"currency"`). When absent, the client infers rendering from the value type.

This contract is enforced on every write path:
- `PUT /api/accounts/me/board` — both prebuilt and custom-app payloads
- `POST /api/private/apps/{app_id}/board/payload` — custom-app developer push
- Internally by `Passport.AccountBoardService` and `Develop.CustomAppService.ValidateBoardWidgetPayload`

Any payload field that does not conform to this envelope is rejected.

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
- stored widget payload

Develop owns:

- whether a `CustomApp` is board-widget capable
- widget renderer/type metadata
- payload validation rules

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

Custom apps that want to expose board widgets must also declare:

- `accounts.profile.board`

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
        "payload": {
            "title": {
                "value": "My widget",
                "label": "Title"
            },
            "show_points": {
                "value": true,
                "label": "Show points",
                "format": "boolean"
            }
        }
    }
]
```

Behavior:

- validates duplicate `order` values
- validates prebuilt widget keys against Passport allowlist
- validates custom widget availability and payload through Develop gRPC
- replaces all existing board items in one transaction
- purges cached account hydration after mutation

## Profile Hydration

Board data is included in:

- `GET /api/accounts/me`
- `GET /api/accounts/{name}`
- profile gRPC payloads that serialize `DyAccountProfile`

Board items are always returned ordered by `order`.

Only the dedicated self board endpoints require the board-management scope. Public profile hydration stays public.

## Developer Payload Push API

Custom apps can manually push payload updates into an already-installed board widget instance for a user.

This endpoint lives in Develop, authenticates with a custom app API secret, validates the payload against the app's widget manifest in Develop, and then persists the normalized payload in Passport through gRPC.

This endpoint enforces the same universal payload contract described above. Every field in the payload **must** use the `{value, label, format?}` envelope. No other shape is accepted.

Route:

```text
/api/private/apps/{app_id}/board/payload
```

Authentication:

- `Authorization: Bearer <custom_app_secret>`
- or `X-App-Secret: <custom_app_secret>`

Requirements:

- the secret must be a valid non-OIDC custom app secret for the `app_id`
- the app must declare `accounts.profile.board`
- the target widget manifest must exist and be enabled
- the target board item must already exist on the user's board

Request shape:

```json
{
    "account_id": "550e8400-e29b-41d4-a716-446655440000",
    "board_item_id": "de305d54-75b4-431b-adb2-eb6b9e546014",
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

Behavior:

- `board_item_id` is required because one custom app can expose multiple widget definitions and each definition may allow multiple installed instances
- Develop validates and normalizes the payload before sending it to Passport
- Passport verifies that the board item belongs to the specified account, custom app, and widget key before updating only the payload
- board order, enabled state, and board placement still remain Passport-owned and are not changed by this endpoint

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
- `field_types`
- `required_fields`
- `max_payload_bytes`
- `allow_multiple`

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

Passport uses this before saving any custom-app board item.

Develop also uses the same validation logic before forwarding developer-initiated payload pushes to Passport.

## Validation Rules

### Prebuilt widgets

Passport currently allows these widget keys:

- `badges`
- `bio`
- `links`
- `notable_days`
- `social_credits`

They are currently treated as singleton widgets.

### Custom-app widgets

Custom widgets must satisfy all of the following:

- `custom_app_id` exists
- `custom_app_widget_key` exists
- app OAuth config includes `accounts.profile.board`
- app has a matching entry in `board_widgets`
- `board_widgets[n].is_enabled` is `true`
- app status is `Production`
- widget `payload_type` is `object`
- payload size does not exceed `max_payload_bytes` when configured
- all `required_fields` are present
- **every payload field conforms to the universal payload envelope** (`{value, label, format?}`)
- singleton behavior respects `allow_multiple = false`

### Prebuilt widgets

Prebuilt widgets also enforce the universal payload envelope on every field. Pass raw `{}` or non-envelope values and the write is rejected.

## Migrations

Passport migration:

- `AddAccountBoardItems`

Develop migration:

- `AddBoardWidgetManifest`

These must be applied together when deploying the full feature.

## Implementation Notes

- Passport persistence is intentionally relational for ordering and replacement semantics.
- Flexible widget instance data stays in `jsonb`.
- Develop remains the source of truth for custom-widget schema without forcing Passport to understand app-specific payload structure.
- If a future version needs multiple widget definitions per `CustomApp`, add that on the Develop side rather than overloading Passport persistence.

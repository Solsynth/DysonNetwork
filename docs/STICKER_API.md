# Sticker API

This document covers sticker-specific API behavior in `DysonNetwork.Sphere/Sticker/StickerController.cs`.

All examples below use local development routes. In production, the gateway rewrites `/api/stickers/...` to `/sphere/stickers/...`.

## Sticker Rendering Settings

Stickers now expose two client-rendering fields:

- `size`
- `mode`

These fields are metadata for the client renderer only. The backend does not apply any special behavior based on them.

`size` and `mode` are int-backed enums in API payloads.

## Sticker Size

Supported values:

- `0`: `auto`
- `1`: `small`
- `2`: `medium`
- `3`: `large`

Default value:

- `auto`

## Sticker Mode

Supported values:

- `0`: `sticker`
- `1`: `emote`

Default value:

- `sticker`

## Sticker Object

Example response shape:

```json
{
  "id": "7b602089-3c6c-45bf-91be-4c812e4fb5a1",
  "slug": "wave",
  "name": "Waving Hand",
  "image": {
    "id": "file_123"
  },
  "size": 0,
  "mode": 0,
  "order": 0,
  "pack_id": "d65dce58-1f7d-4adc-a9a4-6d33fd62ee28",
  "created_at": "2026-05-10T10:00:00Z",
  "updated_at": "2026-05-10T10:00:00Z"
}
```

## Create Sticker

Endpoint:

```text
POST /api/stickers/{packId}/content
```

Request body:

```json
{
  "name": "Waving Hand",
  "slug": "wave",
  "image_id": "file_123",
  "size": 0,
  "mode": 0
}
```

Notes:

- `slug` is required.
- `image_id` is required.
- `name` is optional. If omitted, it falls back to `slug` on create.
- `size` is optional and defaults to `0` (`auto`).
- `mode` is optional and defaults to `0` (`sticker`).

## Update Sticker

Endpoint:

```text
PATCH /api/stickers/{packId}/content/{id}
```

Request body:

```json
{
  "name": "Waving Hand Large",
  "slug": "wave_alt",
  "image_id": "file_456",
  "size": 3,
  "mode": 1
}
```

Notes:

- All fields are optional.
- Only provided fields are updated.

## Batch Update Sticker Rendering Settings

Endpoint:

```text
PATCH /api/stickers/{packId}/content/batch/rendering-settings
```

Request body:

```json
{
  "sticker_ids": [
    "7b602089-3c6c-45bf-91be-4c812e4fb5a1",
    "8a4466f0-5ce8-4f03-8ab3-9f5b51f50dea"
  ],
  "size": 3,
  "mode": 1
}
```

Notes:

- `sticker_ids` is required.
- At least one of `size` or `mode` must be provided.
- All sticker ids must belong to the target pack.
- The endpoint updates up to 24 stickers per request.
- This endpoint only changes rendering metadata. It does not update `slug` or `image_id`.

Response body:

```json
[
  {
    "id": "7b602089-3c6c-45bf-91be-4c812e4fb5a1",
    "slug": "wave",
    "size": "large",
    "mode": 1
  },
  {
    "id": "8a4466f0-5ce8-4f03-8ab3-9f5b51f50dea",
    "slug": "smile",
    "size": 3,
    "mode": 1
  }
]
```

## Lookup Sticker By Placeholder

Endpoint:

```text
GET /api/stickers/lookup/{identifier}
```

Supported identifier formats:

- sticker id: `7b602089-3c6c-45bf-91be-4c812e4fb5a1`
- legacy placeholder: `packwave`
- v2 placeholder: `pack+wave`

## Open Sticker Asset By Placeholder

Endpoint:

```text
GET /api/stickers/lookup/{identifier}/open
```

Behavior:

- Resolves the sticker by identifier.
- Redirects to `/drive/files/{image.id}?original=true`.

## Batch Lookup By Placeholder

Endpoint:

```text
POST /api/stickers/lookup/batch
```

Request body:

```json
{
  "placeholders": [
    "pack+wave",
    "pack+smile",
    "7b602089-3c6c-45bf-91be-4c812e4fb5a1"
  ]
}
```

Notes:

- The endpoint accepts up to 100 non-empty placeholders per request.
- Each item is resolved independently.
- The original placeholder string is preserved in the response.

Response body:

```json
[
  {
    "placeholder": "pack+wave",
    "sticker": {
      "id": "7b602089-3c6c-45bf-91be-4c812e4fb5a1",
      "slug": "wave",
      "name": "Waving Hand",
      "size": 0,
      "mode": 0
    }
  },
  {
    "placeholder": "pack+missing",
    "sticker": null
  }
]
```

## Migration Note

This feature adds new columns to the `stickers` and `sticker_pack_ownerships` tables:

- `stickers`: `size`, `mode`, `name`, `order`
- `sticker_pack_ownerships`: `order`

Apply the Sphere migration before using these fields in production.

## Sticker Order

Both stickers and sticker pack ownerships have an `order` field (int, default 0). This field controls the display order:

- Stickers within a pack are sorted by `order` ascending.
- Owned sticker packs are sorted by `order` ascending.

The `order` field is set to `0` by default. Clients should use the reorder endpoints to define a custom ordering.

## List Owned Sticker Packs

Endpoint:

```text
GET /api/stickers/me
```

Requires authentication.

Returns the current user's owned sticker packs as a list of ownership objects, sorted by `order` ascending. Each ownership includes the nested `pack` object with its `stickers` (also sorted by `order`) and `publisher`.

Response body:

```json
[
  {
    "id": "ownership-id",
    "pack_id": "pack-id",
    "account_id": "account-id",
    "order": 0,
    "pack": {
      "id": "pack-id",
      "name": "My Pack",
      "prefix": "mypack",
      "stickers": [
        {
          "id": "sticker-id",
          "slug": "wave",
          "name": "Wave",
          "order": 0
        }
      ],
      "publisher": {
        "id": "pub-id",
        "name": "my-publisher"
      }
    },
    "created_at": "2026-05-10T10:00:00Z",
    "updated_at": "2026-05-10T10:00:00Z"
  }
]
```

## Acquire Sticker Pack

Endpoint:

```text
POST /api/stickers/{packId}/own
```

Requires authentication.

Adds the sticker pack to the current user's collection. Returns the existing ownership if already acquired.

## Release Sticker Pack

Endpoint:

```text
DELETE /api/stickers/{packId}/own
```

Requires authentication.

Removes the sticker pack from the current user's collection.

## Reorder Owned Sticker Packs

Endpoint:

```text
PATCH /api/stickers/me/order
```

Requires authentication.

Updates the `order` field on the current user's owned sticker packs.

Request body:

```json
{
  "items": [
    { "id": "ownership-id-1", "order": 0 },
    { "id": "ownership-id-2", "order": 1 },
    { "id": "ownership-id-3", "order": 2 }
  ]
}
```

Notes:

- `id` refers to the ownership ID (not the pack ID). Use the `GET /api/stickers/me` response to get ownership IDs.
- All IDs must belong to the current user.
- Only the `order` field is updated.

## Reorder Stickers In Pack

Endpoint:

```text
PATCH /api/stickers/{packId}/content/order
```

Requires authentication. User must be at least an Editor of the publisher that owns the pack.

Updates the `order` field on stickers within a pack.

Request body:

```json
{
  "items": [
    { "id": "sticker-id-1", "order": 0 },
    { "id": "sticker-id-2", "order": 1 },
    { "id": "sticker-id-3", "order": 2 }
  ]
}
```

Notes:

- All sticker IDs must belong to the target pack.
- Only the `order` field is updated.

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

This feature adds two new columns to the `stickers` table:

- `size`
- `mode`
- `name`

Apply the Sphere migration before using these fields in production.

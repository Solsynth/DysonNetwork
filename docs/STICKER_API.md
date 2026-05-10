# Sticker API

This document covers sticker-specific API behavior in `DysonNetwork.Sphere/Sticker/StickerController.cs`.

All examples below use local development routes. In production, the gateway rewrites `/api/stickers/...` to `/sphere/stickers/...`.

## Sticker Rendering Settings

Stickers now expose two client-rendering fields:

- `size`
- `mode`

These fields are metadata for the client renderer only. The backend does not apply any special behavior based on them.

## Sticker Size

Supported values:

- `auto`
- `small`
- `medium`
- `large`

Default value:

- `auto`

## Sticker Mode

Supported values:

- `sticker`
- `emote`

Default value:

- `sticker`

## Sticker Object

Example response shape:

```json
{
  "id": "7b602089-3c6c-45bf-91be-4c812e4fb5a1",
  "slug": "wave",
  "image": {
    "id": "file_123"
  },
  "size": "auto",
  "mode": "sticker",
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
  "slug": "wave",
  "image_id": "file_123",
  "size": "auto",
  "mode": "sticker"
}
```

Notes:

- `slug` is required.
- `image_id` is required.
- `size` is optional and defaults to `auto`.
- `mode` is optional and defaults to `sticker`.

## Update Sticker

Endpoint:

```text
PATCH /api/stickers/{packId}/content/{id}
```

Request body:

```json
{
  "slug": "wave_alt",
  "image_id": "file_456",
  "size": "large",
  "mode": "emote"
}
```

Notes:

- All fields are optional.
- Only provided fields are updated.

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
      "size": "auto",
      "mode": "sticker"
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

Apply the Sphere migration before using these fields in production.

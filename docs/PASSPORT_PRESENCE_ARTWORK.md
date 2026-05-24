# Passport Presence Artwork

## Overview

Passport now has a dedicated presence artwork resource flow for presence activities.

- Base path: `/api/presence/artworks`
- Service: `DysonNetwork.Passport`
- Gateway path: `/passport/presence/artworks`
- Max file size: `1 MB`
- Storage: S3-compatible object storage via MinIO client
- Retention: unused resources are deleted after `30 days`

This is intentionally separate from the general attachment system. It is designed for small artwork assets referenced by presence activities.

## Reference Model

Presence activities should store artwork references as content hashes:

```text
sha256:<64-char-hex>
```

Example:

```text
sha256:8f434346648f6b96df89dda901c5176b10a6d83961e8f4eab9a7d6e2d7d3c9f2
```

`large_image` and `small_image` in `/api/activities` may use this format. When they do, Passport validates that the artwork exists and refreshes its retention timestamp.

## Upload Artwork

Upload an image as multipart form data.

**Endpoint**

```text
POST /api/presence/artworks
```

**Production Gateway**

```text
POST /passport/presence/artworks
```

**Authentication**

- Required

**Request**

- Content type: `multipart/form-data`
- Field name: `file`

**Example cURL**

```bash
curl -X POST "http://localhost:5001/api/presence/artworks" \
  -H "Authorization: Bearer <token>" \
  -F "file=@cover.png"
```

**Response**

```json
{
  "hash": "sha256:8f434346648f6b96df89dda901c5176b10a6d83961e8f4eab9a7d6e2d7d3c9f2",
  "mime_type": "image/png",
  "size": 58213,
  "url": "/api/presence/artworks/sha256:8f434346648f6b96df89dda901c5176b10a6d83961e8f4eab9a7d6e2d7d3c9f2"
}
```

## Read Artwork

Fetch the stored artwork by hash.

**Endpoint**

```text
GET /api/presence/artworks/{hash}
```

**Production Gateway**

```text
GET /passport/presence/artworks/{hash}
```

Example:

```text
GET /api/presence/artworks/sha256:8f434346648f6b96df89dda901c5176b10a6d83961e8f4eab9a7d6e2d7d3c9f2
```

The response streams the original file with its stored MIME type.

## Use With Presence Activities

Upload first, then reference the returned hash in the activity payload.

**Example**

```json
{
  "type": "Gaming",
  "manual_id": "steam-game",
  "title": "Playing Dyson Sphere Program",
  "large_image": "sha256:8f434346648f6b96df89dda901c5176b10a6d83961e8f4eab9a7d6e2d7d3c9f2",
  "lease_minutes": 10
}
```

If `large_image` or `small_image` starts with `sha256:`, Passport will:

- verify that the artwork exists
- reject unknown hashes with `400 Bad Request`
- update the artwork's `last_referenced_at`

## Validation Rules

- file must not be empty
- file size must be `<= 1048576` bytes
- file MIME type must start with `image/`
- hash format must be `sha256:<64 hex chars>`

Uploads are deduplicated by content hash. Uploading the same image again reuses the existing stored object.

## Retention And Cleanup

Presence artwork is retained for `30 days` after its last reference.

- tracked by `last_referenced_at`
- cleanup runs through Quartz
- expired objects are deleted from S3 first, then removed from the database

This keeps presence artwork lightweight without depending on the larger attachment pipeline.

## Configuration

Passport uses the `PresenceArtwork` config section:

```json
{
  "PresenceArtwork": {
    "KeyPrefix": "presence-artwork",
    "MaxFileSizeBytes": 1048576,
    "RetentionDays": 30,
    "CleanupCron": "0 20 * * * ?",
    "S3": {
      "ServiceUrl": "",
      "Endpoint": "",
      "Region": "us-east-1",
      "AccessKey": "",
      "SecretKey": "",
      "Bucket": "",
      "ForcePathStyle": true,
      "EnableSsl": true
    }
  }
}
```

Required S3 fields:

- `Bucket`
- `AccessKey`
- `SecretKey`
- either `Endpoint` or `ServiceUrl`

## Database

This feature adds a Passport-local table for presence artwork metadata.

You still need to create and apply an EF migration for it.

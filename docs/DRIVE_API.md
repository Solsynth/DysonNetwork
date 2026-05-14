# DysonNetwork Drive API

Complete API reference for the Drive service — file storage, upload, folders, bundles, pools, and billing.

**Production base URL:** `/drive/...`
**Development base URL:** `/api/...`

All endpoints require `Authorization: Bearer <token>` unless marked as public. JSON responses use `snake_case`.

---

## Table of Contents

- [Files](#files)
  - [Open File](#open-file)
  - [Get E2EE Metadata](#get-e2ee-metadata)
  - [Get File Info](#get-file-info)
  - [Get File References](#get-file-references)
  - [Update File Name](#update-file-name)
  - [Update Sensitive Marks](#update-sensitive-marks)
  - [Update User Metadata](#update-user-metadata)
  - [Delete File](#delete-file)
  - [Batch Delete](#batch-delete)
- [Folders & Hierarchy](#folders--hierarchy)
  - [List Root Children](#list-root-children)
  - [List Folder Children](#list-folder-children)
  - [Create Folder](#create-folder)
  - [Move File](#move-file)
  - [List Unindexed Files](#list-unindexed-files)
  - [List My Files](#list-my-files)
  - [Delete Recycled Files (User)](#delete-recycled-files-user)
  - [Delete All Recycled Files (Admin)](#delete-all-recycled-files-admin)
- [Upload](#upload)
  - [Create Upload Task](#create-upload-task-chunked)
  - [Upload Chunk](#upload-chunk)
  - [Complete Upload](#complete-upload)
  - [Direct Upload](#direct-upload)
  - [List Upload Tasks](#list-upload-tasks)
  - [Get Upload Progress](#get-upload-progress)
  - [Resume Upload](#resume-upload)
  - [Cancel Upload](#cancel-upload)
  - [Upload Stats](#upload-stats)
  - [Recent Tasks](#recent-tasks)
  - [Task Details](#task-details)
  - [Cleanup Failed Tasks](#cleanup-failed-tasks)
- [Bundles](#bundles)
  - [Get Bundle](#get-bundle)
  - [List My Bundles](#list-my-bundles)
  - [Create Bundle](#create-bundle)
  - [Update Bundle](#update-bundle)
  - [Delete Bundle](#delete-bundle)
- [Pools](#pools)
  - [List Pools](#list-pools)
  - [Delete Pool Recycled Files](#delete-pool-recycled-files)
- [Billing](#billing)
  - [Get Total Usage](#get-total-usage)
  - [Get Pool Usage](#get-pool-usage)
  - [Get Quota](#get-quota)
  - [Get Quota Records](#get-quota-records)
- [Data Models](#data-models)

---

## Files

### Open File

```
GET /files/{id}
```

**Public** — Serves a file. Handles local files (not yet uploaded), remote files (redirect to signed URL / proxy), and external URLs.

**Path Parameters**

| Parameter | Type   | Description                                  |
|-----------|--------|----------------------------------------------|
| `id`      | string | File ID, optionally with extension (`abc.png`) |

**Query Parameters**

| Parameter          | Type    | Default | Description                              |
|--------------------|---------|---------|------------------------------------------|
| `download`         | bool    | `false` | Set `Content-Disposition: attachment`    |
| `original`         | bool    | `false` | Skip compression, serve original         |
| `thumbnail`        | bool    | `false` | Serve thumbnail variant                  |
| `overrideMimeType` | string  | —       | Override `Content-Type`                  |
| `passcode`         | string  | —       | Bundle passcode for protected files      |

**Responses**

| Code | Description                          |
|------|--------------------------------------|
| 200  | File content or redirect             |
| 403  | Wrong passcode / no read permission  |
| 404  | File not found                       |

**Notes**
- If `storage_url` is set (external/embedded file), redirects to that URL.
- If `uploaded_at` is null (local file not yet on remote), generates a short-lived signed token and redirects to `/files/{id}/access`.
- Otherwise redirects to the remote storage (signed URL, proxy, or direct S3 endpoint).

---

### Get E2EE Metadata

```
GET /files/{id}/e2ee
```

Returns end-to-end encryption metadata stored in the file's object metadata.

**Query Parameters**

| Parameter  | Type   | Description                |
|------------|--------|----------------------------|
| `passcode` | string | Bundle passcode (if needed)|

**Response** `200`

```json
{
  "scheme": "file.aesgcm.v1",
  "header": "<base64>",
  "signature": "<base64>"
}
```

| Code | Description               |
|------|---------------------------|
| 200  | E2EE metadata             |
| 404  | No E2EE metadata on file  |

---

### Get File Info

```
GET /files/{id}/info
```

Returns the full `SnCloudFile` object. Does not require authentication for public files.

**Response** `200` — `SnCloudFile`

---

### Get File References

```
GET /files/{id}/references
```

Returns all cloud files sharing the same underlying storage object (`ObjectId`). Useful for finding deduplicated copies.

**Response** `200` — `SnCloudFile[]`

---

### Update File Name

```
PATCH /files/{id}/name
```

Updates the file's display name. **Owner only.**

**Body** `string`
```json
"new-name.jpg"
```

**Response** `200` — Updated `SnCloudFile`

---

### Update Sensitive Marks

```
PUT /files/{id}/marks
```

Sets content sensitivity labels. **Owner only.**

**Body**
```json
{
  "sensitive_marks": ["Violence", "AdultContent"]
}
```

**Valid values:** `Language`, `SexualContent`, `Violence`, `Profanity`, `HateSpeech`, `Racism`, `AdultContent`, `DrugAbuse`, `AlcoholAbuse`, `Gambling`, `SelfHarm`, `ChildAbuse`, `Other`

**Response** `200` — Updated `SnCloudFile`

---

### Update User Metadata

```
PUT /files/{id}/meta
```

Sets arbitrary user-defined metadata (stored as JSONB). **Owner only.**

**Body** `Dictionary<string, object?>`
```json
{
  "caption": "Sunset",
  "tags": ["nature"]
}
```

**Response** `200` — Updated `SnCloudFile`

---

### Delete File

```
DELETE /files/{id}
```

Permanently deletes a file and its storage data. **Owner only.**

**Response** `200` — Deleted `SnCloudFile`

---

### Batch Delete

```
POST /files/batches/delete
```

Deletes multiple files at once. **Owner only.**

**Body**
```json
{
  "file_ids": ["abc123", "def456"]
}
```

**Response** `200`
```json
{ "count": 2 }
```

---

## Folders & Hierarchy

### List Root Children

```
GET /files/root/children
```

Lists indexed files at the root level (no parent). **Authenticated.**

**Query Parameters**

| Parameter   | Type   | Default  | Description                        |
|-------------|--------|----------|------------------------------------|
| `offset`    | int    | `0`      | Pagination offset                  |
| `take`      | int    | `50`     | Page size                          |
| `query`     | string | —        | Filter by name (substring)         |
| `order`     | string | `"date"` | Sort: `date`, `name`, `size`       |
| `orderDesc` | bool   | `true`   | Sort descending                    |

**Response** `200` — `SnCloudFile[]` (header `X-Total` = total count)

---

### List Folder Children

```
GET /files/{parentId}/children
```

Lists files inside a folder. Same query parameters as root children. **Authenticated.**

**Response** `200` — `SnCloudFile[]`

---

### Create Folder

```
POST /files/folders
```

Creates a new virtual folder. **Authenticated.**

**Body**

| Field       | Type   | Required | Max Length | Description               |
|-------------|--------|----------|------------|---------------------------|
| `name`      | string | Yes      | 1024       | Folder name               |
| `parent_id` | string | No       | 32         | Parent folder ID          |

**Response** `200` — `SnCloudFile` with `is_folder: true`

**Notes**
- Parent must be a folder (`is_folder: true`).
- Folders have no storage object — they're virtual containers.

---

### Move File

```
PATCH /files/{id}/hierarchy
```

Moves a file to a different folder or to root. **Owner only.**

**Body**

| Field       | Type | Required | Description                          |
|-------------|------|----------|--------------------------------------|
| `parent_id` | string | No     | Target folder ID, `null` for root    |
| `indexed`   | bool | No       | Whether file appears in hierarchy    |

**Response** `200` — Updated `SnCloudFile`

**Validation**
- Cannot parent a file to itself.
- Target parent must exist and belong to the same user.

---

### List Unindexed Files

```
GET /files/unindexed
```

Lists files not part of the folder hierarchy (`indexed: false`). **Authenticated.**

**Query Parameters**

| Parameter   | Type   | Default  | Description                        |
|-------------|--------|----------|------------------------------------|
| `pool`      | guid   | —        | Filter by storage pool             |
| `recycled`  | bool   | `false`  | Show recycled files                |
| `offset`    | int    | `0`      | Pagination                         |
| `take`      | int    | `20`     | Page size                          |
| `query`     | string | —        | Filter by name                     |
| `order`     | string | `"date"` | Sort: `date`, `name`, `size`       |
| `orderDesc` | bool   | `true`   | Sort direction                     |

**Response** `200` — `SnCloudFile[]`

---

### List My Files

```
GET /files/me
```

Lists all files owned by the user, regardless of hierarchy. **Authenticated.**

**Query Parameters** — Same as [List Unindexed Files](#list-unindexed-files)

**Response** `200` — `SnCloudFile[]`

---

### Delete Recycled Files (User)

```
DELETE /files/me/recycle
```

Permanently deletes all recycled files for the current user. **Authenticated.**

**Response** `200`
```json
{ "count": 5 }
```

---

### Delete All Recycled Files (Admin)

```
DELETE /files/recycle
```

Permanently deletes all recycled files across all users. **Requires permission:** `files.delete.recycle`

**Response** `200`
```json
{ "count": 42 }
```

---

## Upload

All upload endpoints require authentication. Files are uploaded to a storage pool. If no `pool_id` is specified, the system default is used.

### Create Upload Task (Chunked)

```
POST /files/upload/create
```

Initiates a chunked upload for files > 20MB. Returns a task ID and chunk configuration. Supports **deduplication by hash** — if a file with the same hash already exists, returns the existing file immediately.

**Body** `CreateUploadTaskRequest`

| Field                 | Type       | Required | Description                                        |
|-----------------------|------------|----------|----------------------------------------------------|
| `hash`                | string     | Yes      | MD5 hash of the file                               |
| `file_name`           | string     | Yes      | Original file name                                 |
| `file_size`           | long       | Yes      | Size in bytes                                      |
| `content_type`        | string     | Yes      | MIME type                                          |
| `pool_id`             | guid       | No       | Storage pool (defaults to preferred)               |
| `bundle_id`           | guid       | No       | Associate with a bundle                            |
| `encryption_scheme`   | string     | No       | E2EE scheme (e.g. `file.aesgcm.v1`)               |
| `encryption_header`   | string     | No       | E2EE header (base64)                               |
| `encryption_signature`| string     | No       | E2EE signature (base64)                            |
| `expired_at`          | instant    | No       | Auto-delete timestamp                              |
| `chunk_size`          | long       | No       | Chunk size in bytes (default: 5MB)                 |
| `parent_id`           | string     | No       | Parent folder ID                                   |
| `usage`               | string     | No       | What this file is for (e.g. `post`, `chat_message`)|
| `application_type`    | string     | No       | Composed type (e.g. `live_photo`)                  |

**Response** `200`

```json
{
  "file_exists": false,
  "file": null,
  "task_id": "V1StGXR8_Z5jdHi6B-myT",
  "chunk_size": 5242880,
  "chunks_count": 3
}
```

If a file with the same hash exists:
```json
{
  "file_exists": true,
  "file": { /* SnCloudFile */ },
  "task_id": null,
  "chunk_size": null,
  "chunks_count": null
}
```

**Validation errors**
- Pool not found (404)
- User lacks privilege for pool (403)
- E2EE not allowed in pool (403)
- File too large for pool policy (403)
- Quota exceeded (403)

---

### Upload Chunk

```
POST /files/upload/chunk/{taskId}/{chunkIndex}
```

Uploads a single chunk. Idempotent — returns success if chunk already uploaded.

**Path Parameters**

| Parameter    | Type   | Description            |
|--------------|--------|------------------------|
| `taskId`     | string | Upload task ID         |
| `chunkIndex` | int    | Zero-based chunk index |

**Body** — `multipart/form-data`

| Field   | Type | Description     |
|---------|------|-----------------|
| `chunk` | file | Chunk binary    |

**Response** `200`
```json
{ "message": "Chunk already uploaded" }
```
or empty `200` on first upload.

---

### Complete Upload

```
POST /files/upload/complete/{taskId}
```

Merges all chunks, processes the file (metadata extraction, hashing), saves to database, and triggers async upload to remote storage.

**Response** `200` — `SnCloudFile`

| Code | Description                    |
|------|--------------------------------|
| 200  | File created successfully      |
| 401  | Not authenticated              |
| 403  | Not task owner                 |
| 404  | Task not found                 |
| 500  | Processing failed              |

---

### Direct Upload

```
POST /files/upload/direct
```

Single-request upload for files **≤ 20MB**. Combines upload and processing.

**Body** — `multipart/form-data`

| Field                  | Type     | Required | Description                           |
|------------------------|----------|----------|---------------------------------------|
| `file`                 | file     | Yes      | The file to upload                    |
| `pool_id`              | guid     | No       | Storage pool                          |
| `bundle_id`            | guid     | No       | Bundle association                    |
| `content_type`         | string   | No       | Override MIME type                    |
| `encryption_scheme`    | string   | No       | E2EE scheme                           |
| `encryption_header`    | string   | No       | E2EE header (base64)                  |
| `encryption_signature` | string   | No       | E2EE signature (base64)               |
| `expired_at`           | datetime | No       | Auto-delete timestamp                 |
| `parent_id`            | string   | No       | Parent folder ID                      |
| `usage`                | string   | No       | File usage context                    |
| `application_type`     | string   | No       | Composed file type                    |

**Response** `200` — `SnCloudFile`

**Size limit:** 20MB (returns 400 if exceeded)

---

### List Upload Tasks

```
GET /files/upload/tasks
```

Lists the authenticated user's upload tasks. **Authenticated.**

**Query Parameters**

| Parameter        | Type   | Default          | Description                |
|------------------|--------|------------------|----------------------------|
| `status`         | enum   | —                | Filter by status           |
| `sortBy`         | string | `"lastActivity"` | Sort field                 |
| `sortDescending` | bool   | `true`           | Sort direction             |
| `offset`         | int    | `0`              | Pagination                 |
| `limit`          | int    | `50`             | Page size                  |

**Response** `200` — Array of task objects with progress info.

---

### Get Upload Progress

```
GET /files/upload/progress/{taskId}
```

Returns real-time progress for a task. **Authenticated.**

**Response** `200`
```json
{
  "task_id": "abc123",
  "file_name": "photo.jpg",
  "file_size": 10485760,
  "chunks_count": 2,
  "chunks_uploaded": 1,
  "progress": 50.0,
  "status": "InProgress",
  "last_activity": "2026-05-15T10:30:00Z",
  "uploaded_chunks": [0]
}
```

---

### Resume Upload

```
GET /files/upload/resume/{taskId}
```

Returns information needed to resume an interrupted upload. **Authenticated.**

**Response** `200`
```json
{
  "task_id": "abc123",
  "file_name": "photo.jpg",
  "file_size": 10485760,
  "content_type": "image/jpeg",
  "chunk_size": 5242880,
  "chunks_count": 2,
  "chunks_uploaded": 1,
  "uploaded_chunks": [0],
  "progress": 50.0
}
```

---

### Cancel Upload

```
DELETE /files/upload/task/{taskId}
```

Cancels an in-progress upload and cleans up temp files. **Authenticated.**

**Response** `200`
```json
{ "message": "Upload task cancelled" }
```

---

### Upload Stats

```
GET /files/upload/stats
```

Returns upload statistics for the authenticated user.

**Response** `200`
```json
{
  "total_tasks": 150,
  "in_progress_tasks": 2,
  "completed_tasks": 140,
  "failed_tasks": 5,
  "expired_tasks": 3,
  "total_uploaded_bytes": 1073741824,
  "average_progress": 95.5,
  "recent_activity": "..."
}
```

---

### Recent Tasks

```
GET /files/upload/tasks/recent
```

Returns the user's most recent upload tasks.

**Query Parameters**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `limit`   | int  | `10`    | Max results |

**Response** `200` — Array of task summaries.

---

### Task Details

```
GET /files/upload/tasks/{taskId}/details
```

Returns detailed task info including pool and bundle metadata, estimated time remaining, and upload speed.

**Response** `200`
```json
{
  "task": { /* task details */ },
  "pool": { "id": "...", "name": "...", "description": "..." },
  "bundle": { "id": "...", "name": "...", "description": "..." },
  "estimated_time_remaining": "2 minutes",
  "upload_speed": "3.2 MB/s"
}
```

---

### Cleanup Failed Tasks

```
DELETE /files/upload/tasks/cleanup
```

Cleans up all failed upload tasks for the authenticated user.

**Response** `200`
```json
{ "message": "Cleaned up 3 failed tasks" }
```

---

## Bundles

Bundles are named collections of files with optional passcode protection and expiration. Files in a bundle share the bundle's passcode and expiry.

### Get Bundle

```
GET /bundles/{id}
```

**Public** (if passcode is correct or not set).

**Query Parameters**

| Parameter  | Type   | Description         |
|------------|--------|---------------------|
| `passcode` | string | Bundle passcode     |

**Response** `200` — `SnFileBundle` (includes `files` array)

---

### List My Bundles

```
GET /bundles/me
```

Lists bundles owned by the authenticated user. **Authenticated.**

**Query Parameters**

| Parameter | Type   | Default | Description            |
|-----------|--------|---------|------------------------|
| `term`    | string | —       | Search by name (ILIKE) |
| `offset`  | int    | `0`     | Pagination             |
| `take`    | int    | `20`    | Page size              |

**Response** `200` — `SnFileBundle[]` (header `X-Total` = total count)

---

### Create Bundle

```
POST /bundles
```

Creates a new file bundle. **Authenticated.**

**Body** `BundleRequest`

| Field         | Type    | Required | Max Length | Description                    |
|---------------|---------|----------|------------|--------------------------------|
| `slug`        | string  | No       | 1024       | Custom URL slug (requires subscription) |
| `name`        | string  | No       | 1024       | Display name                   |
| `description` | string  | No       | 8192       | Description                    |
| `passcode`    | string  | No       | 256        | Access passcode (hashed)       |
| `expired_at`  | instant | No       | —          | Auto-expire timestamp          |

**Response** `200` — Created `SnFileBundle`

**Notes**
- If `slug` is omitted, a random 6-char slug is generated.
- Custom slugs require a Stellar Program subscription.
- Passcode is bcrypt-hashed before storage.

---

### Update Bundle

```
PUT /bundles/{id}
```

Updates a bundle. **Owner only.**

**Body** `BundleRequest` — Same as create. Only provided fields are updated.

**Response** `200` — Updated `SnFileBundle`

**Notes**
- Changing `slug` requires a subscription.
- Setting a new `passcode` replaces the old one.

---

### Delete Bundle

```
DELETE /bundles/{id}
```

Deletes a bundle. Files in the bundle are marked for recycle (not immediately deleted). **Owner only.**

**Response** `204` — No Content

---

## Pools

Storage pools are remote storage backends (S3/MinIO). Users upload files to pools. Pool credentials are stripped from responses.

### List Pools

```
GET /pools
```

Lists storage pools available to the authenticated user. Returns public pools and pools owned by the user. **Authenticated.**

**Response** `200` — `FilePool[]`

**Notes**
- `storage_config.secret_id` and `storage_config.secret_key` are cleared before response.

---

### Delete Pool Recycled Files

```
DELETE /pools/{id}/recycle
```

Permanently deletes all recycled files in a pool. **Owner or superuser.**

**Response** `200`
```json
{ "count": 12 }
```

---

## Billing

### Get Total Usage

```
GET /billing/usage
```

Returns total storage usage across all pools for the authenticated user. Cached for 5 minutes. **Authenticated.**

**Response** `200` — `TotalUsageDetails`
```json
{
  "pool_usages": [
    {
      "pool_id": "...",
      "pool_name": "Default Pool",
      "usage_bytes": 1073741824,
      "cost": 1.0,
      "file_count": 42
    }
  ],
  "total_usage_bytes": 1073741824,
  "total_file_count": 42,
  "total_quota": 5368709120,
  "used_quota": 1073741824
}
```

---

### Get Pool Usage

```
GET /billing/usage/{poolId}
```

Returns usage details for a specific pool. **Authenticated.**

**Response** `200` — `UsageDetails`
```json
{
  "pool_id": "...",
  "pool_name": "Default Pool",
  "usage_bytes": 1073741824,
  "cost": 1.0,
  "file_count": 42
}
```

---

### Get Quota

```
GET /billing/quota
```

Returns the user's storage quota breakdown. **Authenticated.**

**Response** `200`
```json
{
  "based_quota": 1073741824,
  "extra_quota": 4294967296,
  "total_quota": 5368709120
}
```

**Quota tiers (based):**
- Normal user: 1 GiB
- Stellar T1: 5 GiB
- Stellar T2: 10 GiB
- Stellar T3: 15 GiB
- Extra quota: 1 GiB per 1 NSD spent

---

### Get Quota Records

```
GET /billing/quota/records
```

Returns the user's quota purchase/addition records. **Authenticated.**

**Query Parameters**

| Parameter | Type | Default | Description              |
|-----------|------|---------|--------------------------|
| `expired` | bool | `false` | Include expired records  |
| `offset`  | int  | `0`     | Pagination               |
| `take`    | int  | `20`    | Page size                |

**Response** `200` — `QuotaRecord[]` (header `X-Total` = total count)
```json
[
  {
    "id": "...",
    "account_id": "...",
    "name": "Stellar T2 Bonus",
    "description": "10 GiB bonus quota",
    "quota": 10240,
    "expired_at": null,
    "created_at": "2026-01-01T00:00:00Z"
  }
]
```

**Note:** `quota` is in MiB.

---

## Data Models

### SnCloudFile

The core file entity.

```json
{
  "id": "abc123def456",
  "name": "photo.jpg",
  "description": null,
  "user_meta": {},
  "sensitive_marks": [],
  "mime_type": "image/jpeg",
  "hash": "d41d8cd98f00b204e9800998ecf8427e",
  "size": 1048576,
  "has_compression": true,
  "has_thumbnail": true,
  "expired_at": null,
  "uploaded_at": "2026-05-15T10:00:00Z",
  "object_id": "abc123def456",
  "parent_id": null,
  "bundle_id": null,
  "indexed": false,
  "is_folder": false,
  "is_marked_recycle": false,
  "storage_id": "abc123def456",
  "storage_url": null,
  "account_id": "550e8400-e29b-41d4-a716-446655440000",
  "usage": "post",
  "application_type": "live_photo",
  "created_at": "2026-05-15T10:00:00Z",
  "updated_at": "2026-05-15T10:00:00Z"
}
```

| Field              | Type       | Description                                                  |
|--------------------|------------|--------------------------------------------------------------|
| `id`               | string(32) | Unique file ID (nanoid, no dashes)                           |
| `name`             | string     | Display name                                                 |
| `description`      | string?    | Optional description                                         |
| `user_meta`        | object?    | Arbitrary user metadata (JSONB)                              |
| `sensitive_marks`  | string[]?  | Content sensitivity labels                                   |
| `mime_type`        | string?    | MIME type (from object)                                      |
| `hash`             | string?    | MD5 hash (from object)                                       |
| `size`             | long       | Size in bytes (from object)                                  |
| `has_compression`  | bool       | Has compressed variant                                       |
| `has_thumbnail`    | bool       | Has thumbnail variant                                        |
| `expired_at`       | instant?   | Auto-delete time                                             |
| `uploaded_at`      | instant?   | When uploaded to remote storage (null = local only)          |
| `object_id`        | string?    | Storage object ID (FK to file_objects)                       |
| `parent_id`        | string?    | Parent folder ID (null = root)                               |
| `bundle_id`        | guid?      | Bundle ID                                                    |
| `indexed`          | bool       | Part of folder hierarchy                                     |
| `is_folder`        | bool       | Is a virtual folder                                          |
| `is_marked_recycle`| bool       | In recycle bin                                               |
| `storage_id`       | string?    | Remote object name (may differ from id)                      |
| `storage_url`      | string?    | External URL (for embedded/off-site files)                   |
| `account_id`       | guid       | Owner                                                        |
| `usage`            | string?    | Upload purpose (e.g. `post`, `chat_message`, `profile_picture`) |
| `application_type` | string?    | Composed type (e.g. `live_photo`)                            |

---

### SnFileBundle

A named, optionally passcode-protected collection of files.

| Field         | Type       | Description                    |
|---------------|------------|--------------------------------|
| `id`          | guid       | Bundle ID                      |
| `slug`        | string     | URL-friendly identifier        |
| `name`        | string     | Display name                   |
| `description` | string?    | Description                    |
| `passcode`    | string?    | Bcrypt-hashed passcode         |
| `expired_at`  | instant?   | Auto-expire time               |
| `account_id`  | guid       | Owner                          |
| `files`       | SnCloudFile[] | Associated files (on GET)   |

---

### FilePool

A remote storage backend configuration.

| Field           | Type               | Description                          |
|-----------------|--------------------|--------------------------------------|
| `id`            | guid               | Pool ID                              |
| `name`          | string             | Display name                         |
| `description`   | string             | Description                          |
| `storage_config`| RemoteStorageConfig| S3/MinIO connection details          |
| `billing_config`| BillingConfig      | Cost multiplier                      |
| `policy_config` | PolicyConfig       | Upload/access policies               |
| `is_hidden`     | bool               | Hidden from non-owner listings       |
| `account_id`    | guid?              | Owner (null = system pool)           |

#### PolicyConfig

| Field              | Type       | Default | Description                              |
|--------------------|------------|---------|------------------------------------------|
| `enable_fast_upload`| bool      | `false` | Allow hash-based dedup                   |
| `enable_recycle`   | bool       | `false` | Soft-delete support                      |
| `public_indexable` | bool       | `false` | Public files are search-indexed          |
| `public_usable`    | bool       | `false` | Available to all users                   |
| `no_optimization`  | bool       | `false` | Skip image/video optimization            |
| `no_metadata`      | bool       | `false` | Skip metadata extraction                 |
| `allow_encryption` | bool       | `true`  | Allow E2EE uploads                       |
| `allow_anonymous`  | bool       | `true`  | Allow unauthenticated file access        |
| `accept_types`     | string[]?  | —       | Allowed MIME types (null = all)          |
| `max_file_size`    | long?      | —       | Max file size in bytes                   |
| `require_privilege`| int?       | `0`     | Minimum Stellar Program tier             |

---

### QuotaRecord

An extra quota purchase or grant.

| Field        | Type     | Description              |
|--------------|----------|--------------------------|
| `id`         | guid     | Record ID                |
| `account_id` | guid     | User                     |
| `name`       | string   | Label                    |
| `description`| string   | Description              |
| `quota`      | long     | Quota in MiB             |
| `expired_at` | instant? | Expiry (null = permanent)|

---

## Upload Flow Diagrams

### Chunked Upload (> 20MB)

```
Client                          Drive Service                Remote Storage
  |                                  |                            |
  |-- POST /upload/create ---------->|                            |
  |<-- { task_id, chunk_size } -----|                            |
  |                                  |                            |
  |-- POST /upload/chunk/{id}/{0} -->|                            |
  |-- POST /upload/chunk/{id}/{1} -->|                            |
  |-- POST /upload/chunk/{id}/{2} -->|                            |
  |                                  |                            |
  |-- POST /upload/complete/{id} --->|                            |
  |                                  |-- merge chunks ---------->|
  |                                  |-- extract metadata ------>|
  |                                  |-- save to DB ------------>|
  |<-- SnCloudFile -----------------|                            |
  |                                  |-- upload to S3 (async) -->|
```

### Direct Upload (≤ 20MB)

```
Client                          Drive Service                Remote Storage
  |                                  |                            |
  |-- POST /upload/direct --------->|                            |
  |   (multipart: file + metadata)  |                            |
  |                                  |-- process file ---------->|
  |                                  |-- save to DB ------------>|
  |<-- SnCloudFile -----------------|                            |
  |                                  |-- upload to S3 (async) -->|
```

### Deduplication

```
Client                          Drive Service
  |                                  |
  |-- POST /upload/create --------->|
  |   { hash: "abc123" }            |
  |                                  |-- lookup by hash
  |                                  |-- found match!
  |<-- { file_exists: true,         |
  |       file: SnCloudFile } ------|
```

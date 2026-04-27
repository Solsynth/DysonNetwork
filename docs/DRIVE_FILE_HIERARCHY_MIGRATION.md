# Drive File Hierarchy Migration

This document explains the migration from `CloudFileIndex` path indexing to `SnCloudFile` hierarchy.

## Scope

- Service: `DysonNetwork.Drive`
- Model: `DysonNetwork.Shared/Models/CloudFile.cs`
- Migration: `ReplaceFileIndexWithHierarchy`

## Why this change

- Replace path index table with first-class hierarchy on file records.
- Support nested parent relationships using `parent_id`.
- Support folder placeholders (`is_folder=true`) without backing file objects.
- Support multipart-like relationships (for example, live-photo style pairs) using parent-child relations.
- Keep non-indexed uploads hidden from indexed listing until explicitly indexed.

## Breaking changes

## 1) `CloudFileIndex` removed

- Removed model: `SnCloudFileIndex`
- Removed table: `file_indexes`
- Removed controller/service under `/api/index`

If clients call `/api/index/*`, they must migrate to `/api/files/*` hierarchy endpoints.

## 2) Path-based browse removed

- Old listing behavior used string `path`.
- New listing behavior uses direct parent/children traversal.

No API supports chained route segments like `/a/b/c` for listing.

## 3) Upload request field changed

- Old request field: `path`
- New request field: `parent_id`

If `parent_id` is null/empty, uploaded files are not in indexed tree by default (`indexed=false`) and appear in unindexed listing.

## New `SnCloudFile` fields

- `parent_id: string?` - direct parent file/folder ID
- `indexed: bool` - whether file appears in indexed tree listing
- `is_folder: bool` - folder placeholder marker (no file object required)

## API migration map

## Listing and hierarchy

- Old: `GET /api/index/browse?path=/...`
- New: `GET /api/files/root/children`
- New: `GET /api/files/{parentId}/children`

## Unindexed listing

- Old: `GET /api/index/unindexed`
- New: `GET /api/files/unindexed`

## Folder creation

- Old: virtual folders inferred from indexed paths
- New: `POST /api/files/folders`

Request body:

```json
{
  "name": "Vacation",
  "parent_id": "optional-parent-file-id"
}
```

## Move/reindex file

- Old: index move/update endpoints under `/api/index`
- New: `PATCH /api/files/{id}/hierarchy`

Request body:

```json
{
  "parent_id": "new-parent-id-or-null",
  "indexed": true
}
```

## Upload request changes

## Create upload task

- Endpoint unchanged: `POST /api/files/upload/create`
- Field change:
  - remove `path`
  - use `parent_id`

Example:

```json
{
  "hash": "...",
  "file_name": "IMG_0001.HEIC",
  "file_size": 1024000,
  "content_type": "image/heic",
  "pool_id": "...",
  "parent_id": "folder-or-parent-file-id"
}
```

## Direct upload

- Endpoint unchanged: `POST /api/files/upload/direct`
- Form field change:
  - remove `path`
  - use `parent_id`

## Response/DTO impact for clients

When reading files from listing/detail APIs, clients should now handle:

- `parent_id`
- `indexed`
- `is_folder`

Client behavior recommendation:

- Use `is_folder` to render folder rows.
- Use `parent_id` for navigation state.
- Use `indexed=false` results from `/api/files/unindexed` as staging area.

## Data migration behavior

Migration backfills hierarchy from old `file_indexes` with canonicalization:

- For each `(file_id, account_id)`, the latest index row is chosen as canonical.
- Folder placeholders are created for canonical path segments.
- File is linked to leaf folder via `parent_id` and marked `indexed=true`.
- Files without any index row remain `indexed=false`.

## Compatibility summary

- Direct file-by-id retrieval remains supported.
- Path/index APIs are removed and are not backward compatible.
- Client update is required if app used browse/index/move-by-path flows.

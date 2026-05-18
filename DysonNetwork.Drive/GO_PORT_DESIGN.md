# DysonNetwork.Drive — Go Port Design

## Overview

Port of `DysonNetwork.Drive` from C# / .NET 10 to Go. Three-node architecture: **Master** (API), **Storage Node** (media processing + S3 I/O), **Worker** (maintenance). Preserves the external REST + gRPC surface for wire compatibility.

---

## Goals

| Goal | Detail |
|------|--------|
| **API parity** | Every REST route, request body, response shape, and status code identical. Clients unchanged. |
| **gRPC parity** | Same proto definitions, method signatures, error codes. Other services work without modification. |
| **Three-node layout** | Master (API) / Storage Node (processing) / Worker (maintenance) — independent scaling. |
| **Event bus compat** | Same NATS subjects, event schemas, consumer names. |
| **DB compat** | Same PostgreSQL schema, table/column names, JSONB structures. |
| **Drop-in replacement** | Run alongside C# service during migration. |
| **Bundles removed** | Deprecated feature dropped. DB migration removes `bundles` table and `bundle_id` column. |
| **Optimized IDs** | Replace nanoid/dashless-GUID with ULID — time-sortable, 26 chars, zero coordination. |

---

## ID Strategy

### Current (C#)

| Entity | Current ID | Problem |
|--------|-----------|---------|
| `SnCloudFile.Id` | nanoid (21 chars) or dashless GUID (32 chars) | Not sortable, mixed schemes, 32-char default is wasteful |
| `SnFileObject.Id` | Same as `SnCloudFile.Id` | Coupled — fine, but ID format is inconsistent |
| `PersistentTask.TaskId` | nanoid (21 chars) | Not sortable, no temporal ordering |
| `SnFileReplica.Id` | `Guid.NewGuid()` (36 chars with dashes) | UUIDv4 — not sortable, large index |
| `SnFilePermission.Id` | `Guid.NewGuid()` | Same |
| `FilePool.Id` | `Guid.NewGuid()` | Same |
| `QuotaRecord.Id` | `Guid.NewGuid()` | Same |

### Proposed (Go)

All IDs become **ULID** (Universally Unique Lexicographically Sortable Identifier):

```
01ARZ3NDEKTSV4RRFFQ69G5FAV
├── 10 chars: timestamp (48-bit ms precision, sortable)
└── 16 chars: randomness (80-bit)
```

| Entity | New ID | Format | Storage |
|--------|--------|--------|---------|
| `files.id` | ULID | `char(26)` PK | Same as file_objects.id |
| `file_objects.id` | ULID | `char(26)` PK | Same as files.id |
| `file_replicas.id` | ULID | `char(26)` PK | Replaces UUID |
| `file_permissions.id` | ULID | `char(26)` PK | Replaces UUID |
| `pools.id` | ULID | `char(26)` PK | Replaces UUID |
| `tasks.id` | ULID | `char(26)` PK | Replaces UUID |
| `tasks.task_id` | ULID | `char(26)` | Replaces nanoid |
| `quota_records.id` | ULID | `char(26)` PK | Replaces UUID |

**Why ULID over nanoid/UUID:**
- **Time-sortable**: first 10 chars encode timestamp → B-tree index inserts are append-only, no random page splits
- **Compact**: 26 chars vs 32 (dashless GUID) or 36 (UUID)
- **No coordination**: 80-bit randomness = collision risk is negligible
- **DB friendly**: `ORDER BY id` gives chronological order for free
- **String-safe**: uses Crockford's Base32 (no ambiguous chars: 0/O, 1/I/L)

**Migration**: New rows get ULID. Existing rows keep their old IDs. The `char(32)` columns can be altered to `char(26)` or left as-is (ULID fits in 32). A one-time migration script can convert existing IDs if desired, but it's not required — both formats are valid strings.

---

## Architecture

```
                            ┌──────────────────────────────────────────────┐
                            │               Master (API Node)              │
                            │                                              │
  Clients ──── REST ──────▶ │  HTTP Handlers  ──▶  Service Layer           │
  Other     ──── gRPC ────▶ │  gRPC Handlers  ──▶  ┌───────────────────┐  │
  Services                  │                     │  FileService       │  │
                            │                     │  TaskService       │  │
                            │                     │  QuotaService      │  │
                            │                     │  PoolService       │  │
                            │                     └────────┬──────────┘  │
                            │                              │              │
                            │                    PostgreSQL │ Redis       │
                            │                    NATS publish│             │
                            └──────────────────────────────┼──────────────┘
                                                           │
                                                NATS JetStream
                                                           │
                            ┌──────────────────────────────┼───────────────────┐
                            │          Storage Node (File Processor)          │
                            │                                                  │
                            │  Subscribes: file_events / file_uploaded         │
                            │                                                  │
                            │  Pipeline:                                       │
                            │  1. Read temp file from shared storage           │
                            │  2. Extract metadata (vips / ffprobe)            │
                            │  3. Image: BlurHash → WebP → compressed          │
                            │  4. Video: thumbnail via ffmpeg                  │
                            │  5. Upload original + variants to S3             │
                            │  6. Update DB (meta, has_compression, etc.)      │
                            │  7. Mark task complete, send notifications       │
                            │  8. Clean up temp files                          │
                            │                                                  │
                            │  PostgreSQL │ Redis │ S3/Minio │ NATS            │
                            └──────────────────────────────────────────────────┘

                            ┌──────────────────────────────────────────────┐
                            │              Worker (Maintenance)              │
                            │                                              │
                            │  Cron jobs (no NATS subscription):            │
                            │  ├─ File reanalysis loop (10s interval)       │
                            │  ├─ Orphaned object cleanup (daily 1 AM)      │
                            │  ├─ Unused/expired file recycling (midnight)  │
                            │  ├─ Soft-delete purging (daily, 7-day cutoff) │
                            │  └─ Stale task cleanup (daily, 30-day cutoff) │
                            │                                              │
                            │  NATS subscriber:                             │
                            │  └─ account_events / AccountDeletedEvent      │
                            │     → bulk soft-delete all files for account  │
                            │                                              │
                            │  PostgreSQL │ S3/Minio │ NATS                 │
                            └──────────────────────────────────────────────┘
```

### Master (API Node)

Stateless HTTP + gRPC server. All client-facing logic.

**REST API:**
- Files: CRUD, hierarchy, permissions, download, info
- Upload: create task, accept chunks, direct upload, complete, progress
- Pools: list usable pools (credentials stripped), recycle pool files
- Billing: quota details, quota records, usage per pool/total

**gRPC:** `DyFileService` — GetFile, GetFileBatch, UpdateFile, DeleteFile, PurgeCache, SetFilePublic, UnsetFilePublic

**Internal:**
- JWT auth + permission checks
- Quota enforcement before upload
- Redis caching (files 15min, tasks 30min, pools)
- Chunked upload chunk storage (local disk or S3 staging)
- DB writes via pgx
- Publishes `FileUploadedEvent` to NATS after DB save

**Does NOT** do: image processing, video processing, S3 uploads, metadata extraction, background cleanup.

### Storage Node (File Processor)

CPU/I/O intensive processing. Subscribes to NATS `file_events`.

**Single responsibility**: process uploaded files.

Pipeline per `FileUploadedEvent`:
1. Access temp file (shared volume or download from staging)
2. Detect MIME category (image/video/audio/other)
3. **Image path**: Extract EXIF (strip GPS), compute BlurHash, generate lossless WebP, create compressed variant if >1MP, update metadata
4. **Video path**: FFProbe for stream info, ffmpeg thumbnail at frame 0, update metadata
5. Upload original + `.compressed` + `.thumbnail` to S3 via Minio
6. Update `file_objects.meta` (jsonb), `has_compression`, `has_thumbnail`
7. Update `files.uploaded_at`
8. Mark task completed, fire WebSocket + push notifications
9. Delete temp file

**Why separate from Worker**: Media processing is CPU-bound (vips, ffmpeg) and I/O-heavy (S3). Mixing it with lightweight cron jobs would cause processing spikes to starve maintenance tasks. Separate scaling: add more Storage Nodes when upload volume increases, keep Workers at 1-2 replicas.

### Worker (Maintenance)

Lightweight background jobs. No NATS subscription needed except for account deletion.

| Job | Schedule | What |
|-----|----------|------|
| File reanalysis | Continuous (10s delay) | Find files missing metadata, download from S3, extract, validate compression/thumbnail flags |
| Orphan cleanup | Daily 1 AM | Find `file_objects` with no `files` reference, delete from S3 + DB |
| Unused recycling | Daily midnight | Mark unindexed files without replicas for recycling; mark expired files |
| Soft-delete purge | Daily midnight | Hard-delete records with `deleted_at` older than 7 days |
| Task cleanup | Daily 2 AM | Delete completed/failed/cancelled/expired tasks older than 30 days |
| Account deletion | NATS event | Soft-delete all files for deleted account |

---

## Project Structure

```
drive/
├── cmd/
│   ├── master/
│   │   └── main.go                  # Master entrypoint (HTTP + gRPC)
│   ├── storage/
│   │   └── main.go                  # Storage node entrypoint (NATS subscriber + processor)
│   └── worker/
│       └── main.go                  # Worker entrypoint (cron + NATS subscriber)
│
├── internal/
│   ├── config/
│   │   └── config.go                # Shared config (env/yaml)
│   │
│   ├── database/
│   │   ├── db.go                    # pgx pool, migrations
│   │   ├── models.go                # All DB models (snake_case JSON tags)
│   │   ├── queries.go               # Shared query helpers
│   │   └── id.go                    # ULID generation wrapper
│   │
│   ├── cache/
│   │   └── redis.go                 # Redis client wrapper
│   │
│   ├── storage/
│   │   ├── s3.go                    # Minio/S3 client (upload, delete, signed URLs)
│   │   ├── upload.go                # Chunk management (local filesystem)
│   │   └── processor.go            # Media processing pipeline (vips, ffprobe, ffmpeg)
│   │
│   ├── eventbus/
│   │   ├── nats.go                  # NATS connection + JetStream helpers
│   │   └── events.go                # Event type definitions + serialization
│   │
│   ├── auth/
│   │   └── auth.go                  # JWT validation, permission checks
│   │
│   ├── api/
│   │   ├── router.go                # Chi router setup + route registration
│   │   ├── middleware.go            # Auth, logging, CORS
│   │   ├── files.go                 # /api/files handlers
│   │   ├── upload.go                # /api/files/upload handlers
│   │   ├── pools.go                 # /api/pools handlers
│   │   ├── billing.go               # /api/billing handlers
│   │   └── responses.go             # Response helpers (snake_case JSON, error codes)
│   │
│   ├── grpc/
│   │   └── file_service.go          # DyFileService gRPC implementation
│   │
│   ├── service/
│   │   ├── file.go                  # File business logic
│   │   ├── task.go                  # Persistent task management
│   │   ├── quota.go                 # Quota calculation
│   │   ├── usage.go                 # Usage aggregation
│   │   ├── pool.go                  # Pool management
│   │   └── encryptor.go             # E2EE envelope encrypt/decrypt
│   │
│   └── handlers/
│       ├── upload_processor.go      # NATS: FileUploadedEvent → media pipeline
│       └── account_deletion.go      # NATS: AccountDeletedEvent → bulk soft-delete
│
├── proto/                           # Generated gRPC code (from DysonSpec)
├── go.mod
├── go.sum
├── Dockerfile.master
├── Dockerfile.storage
├── Dockerfile.worker
└── docker-compose.yml
```

---

## REST API Contract (unchanged)

Every route below matches the C# implementation exactly. Request bodies, query params, response shapes, status codes, and `X-Total` headers must be identical.

### Files (`/api/files`)

```
GET    /api/files/{id}                        # Open/download file
GET    /api/files/{id}/e2ee                   # Get E2EE metadata
GET    /api/files/{id}/access                 # Serve local file via HMAC token
GET    /api/files/{id}/info                   # File info (no auth)
GET    /api/files/{id}/references             # Files sharing same object
PATCH  /api/files/{id}/name                   # Update file name
PUT    /api/files/{id}/marks                  # Update sensitive marks
PUT    /api/files/{id}/meta                   # Update user metadata
GET    /api/files/root/children               # Root-level indexed files
GET    /api/files/{parentId}/children         # Folder children
POST   /api/files/folders                     # Create folder
GET    /api/files/unindexed                   # Unindexed files
PATCH  /api/files/{id}/hierarchy              # Move file / toggle indexed
GET    /api/files/me                          # List user's files
POST   /api/files/batches/delete              # Batch delete
DELETE /api/files/{id}                        # Delete single file
DELETE /api/files/me/recycle                  # Delete user's recycled files
DELETE /api/files/recycle                     # Delete all recycled (admin)
```

### Upload (`/api/files/upload`)

```
POST   /api/files/upload/create               # Create chunked upload task
POST   /api/files/upload/direct               # Direct upload (≤20MB, multipart form)
POST   /api/files/upload/chunk/{taskId}/{idx} # Upload single chunk
POST   /api/files/upload/complete/{taskId}    # Complete upload
GET    /api/files/upload/tasks                # List upload tasks
GET    /api/files/upload/progress/{taskId}    # Upload progress
GET    /api/files/upload/resume/{taskId}      # Resume info
DELETE /api/files/upload/task/{taskId}        # Cancel upload
GET    /api/files/upload/stats                # Upload statistics
DELETE /api/files/upload/tasks/cleanup        # Cleanup failed tasks
GET    /api/files/upload/tasks/recent         # Recent tasks
GET    /api/files/upload/tasks/{taskId}/details  # Task details
```

### Pools (`/api/pools`)

```
GET    /api/pools                             # List usable pools (credentials hidden)
DELETE /api/pools/{id}/recycle                # Delete recycled files in pool
```

### Billing (`/api/billing`)

```
GET    /api/billing/quota                     # Quota details
GET    /api/billing/quota/records             # Quota records
GET    /api/billing/usage                     # Total usage (cached 5min)
GET    /api/billing/usage/{poolId}            # Pool usage
```

### Removed Endpoints (bundles deprecated)

```
- GET    /api/bundles/{id}
- GET    /api/bundles/me
- POST   /api/bundles
- PUT    /api/bundles/{id}
- DELETE /api/bundles/{id}
```

These return **410 Gone** during migration if old clients call them.

---

## gRPC Contract

Implement `DyFileService` from `DysonSpec/proto/`. Generated Go code in `proto/`.

| Method | Behavior |
|--------|----------|
| `GetFile(DyGetFileRequest) → DyCloudFile` | Lookup by ID, `codes.NotFound` if missing |
| `GetFileBatch(DyGetFileBatchRequest) → DyGetFileBatchResponse` | Batch lookup |
| `UpdateFile(DyUpdateFileRequest) → DyCloudFile` | Partial update via field mask |
| `DeleteFile(DyDeleteFileRequest) → Empty` | Delete file + data |
| `PurgeCache(DyPurgeCacheRequest) → Empty` | Evict Redis cache key |
| `SetFilePublic(DySetFilePublicRequest) → Empty` | Add `Anyone/Read` permission |
| `UnsetFilePublic(DyUnsetFilePublicRequest) → Empty` | Remove public, add owner-only |

Settings: `MaxRecvMsgSize = 16MB`, `MaxSendMsgSize = 16MB`, reflection enabled.

---

## Database Schema

PostgreSQL with `snake_case` naming. Use `pgx` directly or `sqlc` for type-safe queries.

### Tables (bundles removed)

| Table | Key Columns |
|-------|-------------|
| `files` | `id` (ULID, PK), `name`, `account_id`, `object_id`, `parent_id`, `indexed`, `is_folder`, `is_marked_recycle`, `expired_at`, `uploaded_at`, `storage_id`, `storage_url`, `file_meta` (jsonb), `user_meta` (jsonb), `usage`, `application_type`, `deleted_at` |
| `file_objects` | `id` (ULID, PK), `size`, `mime_type`, `hash`, `meta` (jsonb), `has_compression`, `has_thumbnail` |
| `file_replicas` | `id` (ULID, PK), `object_id`, `pool_id`, `storage_id`, `status`, `is_primary` |
| `file_permissions` | `id` (ULID, PK), `file_id`, `subject_type`, `subject_id`, `permission` |
| `pools` | `id` (ULID, PK), `name`, `storage_config` (jsonb), `billing_config` (jsonb), `policy_config` (jsonb), `is_hidden`, `account_id` |
| `tasks` | `id` (ULID, PK), `task_id` (ULID), `name`, `type`, `status`, `account_id`, `progress`, `parameters` (jsonb), `results` (jsonb), `error_message`, `priority`, `last_activity` |
| `quota_records` | `id` (ULID, PK), `account_id`, `name`, `quota` (MiB), `expired_at` |

### Removed

- `bundles` table — dropped
- `files.bundle_id` column — dropped
- `ix_files_bundle_id` index — dropped
- `fk_files_bundles_bundle_id` foreign key — dropped

### Indexes (must match)

- `files`: `parent_id`; `(account_id, parent_id, indexed, deleted_at)`; `(account_id, indexed, is_marked_recycle, deleted_at)`
- Soft-delete global filter on all queries (skip rows where `deleted_at IS NOT NULL`)

---

## NATS Event Bus

### Published (by Master)

| Stream | Subject | Event | When |
|--------|---------|-------|------|
| `file_events` | `file_uploaded` | `FileUploadedEvent` | After file saved to DB |

### Subscribed (by Storage Node)

| Stream | Consumer | Event | Handler |
|--------|----------|-------|---------|
| `file_events` | `drive_file_uploaded_handler` | `FileUploadedEvent` | Media pipeline: extract → optimize → S3 upload → DB update |

### Subscribed (by Worker)

| Stream | Consumer | Event | Handler |
|--------|----------|-------|---------|
| `account_events` | `drive_account_deleted_handler` | `AccountDeletedEvent` | Bulk soft-delete all files for account |

Consumer config: `AckWait=30s`, `MaxDeliver=3`, `DeliverAll`.

---

## Node Boundaries

### Master on upload

```
POST /create
  → validate request
  → check quota
  → create PersistentUploadTask in DB
  → return { task_id, chunk_size, chunks_count }

POST /chunk/{taskId}/{idx}
  → save chunk to disk: {tmp}/multipart-uploads/{taskId}/{idx}
  → update Parameters.UploadedChunks in DB
  → send WebSocket progress

POST /complete/{taskId}
  → merge chunks into single file
  → detect MIME type
  → create file_objects + files + file_replicas rows (ULID IDs)
  → hash file (MD5 <5GB, approximate ≥5GB)
  → publish FileUploadedEvent to NATS
  → return 200

POST /direct (≤20MB)
  → same as complete but single file, no chunks
```

### Storage Node on FileUploadedEvent

```
1. Access temp file at ProcessingFilePath
2. Switch on MIME type:
   image/*:
     → vips: extract EXIF (strip GPS), get dimensions/orientation
     → blurhash: compute BlurHash (3x3)
     → if >1MP: vips → lossless WebP + compressed variant
     → upload original + .compressed to S3
     → update file_objects.meta, has_compression
   video/* / audio/*:
     → ffprobe: streams, duration, bitrate, format
     → ffmpeg: thumbnail at frame 0
     → upload original + .thumbnail to S3
     → update file_objects.meta, has_thumbnail
   other:
     → upload original to S3
3. Update files.uploaded_at
4. Mark task completed (WebSocket + push notification)
5. Delete temp file
```

### Worker on cron

```
Every 10s:
  → find files with missing metadata (no mime_type, empty meta, zero size, null hash)
  → download from S3, extract metadata, validate compression/thumbnail flags

Daily midnight:
  → mark unindexed files without replicas for recycling
  → mark expired files for recycling
  → hard-delete soft-deleted records older than 7 days

Daily 1 AM:
  → find orphaned file_objects (no files reference them)
  → delete from S3 + DB

Daily 2 AM:
  → delete completed/failed/cancelled/expired tasks older than 30 days
```

---

## Go Dependencies

| Purpose | Package |
|---------|---------|
| HTTP router | `github.com/go-chi/chi/v5` |
| gRPC | `google.golang.org/grpc` |
| PostgreSQL | `github.com/jackc/pgx/v5` |
| Redis | `github.com/redis/go-redis/v9` |
| NATS | `github.com/nats-io/nats.go` |
| S3/Minio | `github.com/minio/minio-go/v7` |
| Image processing | `github.com/davidbyttow/govips/v2` (cgo, wraps libvips) |
| Video probing | `github.com/xfrr/goffmpeg` or shell `ffprobe` |
| BlurHash | `github.com/buckket/go-blurhash` |
| ULID | `github.com/oklog/ulid/v2` |
| JWT | `github.com/golang-jwt/jwt/v5` |
| BCrypt | `golang.org/x/crypto/bcrypt` |
| MIME detection | `github.com/gabriel-vasile/mimetype` |
| Cron | `github.com/robfig/cron/v3` |
| Config | `github.com/spf13/viper` |
| Logging | `log/slog` (stdlib) |
| Migration | `github.com/golang-migrate/migrate/v4` |

Removed: `NanoidDotNet` (replaced by ULID), bundle-related packages.

---

## Deployment

### Docker Compose

```yaml
services:
  master:
    build:
      context: .
      dockerfile: Dockerfile.master
    ports:
      - "8080:8080"   # HTTP
      - "9090:9090"   # gRPC
    depends_on:
      - postgres
      - redis
      - nats
      - minio

  storage:
    build:
      context: .
      dockerfile: Dockerfile.storage
    depends_on:
      - postgres
      - redis
      - nats
      - minio
    volumes:
      - upload-staging:/tmp/multipart-uploads  # shared with master
    deploy:
      replicas: 2

  worker:
    build:
      context: .
      dockerfile: Dockerfile.worker
    depends_on:
      - postgres
      - nats
      - minio
    deploy:
      replicas: 1     # maintenance is light, 1-2 is enough

  postgres:
    image: postgres:17

  redis:
    image: redis:7

  nats:
    image: nats:2-jetstream

  minio:
    image: minio/minio

volumes:
  upload-staging:
```

### Kubernetes

| Node | Deployment | Scaling |
|------|-----------|---------|
| Master | Deployment + Service (Ingress) | CPU/RPS based HPA |
| Storage | Deployment | NATS consumer pending messages |
| Worker | Deployment | Fixed 1-2 replicas |

---

## Migration Strategy

### Phase 1: Schema migration (C# compatible)

1. Migration: drop `bundles` table, drop `files.bundle_id` column
2. Migration: alter ID columns from `varchar(32)` to `char(26)` (optional, ULID fits in existing 32-char columns)
3. C# service still works — it just ignores missing bundles, and ULID strings are valid for existing nanoid fields

### Phase 2: Run alongside

1. Deploy Go master + storage + worker alongside C#
2. Both share PostgreSQL, Redis, NATS, S3
3. Route percentage of traffic to Go master via gateway
4. Compare responses, latency, error rates

### Phase 3: Cutover

1. Route 100% to Go master
2. Stop C# master
3. Verify storage node processing all events
4. Verify worker cron jobs running

### Phase 4: Cleanup

1. Remove C# service
2. Update CI/CD
3. Remove bundle references from other services (if any)

---

## Performance Advantages

| Area | C# (current) | Go (proposed) |
|------|---------------|---------------|
| Upload processing | BackgroundService (single loop) | Storage node goroutine pool, independent scaling |
| Chunk merge | Parallel read (2 concurrent) | `io.Pipe` + goroutine pool, zero-copy to S3 |
| S3 upload | Minio .NET SDK (buffered) | `minio-go` with `io.Reader` streaming |
| Metadata extraction | P/Invoke to libvips | cgo to libvips (faster FFI) |
| Image pipeline | Synchronous in event handler | Pipeline: goroutine per stage |
| Background jobs | Quartz (in-process, competes with API) | Separate worker binary, no resource contention |
| ID generation | nanoid (async) or GUID | ULID (no crypto randomness, time-sortable) |
| Memory | ASP.NET + EF Core overhead | pgx pool + minimal allocations |
| Deployment | Single monolith | 3 binaries, independent scaling per role |
| Index performance | Random GUID/nanoid inserts | ULID = append-only B-tree inserts |

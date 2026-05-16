# Shared Cache Contract

## Overview

DysonGo and DysonNetwork now share a cache contract for auth-related data.
The goal is to let both runtimes read and write the same Redis entries without
binding the cache layer to protobuf-specific storage.

## Key Rules

- Shared keys use the `dyson:` prefix.
- Shared structured data is stored in a Redis hash.
- Simple throttle/marker values use a plain Redis string.
- Redis TTL is the source of truth for expiration.
- `cached_at` is metadata only.

## Shared Key Layout

Structured data:

```redis
HSET dyson:auth:session:abc123 \
  data "{json}" \
  codec "json" \
  type "DyAuthSession" \
  cached_at "1747382000"

EXPIRE dyson:auth:session:abc123 3600
```

Flags:

```redis
SET dyson:auth:last_seen_touch:abc123 "1"
EXPIRE dyson:auth:last_seen_touch:abc123 60
```

## Data Kinds

### Shared structured data

Use `SetData` / `GetData` for values that should be shared between Go and C#.
Current auth use cases:

- `auth:session:{id}`
- `auth:profile:{id}`

These entries store a JSON payload in the `data` field and use the `type`
field as a soft identifier.

### Simple flags

Use `SetFlagAsync` / `HasFlagAsync` for throttle or marker values.
Current auth use case:

- `auth:last_seen_touch:{id}`

These entries only store the string `"1"` and rely on TTL for expiration.

## API Naming

The shared cache API uses neutral names:

- `SetData`
- `GetData`
- `SetFlagAsync`
- `HasFlagAsync`
- `RemoveFlagAsync`

The Go cache package mirrors the same naming.

## Error Handling

Cache reads should fail closed and behave like a miss when:

- the key is missing
- the codec is not expected
- the payload cannot be parsed
- the type does not match the caller expectation

Auth should continue to work even when cache entries are malformed.

## Auth Flow Usage

### Session cache

1. Extract token.
2. Derive session cache key from `jti` or `sid`.
3. Read shared cached session.
4. On miss, validate via auth service.
5. Store session back into shared cache.

### Profile cache

1. Read profile from shared cache.
2. On miss, hydrate from profile service.
3. Store profile back into shared cache.

### Last seen throttle

1. Check the flag key.
2. Skip if already touched recently.
3. Update `last_seen_at`.
4. Set the flag with a short TTL.

## Files Involved

Go:

- `/Users/littlesheep/Documents/Projects/DysonGo/pkg/cache/cache.go`
- `/Users/littlesheep/Documents/Projects/DysonGo/pkg/cache/redis.go`
- `/Users/littlesheep/Documents/Projects/DysonGo/pkg/cache/memory.go`
- `/Users/littlesheep/Documents/Projects/DysonGo/pkg/auth/cached.go`
- `/Users/littlesheep/Documents/Projects/DysonGo/pkg/auth/profile.go`

C#:

- `/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Shared/Cache/ISharedCacheService.cs`
- `/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Shared/Cache/SharedProtoCacheService.cs`
- `/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Shared/Auth/AuthScheme.cs`

## Migration Notes

- Existing cache entries written with the old serializer are not part of this contract.
- Only auth-related cache entries are expected to be shared initially.
- Other services may keep using `ICacheService` for local cache use cases.

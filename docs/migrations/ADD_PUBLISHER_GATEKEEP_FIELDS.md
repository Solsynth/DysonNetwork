# Migration: AddPublisherGatekeepFields (Client-Side)

**Date**: 2026-04-04
**Migration File**: `20260404151939_AddPublisherGatekeepFields.cs`

## Summary

This migration moves `followRequiresApproval` and `postsRequireFollow` feature flags from a separate `PublisherFeatures` table to direct columns on the `Publishers` table. **The API contract is unchanged** - no modifications are required for most clients.

## API Changes: None Required

All existing API endpoints continue to work exactly as before:

| Endpoint | Method | Behavior |
|----------|--------|----------|
| `/api/publishers/{name}/features` | GET | Returns feature flags - **unchanged** |
| `/api/publishers/{name}/features` | POST | Enable a feature flag - **unchanged** |
| `/api/publishers/{name}/features/{flag}` | DELETE | Disable a feature flag - **unchanged** |
| `/api/publishers/{name}/subscription` | GET | Check subscription/follow status - **unchanged** |
| `/api/publishers/{name}/subscribe` | POST | Subscribe or submit follow request - **unchanged** |
| `/api/publishers/{name}/unsubscribe` | POST | Cancel subscription/follow - **unchanged** |
| `/api/publishers/{name}/subscription/requests` | GET | List pending follow requests - **unchanged** |
| `/api/publishers/{name}/subscription/requests/{id}/approve` | POST | Approve follow request - **unchanged** |
| `/api/publishers/{name}/subscription/requests/{id}/reject` | POST | Reject follow request - **unchanged** |

## Response Format

### GET /api/publishers/{name}/features

**Response** (unchanged):
```json
{
    "followRequiresApproval": true,
    "postsRequireFollow": false
}
```

## What Changed Internally?

| Before | After |
|--------|-------|
| Feature flags stored in `PublisherFeatures` table | Flags stored as columns on `Publishers` table |
| Required JOIN to check flags | Direct column access |
| Complex caching strategy | Simpler caching via `publisher.IsGatekept` |

## If You Were Using Database Direct Access

If your client was directly querying the database to check feature flags, you should now query the `publishers` table instead:

### Before ( querying `publisher_features`)

```sql
SELECT EXISTS(
    SELECT 1 FROM publisher_features
    WHERE publisher_id = 'xxx'
    AND flag = 'followRequiresApproval'
    AND (expired_at IS NULL OR expired_at > NOW())
);
```

### After ( query `publishers`)

```sql
SELECT moderate_subscription FROM publishers WHERE id = 'xxx';
```

## If You Were Caching Feature Flags

If you implemented client-side caching of feature flags, you may need to update your cache keys or TTL:

| Flag | Old Cache Key Pattern | New Cache Key Pattern |
|------|----------------------|----------------------|
| `followRequiresApproval` | `publisher:feature:{id}:followRequiresApproval` | Same |
| `postsRequireFollow` | `publisher:feature:{id}:postsRequireFollow` | Same |

Cache behavior remains the same (5-minute TTL), but the underlying data source is now faster.

## Affected Flag Names

These flag names are still used in the API for backward compatibility:

| Flag Name | Description |
|-----------|-------------|
| `followRequiresApproval` | Require approval before users can follow |
| `postsRequireFollow` | Only show posts to accepted followers |

## Subscription Status Changes

When `followRequiresApproval` is enabled, the subscription flow changes:

1. User calls `POST /api/publishers/{name}/subscribe`
2. Server creates a follow request (state: `Pending`) instead of a subscription
3. Publisher managers approve via `POST .../approve`
4. Server creates a subscription after approval

### Subscription Status Values

| Status | Meaning |
|--------|---------|
| `none` | No subscription or follow request |
| `pending` | Follow request awaiting approval |
| `following` | Follow request approved, now following |
| `subscribed` | Direct subscription (no approval needed) |
| `rejected` | Follow request was rejected |

## Migration Checklist for Clients

- [ ] Verify API responses match expected format
- [ ] Update any database queries that directly accessed `publisher_features` for these flags
- [ ] Clear client-side caches if you cache feature flag data
- [ ] Test follow request flow (submit → approve/reject)
- [ ] Test that posts are hidden from non-followers when `postsRequireFollow` is enabled

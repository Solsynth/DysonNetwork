# Publishing Settings

Allows users to configure default publishers for different operations (posting, replying, and Fediverse activities like following and reacting).

> **Note:** All API responses use snake_case field names (e.g., `default_posting_publisher_id` instead of `defaultPostingPublisherId`).

## Base URL

```
/api/account/publishing
```

## Related Endpoint: Check Fediverse Availability

Before setting `default_fediverse_publisher_id`, you may want to check which publishers the user has Fediverse enabled:

```
GET /api/fediverse/actors/availability
```

**Response:** `200 OK`
```json
{
  "is_enabled": true,
  "publishers": [
    {
      "publisher_id": "uuid",
      "publisher_name": "my_publisher",
      "fediverse_handle": "@username@mastodon.social",
      "fediverse_uri": "https://mastodon.social/@username",
      "avatar_url": "https://...",
      "is_enabled": true,
      "followers_count": 100,
      "following_count": 50,
      "posts_count": 200
    }
  ]
}
```

This endpoint returns all publishers owned by the user that have Fediverse enabled. Use this to validate `default_fediverse_publisher_id` values.

---

## Data Model

### SnPublishingSettings

| Field | Type | Description |
|-------|------|-------------|
| `id` | UUID | Primary key |
| `account_id` | UUID | Owner account |
| `default_posting_publisher_id` | UUID? | Default publisher for creating posts |
| `default_reply_publisher_id` | UUID? | Default publisher for replies |
| `default_fediverse_publisher_id` | UUID? | Default publisher with Fediverse actor for follows, reactions, boosts |
| `created_at` | Instant | Creation timestamp |
| `updated_at` | Instant? | Last update timestamp |

## Endpoints

### Get Settings

Retrieve current account settings.

```
GET /api/account/publishing
```

**Response:** `200 OK`
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "account_id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "default_posting_publisher_id": "3fa85f64-5717-4562-b3fc-2c963f66afa7",
  "default_reply_publisher_id": "3fa85f64-5717-4562-b3fc-2c963f66afa8",
  "default_fediverse_publisher_id": "3fa85f64-5717-4562-b3fc-2c963f66afa9",
  "created_at": "2026-04-01T00:00:00Z",
  "updated_at": "2026-04-07T00:00:00Z"
}
```

**Notes:**
- If no settings exist, a new record is automatically created with all fields set to `null`

---

### Update Settings

Update account settings.

```
PATCH /api/account/publishing
```

**Request Body:**
```json
{
  "default_posting_publisher_id": "3fa85f64-5717-4562-b3fc-2c963f66afa7",
  "default_reply_publisher_id": "3fa85f64-5717-4562-b3fc-2c963f66afa8",
  "default_fediverse_publisher_id": "3fa85f64-5717-4562-b3fc-2c963f66afa9"
}
```

All fields are optional. Only provided fields are updated.

**Validation Rules:**

1. **Membership Check:** User must be a member of any publisher they set as default
2. **Fediverse Validation:** If `default_fediverse_publisher_id` is set, the publisher **must** have Fediverse enabled (an active actor). Use `GET /api/fediverse/actors/availability` to get valid options.

**Error Responses:**

| Status | Condition |
|--------|-----------|
| `400 Bad Request` | Publisher not found |
| `400 Bad Request` | User is not a member of the specified publisher |
| `400 Bad Request` | Publisher does not have Fediverse enabled (for `default_fediverse_publisher_id`) |

---

## Behavior

### Publisher Selection Priority

1. **Explicit Parameter:** If operation supports an explicit publisher parameter (e.g., `?pub=publisherName`), that takes precedence
2. **Default Settings:** If configured and valid, use the corresponding default publisher
3. **Fallback:** Use the first matching publisher (original behavior)

### Operations Using Default Publishers

| Operation | Default Setting Used | Fallback Behavior |
|-----------|---------------------|-------------------|
| Create Post | `default_posting_publisher_id` | First individual publisher owned by user |
| Reply to Post | `default_reply_publisher_id` | First individual publisher owned by user |
| Follow Remote User | `default_fediverse_publisher_id` | First publisher with Fediverse actor |
| Unfollow Remote User | `default_fediverse_publisher_id` | First publisher with Fediverse actor |
| Boost Post | `default_fediverse_publisher_id` | First publisher with Fediverse actor |
| Unboost Post | `default_fediverse_publisher_id` | First publisher with Fediverse actor |
| React to Post | `default_fediverse_publisher_id` | First publisher with Fediverse actor |

### Example Scenarios

**Scenario 1: Personal and Organization Identity**

User has:
- `@john_personal` - Individual publisher (Fediverse enabled)
- `@john_company` - Organization publisher (Fediverse enabled)

User wants to:
- Post personal content as `@john_personal`
- Reply to others as `@john_company`
- Follow/boost as `@john_company`

Settings:
```json
{
  "default_posting_publisher_id": "<id_of_john_personal>",
  "default_reply_publisher_id": "<id_of_john_company>",
  "default_fediverse_publisher_id": "<id_of_john_company>"
}
```

**Scenario 2: Single Publisher**

User has only one publisher with Fediverse enabled. No settings needed - the system automatically uses it.

**Scenario 3: Disabled Fediverse Publisher**

If user sets `default_fediverse_publisher_id` to a publisher that later disables Fediverse:
- Future Fediverse operations will fall back to the first available publisher with Fediverse actor
- The invalid setting is not automatically cleared

---

## Database

### Table: `account_settings`

```sql
CREATE TABLE account_settings (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ,
    deleted_at TIMESTAMPTZ,
    account_id UUID NOT NULL UNIQUE,
    default_posting_publisher_id UUID REFERENCES publishers(id) ON DELETE SET NULL,
    default_reply_publisher_id UUID REFERENCES publishers(id) ON DELETE SET NULL,
    default_fediverse_publisher_id UUID REFERENCES publishers(id) ON DELETE SET NULL
);

CREATE INDEX ix_account_settings_account_id ON account_settings(account_id);
```

### Relationships

- `default_posting_publisher_id` → `publishers.id` (SET NULL on delete)
- `default_reply_publisher_id` → `publishers.id` (SET NULL on delete)
- `default_fediverse_publisher_id` → `publishers.id` (SET NULL on delete)

---

## Migration

A migration is required to add the new table:

```bash
dotnet ef migrations add AddAccountSettings --project DysonNetwork.Sphere --output-dir Migrations
dotnet ef database update --project DysonNetwork.Sphere
```

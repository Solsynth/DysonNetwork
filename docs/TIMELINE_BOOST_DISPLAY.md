# Timeline Boost Display

## Overview

Posts boosted by Fediverse actors that you follow now appear in your timeline. This document explains how to identify and display boosted posts in your client application.

## What is a Boosted Post?

A boosted post is a post originally created by another actor that has been shared/announced by a Fediverse actor you follow. When viewing a timeline, you may encounter posts that were boosted rather than originally authored.

## Boost Sources

There are two types of boosts that appear in your timeline:

### Fediverse Boosts (Remote)

Posts boosted by remote Fediverse actors you follow via the Fediverse network.

### On-Site Boosts (Local)

Posts boosted by on-site users who publish to your network. This happens when:

1. A local user with a publisher boosts a post
2. The publisher has an associated FediverseActor
3. You follow that publisher

Both boost types use the same `boosted_by` mechanism in the API response.

## How to Identify Boosted Posts

### API Response

In the timeline response, boosted posts have the `boosted_by` field populated:

```json
{
  "id": "post-uuid",
  "content": "Original post content...",
  "author": {
    "id": "author-uuid",
    "username": "original_author",
    "display_name": "Original Author"
  },
  "boosted_by": {
    "id": "booster-uuid",
    "username": "booster",
    "display_name": "Booster Name",
    "avatar_url": "https://..."
  },
  "boosted_at": "2026-03-27T10:30:00Z"
}
```

### Fields Added for Boost Display

| Field | Type | Description |
|-------|------|-------------|
| `boosted_by` | object | The Fediverse actor who boosted this post. `null` if originally authored. |
| `boosted_at` | datetime | When the boost occurred. `null` if originally authored. |

## Client Display Guidelines

### Basic Display Rules

1. **Show boost indicator** - Display "Boosted by @username" when `boosted_by` is present
2. **Show original author** - Always display the post's actual author in `author` field
3. **Use `boosted_at` for sorting** - If sorting by recency, use `boosted_at` for boosted posts

### UI/UX Suggestions

#### Boost Indicator

```
┌─────────────────────────────────────┐
│ 🔁 Boosted by @booster@mastodon.social
│ ┌─────────────────────────────────┐ │
│ │ @original_author · 2h ago       │ │
│ │ Original post content here...     │ │
│ │                                   │ │
│ │ ❤️ 42  💬 8                      │ │
│ └─────────────────────────────────┘ │
└─────────────────────────────────────┘
```

#### Avatar Stacking (Alternative)

Show a small booster avatar badge overlaid on the original author's avatar when space is limited.

### Handling Interactions

When a user interacts with a boosted post:

| Action | Behavior |
|--------|----------|
| **Like/React** | Creates reaction on original post |
| **Reply** | Creates reply to original post |
| **Boost** | Creates new boost by current user |
| **Bookmark** | Bookmarks original post |

## Boosting Posts (Client Feature)

Users can boost posts to share them with their followers.

### API Endpoint

```
POST /api/posts/{id}/boost
```

**Request Body (optional):**
```json
{
  "content": "Optional comment with the boost"
}
```

**Requirements:**
- User must be authenticated
- User must have a publisher
- Publisher must have a linked FediverseActor

**Response:**
Returns the created `SnBoost` object.

### Unboost

```
DELETE /api/posts/{id}/boost
```

Removes the user's boost from the post.

### Get Post Boosts

```
GET /api/posts/{id}/boosts
```

Returns list of users who boosted this post.

## Boost Visibility in Timeline

### What Appears

Posts boosted by:
- Fediverse actors you follow (via remote network)
- Local publishers you follow (on-site users)

### How It Works

1. When you follow a publisher, you see posts authored by that publisher
2. If that publisher boosts another post, you also see the boosted post
3. The boosted post appears with the publisher shown as `boosted_by`

## Example Scenarios

### Scenario 1: User Follows Fediverse Actor

1. User follows Fediverse actor `@alice@mastodon.social`
2. Alice boosts a post by `@bob@fosstodon.org`
3. User sees Bob's post in timeline with Alice shown as `boosted_by`

### Scenario 2: User Follows Local Publisher

1. User follows local publisher `@publisher@yourinstance.com`
2. Publisher boosts a post by `@external@othere instance.com`
3. User sees external post in timeline with publisher shown as `boosted_by`

### Scenario 3: Multiple Boosts

If multiple actors boost the same post, only the most recent boost is shown in the timeline (the one that caused the post to appear).

### Scenario 4: Original Author Boosted Own Post

Some implementations allow self-boosting. In this case, `boosted_by` will match `author`.

## Testing

### Testing Fediverse Boost Display

1. Follow a Fediverse actor on another instance
2. Have that actor boost a post from someone else
3. Check your timeline for the boosted post
4. Verify the `boosted_by` field is populated correctly

### Testing On-Site Boost

1. Create a post as user A
2. Have user B (with a publisher) boost user A's post
3. As a user following user B, check timeline
4. Verify user A's post appears with user B as `boosted_by`

## Related Documentation

- [Fediverse Actor API](./docs/ActivityPub/FediverseActorApi.md) - Fediverse actor data structure
- [ActivityPub Integration](./docs/ActivityPub/OVERVIEW.md) - Fediverse integration overview

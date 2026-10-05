# Publisher Payload: ActivityPub Identity

Publishers are serialized as one shape for local (individual/organizational) and mirrored
fediverse actors. Everything that only exists for federation is grouped under a single
`activitypub` object so a local publisher payload stays small.

## Shape

```json
{
  "id": "52f7c7eb-da51-49cd-a326-af8df68a9aad",
  "type": 2,
  "name": "littlesheep2code@mastodon.social",
  "nick": "LittleSheep",
  "bio": "<p>Builder of the solar-network</p>",
  "picture": {
    "id": "",
    "name": "",
    "url": "https://files.mastodon.social/accounts/avatars/.../5c223710c266300e.png",
    "mime_type": null,
    "size": 0,
    "width": null,
    "height": null
  },
  "background": {
    "id": "",
    "url": "https://files.mastodon.social/accounts/headers/.../dda6dc9eec8cafbb.jpg"
  },
  "display_name": "LittleSheep",
  "activitypub": {
    "uri": "https://mastodon.social/ap/users/115797665137366717",
    "actor_type": "Person",
    "username": "littlesheep2code",
    "instance_id": "90b66864-d100-4755-a089-825c7ce49785",
    "instance_domain": "mastodon.social",
    "inbox_uri": "https://mastodon.social/ap/users/115797665137366717/inbox",
    "outbox_uri": "https://mastodon.social/ap/users/115797665137366717/outbox",
    "followers_uri": "https://mastodon.social/ap/users/115797665137366717/followers",
    "following_uri": "https://mastodon.social/ap/users/115797665137366717/following",
    "featured_uri": "https://mastodon.social/ap/users/115797665137366717/collections/featured",
    "public_key_id": "https://mastodon.social/ap/users/115797665137366717#main-key",
    "public_key": "-----BEGIN PUBLIC KEY-----\n...\n-----END PUBLIC KEY-----\n",
    "is_bot": false,
    "is_locked": false,
    "is_discoverable": true,
    "is_community": false,
    "last_fetched_at": "2025-12-29T16:44:39.780836Z",
    "last_activity_at": null,
    "outbox_fetched_at": null,
    "followers_count": 0,
    "following_count": 0,
    "post_count": 0,
    "total_post_count": null,
    "full_handle": "littlesheep2code@mastodon.social",
    "web_url": "https://mastodon.social/ap/@115797665137366717"
  },
  "account_id": null,
  "realm_id": null,
  "rating": 100,
  "rating_level": 0,
  "resource_identifier": "publisher:52f7c7eb-da51-49cd-a326-af8df68a9aad",
  "created_at": "2025-12-29T16:44:39.781544Z",
  "updated_at": "2025-12-29T16:44:40.272295Z",
  "deleted_at": null
}
```

- `activitypub` is `null` for publishers without an ActivityPub presence (no `uri`, not a
  mirrored fediverse actor).
- The payload previously exposed `uri`, `inbox_uri`, `outbox_uri`, `followers_uri`,
  `following_uri`, `featured_uri`, `public_key_id`, `public_key`, `actor_type`, `username`,
  `instance_id`, `instance_domain`, `is_bot`, `is_locked`, `is_discoverable`, `is_community`,
  `last_fetched_at`, `last_activity_at`, `outbox_fetched_at`, `followers_count`,
  `following_count`, `post_count`, `total_post_count`, `full_handle` and `web_url` at the top
  level. All of them now live inside `activitypub`; the top-level keys are gone.

## Avatars and headers

`avatar_url` and `header_url` no longer exist. Both local cloud files and external fediverse
images use the existing `picture` / `background` file references:

| Source | `id` | `url` |
|--------|------|-------|
| Local cloud file | cloud file id | `null` |
| Fediverse actor | `""` | external image url |

Clients MUST prefer `url` when it is set and otherwise build the file endpoint from `id`.
`SnPublisher` stores only the underlying columns; the nested `activitypub` object and the
`activitypub` JSON key are derived, so the shape also survives the JSON cache round trip.

## Migration

`20261005074050_DropPublisherAvatarHeaderUrls` copies `avatar_url` / `header_url` into
`picture` / `background` for mirrored fediverse actors (those are addressed by `url`), then
drops both columns. Local publishers keep their existing `picture` / `background` cloud file
reference; their ActivityPub icon/image URL is rebuilt from the file id at render time.

`scripts/restore-fediverse-identity.sql` restores the identity that `20261001094753` left NULL on
mirrored publishers. It detects whether the legacy columns are still present and writes the images
into the matching shape, so it can run either before or after this migration.

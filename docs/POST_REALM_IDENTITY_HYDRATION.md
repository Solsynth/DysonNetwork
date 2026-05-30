# Post Realm Identity Hydration

Posts published in a realm now carry the publisher's realm-scoped identity, matching the existing chat member behavior.

## What Changed

Publishers (`SnPublisher`) now expose runtime-only realm identity fields when the post belongs to a realm:

- `realm_nick`
- `realm_bio`
- `realm_experience`
- `realm_level`
- `realm_leveling_progress`
- `realm_label`

These fields are `[NotMapped]` — no database migration is required. Data is fetched from the Realm service at runtime via `RemoteRealmService.LoadMemberAccounts`.

## How It Works

1. `PostService.LoadPubsAndActors` loads publishers for a batch of posts.
2. After loading publisher accounts, `PublisherService.HydratePublisherRealmIdentity` is called.
3. For each individual publisher with a `RealmId` and `AccountId`, the method creates a placeholder `SnRealmMember` and calls the Realm service.
4. The returned realm member data (nick, bio, level, label, etc.) is mapped onto the publisher.

This runs automatically on every post list/detail endpoint that calls `LoadPostInfo`.

## API Response Shape

For a post in a realm, the `publisher` object now includes realm identity:

```json
{
  "id": "...",
  "title": "...",
  "realm_id": "...",
  "publisher": {
    "id": "...",
    "name": "john",
    "nick": "John's Journal",
    "realm_nick": "Realm John",
    "realm_bio": "John's realm bio",
    "realm_experience": 1250,
    "realm_level": 5,
    "realm_leveling_progress": 0.45,
    "realm_label": {
      "id": "...",
      "name": "Veteran",
      "color": "#ff0000",
      "icon": "star"
    }
  }
}
```

When the post has no `RealmId` or the publisher is not an individual account, the realm fields are `null`.

## No Schema Migration

All new fields are runtime-only (`[NotMapped]`). The realm identity data already exists in the `realm_members` table managed by the Passport service. No changes to the Sphere database schema are needed.

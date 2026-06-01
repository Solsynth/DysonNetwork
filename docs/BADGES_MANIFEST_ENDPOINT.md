# Badges Manifest Endpoint

## Summary

Public discovery endpoint that serves a machine-readable manifest of all progression badges in the Solar Network.

Clients fetch this manifest to render badge UI with correct colors, icons, and localization keys without hardcoding badge metadata.

An optional icon endpoint serves custom SVG files for each badge, configured via a local folder path.

## Endpoints

### Manifest

```
GET /.well-known/badges
```

- No authentication required
- Response cached for 1 hour (`Cache-Control: public, max-age=3600`)
- Production gateway: `/passport/.well-known/badges`

### Badge Icon (SVG)

```
GET /.well-known/badges/icons/{identifier}
```

- Returns the SVG file for the given badge identifier
- Response cached for 24 hours (`Cache-Control: public, max-age=86400`)
- Returns `404` if no icons folder is configured or the file does not exist
- Production gateway: `/passport/.well-known/badges/icons/{identifier}`

## Configuration

In `appsettings.json`:

```json
{
  "Badges": {
    "IconsPath": "./Resources/Badges"
  }
}
```

| Key | Type | Description |
|---|---|---|
| `IconsPath` | string | Path to a folder containing SVG files named `{identifier}.svg` |

When `IconsPath` is set and the directory exists, each badge in the manifest will include an `icon_url` field pointing to the SVG endpoint. When unset or the directory is missing, `icon_url` will be `null` and the icon endpoint returns `404`.

### SVG File Naming

Files must be named exactly after the badge identifier with a `.svg` extension:

```
Resources/Badges/
├── progression.post.expert.svg
├── progression.post.featured.svg
├── progression.post.streak.30.svg
├── progression.login.streak.365.svg
├── progression.friends.50.svg
└── ...
```

## Response Format

```json
{
  "version": 1,
  "badges": [
    {
      "identifier": "progression.post.expert",
      "achievement_identifier": "expert-post",
      "label": "Better than 陆游",
      "caption": "Created over 9362 posts",
      "icon": "ink",
      "color": "#6366f1",
      "icon_url": "/.well-known/badges/icons/progression.post.expert",
      "localization_key": "badge.post_expert",
      "category": "post",
      "series": null,
      "hidden": false
    }
  ]
}
```

### Fields

| Field | Type | Description |
|---|---|---|
| `identifier` | string | Badge type identifier, matches `SnProgressBadgeRewardDefinition.Type` |
| `achievement_identifier` | string | Links to the achievement definition in progression system |
| `label` | string | Display name shown on the badge |
| `caption` | string | Description or earn condition |
| `icon` | string | Lucide icon name (fallback when no SVG is available) |
| `color` | string | Hex color for badge rendering |
| `icon_url` | string/null | URL to fetch the badge SVG, or null if not configured |
| `localization_key` | string | i18n key for client-side translation |
| `category` | string | Badge grouping |
| `series` | object/null | Series metadata if badge belongs to a tier ladder |
| `hidden` | boolean | Whether badge is secret until unlocked |

### Categories

| Category | Description |
|---|---|
| `post` | Post creation and engagement milestones |
| `streak` | Consecutive day activity streaks |
| `social` | Friend count and reaction milestones |
| `supporter` | Stellar Program subscription milestones |
| `chat` | Chat usage milestones |
| `account` | Profile and account setup milestones |
| `hidden` | Secret achievements not shown until earned |

### Series Object

When a badge belongs to a tier ladder:

```json
{
  "series": {
    "identifier": "post-streak",
    "title": "Posting Streak",
    "order": 3
  }
}
```

## Source

Controller:

- [DysonNetwork.Passport/Progression/BadgesDiscoveryController.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Passport/Progression/BadgesDiscoveryController.cs)

Badge definitions are derived from:

- [DysonNetwork.Passport/Progression/ProgressionCatalogDefaults.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Passport/Progression/ProgressionCatalogDefaults.cs)

Only achievements that include a `Reward.Badge` are listed in the manifest.

## Localization

The `localization_key` field follows the pattern `badge.<achievement_identifier_with_underscores>`.

Clients should resolve these keys against their own locale bundles. Example mapping:

| localization_key | English label |
|---|---|
| `badge.post_expert` | Better than 陆游 |
| `badge.post_featured` | Editor's Pick |
| `badge.post_streak_30` | Serial Publisher |
| `badge.login_streak_365` | Still Here |
| `badge.friends_50` | Community Pillar |
| `badge.hidden_night_owl` | Night Owl |

## Client Usage

1. Fetch `GET /.well-known/badges` on app start or periodically
2. Cache the manifest client-side
3. When rendering a user's badge (from progression reward grant), match `Reward.Badge.Type` against manifest `identifier`
4. Use the matched entry's `color`, `label`, and `localization_key` for rendering
5. Prefer `icon_url` for the badge image when available; fall back to `icon` (Lucide name) for a generic icon
6. For hidden badges, show a placeholder or generic "Secret Badge" until the user unlocks it

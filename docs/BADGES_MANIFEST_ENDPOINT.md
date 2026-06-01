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
GET /.well-known/badges/icons/{iconName}
```

- Returns the SVG file for the given icon name (the `icon` field from the manifest)
- Response cached for 24 hours (`Cache-Control: public, max-age=86400`)
- Returns `404` if no icons folder is configured or the file does not exist
- Production gateway: `/passport/.well-known/badges/icons/{iconName}`
- SVGs are shared across badges with the same icon; the client applies the `color` from the manifest

## Configuration

In `appsettings.json`:

```json
{
  "Badges": {
    "IconsPath": "./Resources/BadgeIcons"
  }
}
```

| Key | Type | Description |
|---|---|---|
| `IconsPath` | string | Path to a folder containing SVG files named `{icon}.svg` |

When `IconsPath` is set and the directory exists, each badge in the manifest will include an `icon_url` field pointing to the SVG endpoint. When unset or the directory is missing, `icon_url` will be `null` and the icon endpoint returns `404`.

### SVG File Naming

SVGs are named by **icon name** (not badge identifier). Multiple badges sharing the same icon use a single SVG file — the client applies the badge's `color` to it.

```
Resources/BadgeIcons/
├── draw.svg                → post.expert, post.streak.90
├── recommend.svg           → post.featured, post.featured.expert
├── calendar-add-on.svg     → post.streak.30, post.streak.365, login.streak.90, login.streak.365
├── diversity-2.svg         → friends.50, friends.100, hidden.speed_friend
├── bolt-boost.svg          → boost.50, reaction.expert
├── shapes.svg              → post.topical.first, post.topical.50
├── volunteer-activism.svg  → stellar.supporter.12, hidden.social_butterfly_day
├── moon-stars.svg          → hidden.night_owl
├── whatshot.svg            → post.downvote.5, post.downvote.20
├── face.svg                → account.avatar
├── badge.svg               → account.profile_complete
├── do-not-step.svg         → chat.expert
└── repeat.svg              → (unused, kept as spare)
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
      "icon": "draw",
      "color": "#6366f1",
      "icon_url": "/.well-known/badges/icons/draw",
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
| `icon` | string | Icon name — used as both the SVG filename and generic fallback |
| `color` | string | Hex color for the client to apply to the SVG icon |
| `icon_url` | string/null | URL to fetch the SVG by icon name, or null if not configured |
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

## Deployment

SVG files are included in the publish output via the csproj:

```xml
<Content Include="Resources\BadgeIcons\**\*.svg">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</Content>
```

No Dockerfile changes are needed — `COPY . .` picks up the files and `dotnet publish` copies them to the output directory automatically.

To add or update icons, place `.svg` files in `Resources/BadgeIcons/` and update the `BadgeEntries` array in the controller if new icon names are introduced.

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
4. Fetch the SVG from `icon_url` and apply the badge's `color` to it (e.g. via CSS `fill`/`currentColor` or SVG manipulation)
5. Use `label`, `caption`, and `localization_key` for text rendering
6. Fall back to `icon` for a generic icon when `icon_url` is null
7. For hidden badges, show a placeholder or generic "Secret Badge" until the user unlocks it

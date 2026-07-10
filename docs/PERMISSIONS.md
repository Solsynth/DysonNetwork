# Permission System

Dyson Network uses a fine-grained, attribute-based permission model. Every mutating endpoint declares one or more `[AskPermission("key")]` attributes. The middleware checks the actor's permission nodes (stored in Padlock) before allowing the request.

---

## Architecture

```
Client request
  → [Authorize] (authentication)
  → AskPermissionMiddleware (authorization)
    → OAuth scope gate (if OAuth session)
    → Superuser bypass
    → Permission node lookup (gRPC or local DB)
  → Controller action
```

Two middleware implementations exist:

| Middleware | Location | Used by | Backend |
|---|---|---|---|
| `RemotePermissionMiddleware` | `DysonNetwork.Shared` | Sphere, Passport, Messager, Develop, Wallet, Ring | gRPC `PermissionService` |
| `LocalPermissionMiddleware` | `DysonNetwork.Padlock` | Padlock | Direct EF Core query |

Both enforce the same logic: iterate all `[AskPermission]` attributes on the endpoint, check each key against the actor's permission nodes, and return 403 on the first failure.

---

## Permission keys

All keys are defined as constants in `DysonNetwork.Shared/Auth/PermissionKeys.cs`.

Convention: `{domain}.{resource}.{action}`

Examples:
- `posts.create`
- `chat.messages.delete`
- `accounts.statuses.update`
- `wallets.balance.modify`

### Wildcard scopes

OAuth tokens can carry a `.*` scope that matches any key with that prefix. For example, a token with scope `posts.*` satisfies `posts.create`, `posts.update`, `posts.delete`, etc. This is handled by `PermissionScopeGate.GetMatchedPermissionScope()`.

### Full scope

A token with scope `*` bypasses all permission checks (except superuser, which always bypasses).

---

## Permission nodes

Each permission is stored as an `SnPermissionNode`:

| Field | Description |
|---|---|
| `Actor` | The account or group this node applies to (e.g. `user:<id>`, `group:default`) |
| `Key` | The permission key string |
| `Value` | JSONB value (boolean `true` for simple grants) |
| `GroupId` | Optional — if set, this node belongs to a permission group |
| `ExpiredAt` | Optional TTL |
| `AffectedAt` | Optional activation time |

### Permission groups

`SnPermissionGroup` + `SnPermissionGroupMember` let you assign a set of permissions to multiple actors at once. The `default` group is seeded on first migration and grants all registered permission keys to every member.

### Punishment-based blocking

`SnAccountPunishment` with `Type = PermissionModification` can block specific permission keys for an actor. Blocked permissions take precedence over granted nodes, including group memberships.

---

## Well-known endpoint

`GET /.well-known/permissions` returns all registered permission keys.

Response:
```json
{
  "count": 247,
  "permissions": [
    { "key": "accounts.connections.view", "name": "AccountsConnectionsView" },
    { "key": "accounts.delete", "name": "AccountsDeletion" }
  ]
}
```

Public, no auth required. Useful for admin UIs and client-side permission introspection.

---

## Default group seeding

On first database migration, Padlock seeds a `default` permission group containing **all** keys from `PermissionKeys.cs` via reflection:

```csharp
var allPermissionKeys = typeof(PermissionKeys)
    .GetFields(BindingFlags.Public | BindingFlags.Static)
    .Where(f => f.IsLiteral && f.FieldType == typeof(string))
    .Select(f => (string)f.GetRawConstantValue()!)
    .ToList();
```

This means adding a new `public const string` to `PermissionKeys.cs` automatically includes it in the default group on next migration — no manual seeding update needed.

---

## Usage in controllers

```csharp
[HttpPost("{id:guid}/reactions")]
[Authorize]
[AskPermission(PermissionKeys.PostsReact)]
public async Task<ActionResult<SnPostReaction>> ReactPost(...)
```

Multiple attributes are supported (all must pass):

```csharp
[HttpPost]
[Authorize]
[AskPermission(PermissionKeys.PermissionsManage)]
[AskPermission(PermissionKeys.PermissionsGroupsManage)]
public async Task<IActionResult> GivePermission(...)
```

---

## Key registry

### Accounts
- `accounts.view`, `accounts.manage`, `accounts.delete`
- `accounts.statuses.create`, `accounts.statuses.update`
- `accounts.connections.view`

### Authentication & Security
- `auth.sessions.manage`, `auth.factors.manage`, `auth.api.keys.manage`
- `auth.apps.authorize`, `auth.recover`
- `account.contacts.manage`, `account.devices.manage`, `account.authorized.apps.manage`

### Chat
- `chat.create`, `chat.update`, `chat.delete`
- `chat.messages.create`, `chat.messages.update`, `chat.messages.delete`, `chat.messages.react`
- `chat.members.manage`, `chat.members.kick`, `chat.members.timeout`
- `chat.invites.manage`, `chat.e2ee.manage`, `chat.groups.manage`
- `chat.pins.manage`, `chat.read.all`, `chat.sync`
- `chat.call.start`, `chat.call.end`, `chat.call.invite`, `chat.call.kick`, `chat.call.mute`

### Plugin marketplace
- `mini.apps.view`
- `mini.apps.create`, `mini.apps.update`, `mini.apps.delete`
- `mini.apps.package.upload`

### Posts
- `posts.view`, `posts.create`, `posts.update`, `posts.delete`, `posts.publish`
- `posts.react`, `posts.boost`, `posts.moderate`, `posts.lock`
- `posts.bookmark`, `posts.award`, `posts.sponsor`, `posts.pin`
- `posts.batch.delete`, `posts.batch.visibility`

### Post Collections
- `post.collections.create`, `post.collections.update`, `post.collections.delete`
- `post.collections.posts.manage`

### Post Categories & Tags
- `post.categories.manage`, `post.categories.subscribe`
- `posts.tags.create`, `posts.tags.update`, `posts.tags.assign`
- `posts.tags.claim`, `posts.tags.protect`, `posts.tags.event`

### Post Subscriptions
- `post.subscriptions.manage`

### Publishers
- `publishers.create`, `publishers.update`, `publishers.delete`
- `publishers.members.manage`, `publishers.invites.manage`
- `publishers.features.manage`, `publishers.fediverse.manage`
- `publishers.domains.manage`
- `publishers.rewards.settle`, `publishers.rewards.resettle`
- `publishers.subscriptions.manage`

### Timelines
- `timelines.feedback`, `timelines.weights.manage`, `timelines.reset`

### Automod
- `automod.rules.manage`, `automod.rules.test`

### Fediverse
- `fediverse.moderation.rules.manage`, `fediverse.moderation.check`
- `fediverse.keys.manage`

### Stickers
- `stickers.packs.create`, `stickers.packs.update`, `stickers.packs.delete`
- `stickers.packs.own`, `stickers.packs.order`
- `stickers.create`, `stickers.update`, `stickers.delete`, `stickers.content.update`

### Surveys
- `surveys.create`, `surveys.update`, `surveys.delete`
- `surveys.publish`, `surveys.archive`, `surveys.clone`
- `surveys.answer`, `surveys.subscribe`

### Live Streams
- `live.streams.create`, `live.streams.update`, `live.streams.delete`
- `live.streams.start`, `live.streams.end`
- `live.streams.egress`, `live.streams.hls`
- `live.streams.pin`, `live.streams.thumbnail`
- `live.streams.chat.moderate`, `live.streams.awards`

### Ads
- `ads.manage`, `ads.leaderboard.view`

### Translation
- `translation.manage`

### Quotes
- `quotes.authorization.manage`

### Wallet
- `wallets.manage`, `wallets.create`, `wallets.delete`
- `wallets.balance.modify`
- `wallets.funds.manage`, `wallets.transactions.manage`
- `wallets.transfer.requests.manage`, `wallets.public.id.manage`

### Orders
- `orders.create`, `orders.update`, `orders.pay`
- `orders.view`, `orders.payouts.manage`

### Merchants
- `merchants.manage`, `merchants.settlements.manage`

### Subscriptions
- `subscriptions.create`, `subscriptions.cancel`, `subscriptions.activate`
- `subscriptions.order.manage`, `subscriptions.checkout`
- `subscriptions.groups.manage`
- `subscription.gifts.purchase`, `subscription.gifts.redeem`
- `subscription.gifts.send`, `subscription.gifts.cancel`

### Notifications
- `notifications.send`, `notifications.put`, `notifications.read.all`
- `notifications.preferences.manage`, `notifications.subscriptions.manage`
- `notifications.sop.subscribe`

### Social Credits
- `credits.validate.perform`, `credits.manage`

### Presence
- `presences.scan`, `presences.manage`
- `presences.activity.manage`, `presences.artwork.manage`

### Relationships
- `relationships.create`, `relationships.update`, `relationships.delete`
- `relationships.friends.manage`, `relationships.block.manage`
- `relationships.mute.manage`, `relationships.close.friends.manage`
- `relationships.alias.manage`, `relationships.inspect`
- `relationships.sync`

### Realms
- `realms.create`, `realms.update`, `realms.delete`
- `realms.invites.manage`, `realms.members.manage`
- `realms.labels.manage`, `realms.boosts.manage`
- `realms.permissions.manage`

### Notable Days
- `notable.days.create`, `notable.days.update`, `notable.days.delete`

### NFC
- `nfc.tags.create`, `nfc.tags.update`, `nfc.tags.delete`
- `nfc.tags.claim`, `nfc.tags.lock`
- `nfc.admin.manage`

### Tickets
- `tickets.create`, `tickets.update`, `tickets.delete`
- `tickets.messages.create`, `tickets.status.update`, `tickets.assign`

### Progression
- `progression.achievements.manage`, `progression.quests.manage`
- `progression.sync`, `progression.badges.manage`

### Domain Trust
- `domain.trust.create`, `domain.trust.update`, `domain.trust.delete`
- `domain.trust.validate`

### Meet / Location
- `meet.create`, `meet.update`, `meet.delete`, `meet.complete`
- `meet.join`, `meet.pin.manage`, `meet.visibility.update`
- `location.pins.create`, `location.pins.update`, `location.pins.delete`

### Calendar
- `calendar.events.create`, `calendar.events.update`, `calendar.events.delete`
- `calendar.subscriptions.manage`, `calendar.checkin.manage`

### Nearby
- `nearby.presence.manage`, `nearby.resolve`

### Affiliations
- `affiliations.manage`, `affiliations.results.manage`

### Rewind
- `rewind.create`

### E2EE
- `e2ee.keys.manage`, `e2ee.mls.manage`, `e2ee.devices.manage`

### Auth
- `auth.sessions.manage`, `auth.factors.manage`, `auth.api.keys.manage`
- `auth.apps.authorize`, `auth.recover`

### Account Security
- `account.contacts.manage`, `account.devices.manage`
- `account.authorized.apps.manage`

### Reader / Cache
- `cache.scrap`

### Developers
- `developers.create`, `developers.manage`
- `custom.apps.create`, `custom.apps.update`, `custom.apps.delete`
- `custom.apps.secrets.manage`
- `bot.accounts.create`, `bot.accounts.update`, `bot.accounts.delete`
- `bot.accounts.keys.manage`, `bot.accounts.chat.manage`
- `app.products.create`, `app.products.update`, `app.products.delete`
- `dev.projects.create`, `dev.projects.update`, `dev.projects.delete`
- `mini.apps.create`, `mini.apps.update`, `mini.apps.delete`
- `quotas.manage`

### Admin
- `admin.ip.check`

### Permissions (Padlock admin)
- `permissions.check`, `permissions.manage`
- `permissions.groups.check`, `permissions.groups.manage`
- `permissions.cache.manage`

### Punishments
- `punishments.view`, `punishments.create`
- `punishments.update`, `punishments.delete`

# Passport Progression System

## Summary

Passport now owns a configurable achievement and quest system backed by database tables.

The system is event-driven:

- Padlock writes action logs
- Padlock publishes tracked action-log events to the shared event bus
- Passport consumes those events
- Passport updates achievement and quest progress
- Passport grants rewards automatically
- Passport adds a silent completion notification to the user inbox

Definitions live in Passport DB. Passport seeds built-in defaults from code at startup for missing or seed-managed records.

## Event Flow

### 1. Action log creation in Padlock

Tracked action logs are written in:

- [DysonNetwork.Padlock/Account/ActionLogService.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Padlock/Account/ActionLogService.cs)

After the action log row is saved, Padlock publishes:

- `ActionLogTriggeredEvent`

Shared event contract:

- [DysonNetwork.Shared/Queue/ActionLogEvent.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Shared/Queue/ActionLogEvent.cs)

Current tracked action list:

- `posts.create`
- `posts.react`
- `posts.bookmark`
- `posts.boost`
- `posts.featured`
- `chat.use`
- `chatrooms.join`
- `publishers.create`
- `publishers.members.join`
- `realms.create`
- `realms.join`
- `accounts.profile.update`
- `accounts.profile.avatar`
- `accounts.profile.complete`
- `accounts.connection.link`
- `accounts.push.enable`
- `accounts.active`
- `stellar.support.month`
- `login`
- `relationships.friends.request`
- `relationships.friends.accept`
- `relationships.friends.established`
- `relationships.block`
- `relationships.unblock`
- `accounts.auth_factors.create`
- `accounts.auth_factors.enable`
- `accounts.auth_factors.disable`
- `accounts.auth_factors.delete`
- `accounts.auth_factors.reset_password`
- `developer.sessions.revoke`
- `developer.devices.revoke`
- `developer.devices.rename`
- `developer.apps.deauthorize`

Published subject format:

- `action_logs.<action>`

JetStream stream:

- `action_log_events`

### 2. Event handling in Passport

Passport subscribes to `action_logs.>` in:

- [DysonNetwork.Passport/Startup/ServiceCollectionExtensions.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Passport/Startup/ServiceCollectionExtensions.cs)

The listener delegates to:

- [DysonNetwork.Passport/Progression/ProgressionService.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Passport/Progression/ProgressionService.cs)

The service:

- loads matching enabled achievement definitions
- loads matching enabled quest definitions
- resolves the account timezone, falling back to configured default
- increments progress
- collapses stepped series on user-facing lists so only the current or highest tier is shown
- deduplicates repeated event deliveries
- creates reward-grant audit rows
- delivers badge, XP, source point, and inbox rewards

## Data Model

Shared progression models:

- [DysonNetwork.Shared/Models/Progression.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Shared/Models/Progression.cs)

Passport tables:

- `achievement_definitions`
- `quest_definitions`
- `account_achievements`
- `account_quest_progresses`
- `progress_reward_grants`
- `progress_event_receipts`

Migration:

- [DysonNetwork.Passport/Migrations/20260318165458_AddProgressionSystem.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Passport/Migrations/20260318165458_AddProgressionSystem.cs)

### Definitions

Achievement definitions include:

- stable `Identifier`
- title, summary, icon, sort order
- optional series metadata: `SeriesIdentifier`, `SeriesTitle`, `SeriesOrder`
- hidden flag
- enabled flag
- progress-enabled flag
- optional `AvailableFrom` and `AvailableUntil` event window
- seed-managed flag
- target count
- trigger definition
- reward definition

Quest definitions include the same core fields plus:

- schedule config
- repeatability mode: `daily`, `weekly`, `monthly`, or `none`

### Stepped achievements and quests

Passport supports tiered progression definitions through series metadata.

- `SeriesIdentifier`: stable key for a tier ladder
- `SeriesTitle`: user-facing title for the merged ladder
- `SeriesOrder`: step number inside the ladder

User-facing progression endpoints merge definitions that share the same `SeriesIdentifier`:

- if the user still has unfinished tiers, Passport returns the first unfinished tier
- if the whole ladder is complete, Passport returns the highest completed tier
- the payload also includes `SeriesTotalSteps` and `SeriesCompletedSteps`
- the payload includes a `SeriesStages` array with every tier in the ladder and its completion status (`Identifier`, `Title`, `SeriesOrder`, `TargetCount`, `IsCompleted`, `CompletedAt`)

This is intended for upgrade-style ladders such as:

- posting streaks: 3 / 7 / 30 / 90 / 365 days
- activity streaks: 7 / 30 / 90 / 365 days
- featured post milestones
- Stellar supporter milestones: 1 / 3 / 6 / 9 / 12 eligible months
- friend count milestones: 1 / 5 / 20 / 50 / 100 friends

### Definition lifecycle

- `IsEnabled`: visible in the catalog
- `IsProgressEnabled`: can still gain progress from events
- `AvailableFrom` / `AvailableUntil`: optional live event window

This allows a special-event achievement or quest to stay visible after the event ends while no longer being earnable.

### User progress

Achievements:

- one row per account and definition
- complete once only

Quests:

- one row per account, definition, and period key
- period key is based on daily, weekly, or monthly reset windows

### Deduplication

The system prevents duplicate progression and rewards with:

- `progress_event_receipts` for event-processing idempotency
- `progress_reward_grants` for reward-delivery idempotency

This matters because event-bus consumers may retry or redeliver messages.

## Reward Delivery

Rewards are applied automatically on completion. There is no manual claim flow in v1.

### Badge reward

Badge rewards are granted through Passport account services.

### Experience reward

Experience rewards use:

- [DysonNetwork.Passport/Leveling/ExperienceService.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Passport/Leveling/ExperienceService.cs)

Reason type used by progression:

- `progression`

### Source point reward

Source points are wallet-backed and use the existing Wallet currency:

- `points`

The reward is granted through:

- `RemotePaymentService.CreateTransactionWithAccount(...)`

Transaction type used:

- `DyTransactionType.System`

### Completion inbox notification

Completion notifications are delivered through:

- [DysonNetwork.Shared/Registry/RemoteRingService.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Shared/Registry/RemoteRingService.cs)

- `topic`: `progression.completed`
- `IsSilent = true`
- `IsSavable = true`

Notification metadata currently includes:

- completion kind: achievement or quest
- definition identifier
- definition title
- optional quest period key
- reward payload

This means progression completions are stored in inbox/history without producing a loud live websocket popup.

## Config And Seeding

Built-in catalog defaults live in:

- [DysonNetwork.Passport/Progression/ProgressionCatalogDefaults.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Passport/Progression/ProgressionCatalogDefaults.cs)

Seed sync service:

- [DysonNetwork.Passport/Progression/ProgressionSeedService.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Passport/Progression/ProgressionSeedService.cs)

Startup wiring:

- [DysonNetwork.Passport/Program.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Passport/Program.cs)

Current seeding behavior:

- missing definitions are inserted from code defaults
- existing definitions are updated only when `IsSeedManaged` is `true`
- DB remains the runtime source of truth
- built-in defaults include streak ladders, featured-post ladders, Stellar supporter ladders, friend count ladders, and first-time action achievements

This is intentionally similar to Wallet’s subscription catalog seeding model.

## Chat Use Action Log

`chat.use` is emitted by:

- [DysonNetwork.Messager/Chat/ChatService.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Messager/Chat/ChatService.cs)

Current behavior:

- emitted only for non-system user messages
- throttled to at most once per account per minute
- includes normal action-log request metadata such as user-agent and IP address
- includes action meta fields: `room_id` and `message_type`

## Avatar Action Log

`accounts.profile.avatar` is emitted by:

- [DysonNetwork.Passport/Account/AccountCurrentController.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Passport/Account/AccountCurrentController.cs)

Current behavior:

- emitted only when the user sets a profile picture for the first time (old `Picture` field was null)
- fires alongside the general `accounts.profile.update` log
- metadata: empty dictionary

Emission logic:

```csharp
var hadPicture = profile.Picture is not null;
// ... update profile ...
if (!hadPicture && profile.Picture is not null)
{
    remoteActionLogs.CreateActionLog(userId, ActionLogType.AccountAvatar, ...);
}
```

## Connection Link Action Log

`accounts.connection.link` is emitted by:

- [DysonNetwork.Padlock/Auth/OpenId/ConnectionController.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Padlock/Auth/OpenId/ConnectionController.cs) — web OAuth flow and mobile Apple sign-in
- [DysonNetwork.Padlock/Auth/OpenId/OidcController.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Padlock/Auth/OpenId/OidcController.cs) — new connection during login flow

Current behavior:

- emitted only when a **new** external account connection is created (not on re-auth or token refresh)
- not emitted when updating an existing connection's tokens
- metadata: `provider` (string, e.g. `"apple"`, `"google"`, `"discord"`)

Supported providers: apple, google, microsoft, discord, github, steam, afdian.

## Push Notification Enable Action Log

`accounts.push.enable` is emitted by:

- [DysonNetwork.Ring/Notification/PushService.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Ring/Notification/PushService.cs)

Current behavior:

- emitted when a new push subscription is created and it is the account's **first** active subscription
- checks existing subscription count after insert; emits only when count <= 1
- metadata: `provider` (string, e.g. `"fcm"`, `"apns"`, `"sop"`)

The Ring service uses `RemoteActionLogService` (gRPC to Padlock) to write the action log, since Ring does not have direct access to the Padlock action log database.

## Friend Established Action Log

`relationships.friends.established` is emitted by:

- [DysonNetwork.Passport/Account/RelationshipService.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Passport/Account/RelationshipService.cs)

Current behavior:

- emitted for **both** parties when a friend request is accepted
- fires alongside the existing `relationships.friends.accept` log (which only fires for the acceptor)
- metadata: `related_account_id` (Guid of the other party)

This means both the sender and receiver of a friend request get progression credit for friend-count achievements.

## Profile Complete Action Log

`accounts.profile.complete` is emitted by:

- [DysonNetwork.Passport/Account/AccountCurrentController.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Passport/Account/AccountCurrentController.cs)

Current behavior:

- emitted when the user's profile transitions from incomplete to complete
- checks before and after the profile update to avoid duplicate emissions
- metadata: empty dictionary

Profile complete requires all of the following fields to be non-null and non-empty:

- `FirstName`
- `LastName`
- `Bio`
- `Location`
- `Pronouns`
- `Birthday`
- `Picture` (avatar)

## Chatroom Join Action Log

`chatrooms.join` is emitted by:

- [DysonNetwork.Messager/Chat/ChatRoomController.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Messager/Chat/ChatRoomController.cs)

Current behavior:

- emitted when a user accepts a chatroom invite (`AcceptChatInvite`)
- emitted when a user self-joins a community chatroom (`JoinChatRoom`)
- metadata: `chatroom_id` (string)

## Stellar Support Progression

Eligible Stellar purchases emit a support-month action log from Wallet:

- action: `stellar.support.month`
- implementation: [DysonNetwork.Wallet/Payment/SubscriptionService.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Wallet/Payment/SubscriptionService.cs)

Emission rules:

- only subscriptions in the `solian.stellar` group are counted
- only payment methods in Wallet `SponsorRewardEligiblePaymentMethods` are counted
- in-app wallet purchases are excluded by default
- one action log is emitted per credited purchase month, so 12-month purchases advance 12 steps

Current action metadata includes:

- `subscription_id`
- `subscription_identifier`
- `subscription_group_identifier`
- `payment_method`
- `perk_level`
- `credited_months`
- `credited_month_index`
- `order_id`

## APIs

### Current user APIs

Controller:

- [DysonNetwork.Passport/Progression/ProgressionController.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Passport/Progression/ProgressionController.cs)

Endpoints:

- `GET /api/accounts/me/progression/achievements`
- `GET /api/accounts/me/progression/quests`
- `GET /api/accounts/me/progression/grants`

User list payloads now include series fields for merged stepped ladders:

- `SeriesIdentifier`
- `SeriesTitle`
- `SeriesOrder`
- `SeriesTotalSteps`
- `SeriesCompletedSteps`
- `SeriesStages` — array of all tiers with `Identifier`, `Title`, `SeriesOrder`, `TargetCount`, `IsCompleted`, `CompletedAt`

### Admin APIs

Controller:

- [DysonNetwork.Passport/Progression/ProgressionAdminController.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Passport/Progression/ProgressionAdminController.cs)

Endpoints:

- `GET /api/admin/progression/achievements`
- `POST /api/admin/progression/achievements`
- `PUT /api/admin/progression/achievements/{identifier}`
- `POST /api/admin/progression/achievements/{identifier}/enable?enabled=true|false`
- `GET /api/admin/progression/quests`
- `POST /api/admin/progression/quests`
- `PUT /api/admin/progression/quests/{identifier}`
- `POST /api/admin/progression/quests/{identifier}/enable?enabled=true|false`
- `POST /api/admin/progression/sync`

## Admin Authorization

Progression admin endpoints are not guarded by `IsSuperuser` anymore.

They now call Padlock permission service over gRPC using:

- `DyPermissionService.HasPermissionAsync(...)`

Required permission key:

- `progression.admin`

Implementation:

- [DysonNetwork.Passport/Progression/ProgressionAdminController.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Passport/Progression/ProgressionAdminController.cs)

Note:

- [DysonNetwork.Shared/Registry/RemoteAccountService.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Shared/Registry/RemoteAccountService.cs) currently does not expose a permission helper
- the controller therefore uses the same direct `DyPermissionService` pattern already used by Passport ticket admin flows

## Seeded Content

### Achievements

Current code-seeded achievement highlights:

- first post, first reaction, first chat, first realm join, and first publisher
- first avatar, first external account link, first push notification enable
- first bookmark, first boost, first chatroom join
- profile completion, first 2FA enable
- friend count ladder (1 / 5 / 20 / 50 / 100)
- featured post ladder
- posting streak ladder (3 / 7 / 30 / 90 / 365 days)
- activity streak ladder (7 / 30 / 90 / 365 days)
- Stellar supporter ladder (1 / 3 / 6 / 9 / 12 months)
- high-count chat, post, reaction, bookmark, boost, chatroom, and realm join milestones

Hidden achievements (not shown in catalog until unlocked):

- Night Owl: post between midnight and 4am
- Instant Connection: accept a friend request within 60 seconds
- Social Butterfly: make 5 friends in a single day

### Quests

Current code-seeded quests:

Daily:

- Daily Dispatch: publish one post
- Give Credit: react to 3 posts
- Say Something: send a chat message

Weekly:

- Weekly Writer: create 5 posts
- Appreciator: react to 20 posts
- Make a Friend: make 1 new friend
- Amplifier: boost 3 posts

Monthly:

- Chronicler: create 20 posts
- Explorer: join a new realm

You can add more definitions either by:

- inserting/updating DB rows directly
- calling the admin APIs
- extending appsettings seed data and resyncing

## Current Limitations

- V1 does not include a manual reward-claim step
- only tracked action-log types publish progression events
- merged series are applied only on user-facing progression list endpoints; admin definition endpoints still return every raw tier
- quest scheduling currently uses account profile timezone when available, otherwise configured default timezone
- progression completion now lands in the inbox silently instead of using a loud direct websocket popup
- `chat.use` is emitted by Messager after successful chat sends and is throttled to one action log per user every minute

## Verification

The implementation was validated with:

- `dotnet build DysonNetwork.Passport/DysonNetwork.Passport.csproj`
- `dotnet build DysonNetwork.Wallet/DysonNetwork.Wallet.csproj`
- `dotnet build DysonNetwork.Padlock/DysonNetwork.Padlock.csproj`
- `dotnet build DysonNetwork.Ring/DysonNetwork.Ring.csproj`

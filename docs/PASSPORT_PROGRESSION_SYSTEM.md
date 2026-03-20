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
- `chat.use`
- `publishers.create`
- `publishers.members.join`
- `realms.create`
- `realms.join`

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

This is intended for upgrade-style ladders such as:

- posting streaks: 3 / 7 / 30 days
- activity streaks: 7 / 30 / 365 days
- featured post milestones
- Stellar supporter milestones: 1 / 3 / 6 / 9 / 12 eligible months

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
- built-in defaults include streak ladders, featured-post ladders, and Stellar supporter ladders

This is intentionally similar to Wallet’s subscription catalog seeding model.

## Chat Use Action Log

`chat.use` is emitted by:

- [DysonNetwork.Messager/Chat/ChatService.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Messager/Chat/ChatService.cs)

Current behavior:

- emitted only for non-system user messages
- throttled to at most once per account per minute
- includes normal action-log request metadata such as user-agent and IP address
- includes action meta fields: `room_id` and `message_type`

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

Current code-seeded achievement highlights:

- first post, first reaction, first chat, first realm join, and first publisher
- featured post ladder
- posting streak ladder
- activity streak ladder
- Stellar supporter ladder
- high-count chat and post milestones

Current code-seeded quest highlights:

- daily post

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

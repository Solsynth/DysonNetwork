# Realm Identity, Boosts, and In-Realm Leveling

This document describes the new realm member identity system, shared realm boosts, moderator-managed labels, and in-realm leveling.

## Overview

Realm membership is now more than a role assignment.

Each active realm member can now have:

- a realm-scoped `nick`
- a realm-scoped `bio`
- one assigned realm label (`称号`)
- realm-specific XP, level, and leveling progress

Each realm now also tracks:

- cumulative `boost_points`
- computed `boost_level`

Linked chat rooms inherit the realm identity overlay when the chat room has a matching `realm_id`.

## Member Identity

Realm identity is stored on `SnRealmMember`.

New member fields:

- `nick`
- `bio`
- `label_id`
- `experience`
- computed `level`
- computed `leveling_progress`
- hydrated label payload on REST responses

Rules:

- only active joined members have realm identity
- members can update their own realm `nick` and `bio`
- custom realm profile requires realm boost level `>= 1`
- identity is realm-scoped and does not modify the account profile globally

## Realm Labels

Labels are moderator-managed realm titles stored in `SnRealmLabel`.

Label fields:

- `id`
- `realm_id`
- `name`
- `description`
- `color`
- `icon`
- `created_by_account_id`

Rules:

- each member can have one active label in v1
- only moderators or owners can create, update, delete, or assign labels
- labels must belong to the same realm as the member
- deleting a label clears it from affected members

REST endpoints:

- `GET /api/realms/{slug}/labels`
- `POST /api/realms/{slug}/labels`
- `PATCH /api/realms/{slug}/labels/{labelId}`
- `DELETE /api/realms/{slug}/labels/{labelId}`
- `PATCH /api/realms/{slug}/members/{memberId}/label`

## Realm Boosts

Realm boosts are shared, time-limited contributions funded by wallet `points`.

Boost data is tracked on:

- `SnRealm.BoostPoints`
- `SnRealm.BoostLevel`
- `SnRealmBoostContribution`

Boost purchases use order-style processing.

One boost share is worth `100` points.

Each boost contribution stays active for `30` days from the time the paid order is applied.

Boost thresholds:

- level 0: below `1000`
- level 1: `1000+`
- level 2: `5000+`
- level 3: `15000+`

Boost unlocks:

- level 1: custom realm profile and labels
- level 2: promotions above normal member
- level 3: highest label capacity tier in v1

Label capacity by boost level:

- level 0: `0`
- level 1: `10`
- level 2: `50`
- level 3: `200`

REST endpoints:

- `POST /api/realms/{slug}/boosts`
- `GET /api/realms/{slug}/boosts`
- `GET /api/realms/{slug}/boosts/leaderboard`

Behavior:

- `POST /boosts` creates a wallet order with product identifier `realms.boost`
- boost points are only applied after the payment order event is received
- each successful paid order writes a `SnRealmBoostContribution`
- realm boost level is derived from active boost points from the last `30` days
- contributions are idempotent by `order_id`
- expired contributions no longer count toward unlocks or promotion gates

Leaderboard data:

- groups active contributions by boosting account
- exposes total `amount_points`
- exposes computed `shares`
- exposes contribution count
- exposes `last_boosted_at`

## Promotions

Realm promotions are still role-based, but now gated by boost level.

Rules:

- owner creation is unchanged
- inviting or promoting a member above `Normal` requires realm boost level `>= 2`
- moderator and owner permission checks still apply on top of the boost gate

Affected flows:

- realm invite with elevated role
- realm role update

## In-Realm Leveling

Realm XP is stored per member and recorded in `SnRealmExperienceRecord`.

Record fields:

- `id`
- `realm_id`
- `account_id`
- `reason_type`
- `reason`
- `delta`
- `created_at`

XP uses the shared `Leveling` curve already used for account progression.

Current XP sources:

- tenure XP: `+5` once per day for active members who joined before the current day
- linked chat XP: `+2` for eligible non-system messages in chat rooms linked to the realm
- realm post XP: `+20` when creating a published post in the realm

Anti-abuse rules:

- tenure XP is granted by a scheduled Quartz job once per day
- chat XP is throttled by a cooldown in Passport event handling
- post XP is event-based and keyed by the post id

REST endpoint:

- `GET /api/realms/{slug}/members/{memberId}/experience`

## Cross-Service Behavior

### Passport

Passport is the source of truth for:

- realm member identity
- label definitions and assignments
- realm boost totals
- realm XP and level history

### Messager

Messager overlays realm identity onto chat members when:

- the chat room has a non-null `realm_id`
- the room member maps to an active realm member in the same realm

Overlay fields used by chat payloads:

- `realm_nick`
- `realm_bio`
- `realm_label`
- `realm_experience`
- `realm_level`
- `realm_leveling_progress`

If a realm nick exists, it overrides the chat member `nick` in the response payload.

### Sphere

Sphere publishes a realm activity event when a published realm post is created.

Passport listens to realm activity events and awards XP through `RealmExperienceService`.

## gRPC Contract Notes

`DyRealm` now carries:

- `boost_points`
- `boost_level`

`DyRealmMember` now carries:

- `nick`
- `bio`
- `label_id`
- `experience`
- `level`
- `leveling_progress`
- `label_name`
- `label_description`
- `label_color`
- `label_icon`

The existing `LoadMemberAccount` and `LoadMemberAccounts` RPCs are reused for member overlay hydration.

## Database Additions

The Passport migration adds:

- realm columns for boost tracking
- realm member columns for identity and XP
- `realm_labels`
- `realm_boost_contributions`
- `realm_experience_records`

Migration:

- `20260313183207_AddRealmIdentityBoostAndLeveling`

## Client Expectations

Client code should treat realm identity as optional.

Recommended precedence for linked chats:

1. `realm_nick`
2. chat member `nick`
3. account `nick`

Realm bio, label, and level should only be shown when present on the linked-chat member payload.

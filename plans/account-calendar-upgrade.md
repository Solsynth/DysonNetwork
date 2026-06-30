# Account calendar / notable days expansion plan

## Context
- User asked for a planning pass first for `DysonNetwork.Passport/Account/AccountEventController.cs`.
- Requested scope:
  - richer event calendar listing
  - more filters
  - preload account info
  - detail API for generated notable days
  - search API across notable days and accessible events
  - tag system for calendar events, filterable by tag
- Current code already has:
  - monthly calendar APIs in `DysonNetwork.Passport/Account/AccountEventController.cs`
  - event CRUD + countdown in the same controller
  - calendar/notable-day generation in `DysonNetwork.Passport/Account/AccountEventService.cs` and `DysonNetwork.Passport/Account/NotableDaysService.cs`
  - DB-backed notable day admin/list APIs in `DysonNetwork.Passport/Account/NotableDaysController.cs`

## Approach
- Extend the existing calendar/event flow instead of adding a parallel subsystem.
- Keep visibility rules centralized in `AccountEventService` so new listing/search/detail endpoints reuse the same access checks.
- Add the minimum event-tag data model needed for filtering/search: user-owned, free-form string tags stored on `SnUserCalendarEvent`, using the same JSONB-style storage pattern already used elsewhere.
- Add a lightweight "list my used event tags" endpoint derived from the current user's existing events instead of creating a separate tag registry/table.
- Add generated notable-day detail/search on top of `NotableDaysService` output rather than trying to persist every generated occurrence.
- Use a synthetic notable-day occurrence key (derived from region + date + source identity) for generated detail lookups.
- Limit the new cross-search endpoint to user-accessible calendar events plus notable days only; skip statuses/check-ins.

## Files to modify
- `DysonNetwork.Passport/Account/AccountEventController.cs`
- `DysonNetwork.Passport/Account/AccountEventService.cs`
- `DysonNetwork.Passport/Account/NotableDaysService.cs`
- `DysonNetwork.Shared/Models/AccountEvent.cs`
- migration / EF snapshot files for the new event `Tags` column and any response-model changes

## Reuse
- `AccountEventService.GetEventCalendar(...)` — current monthly calendar aggregation and visibility handling.
- `AccountEventService.GetUserCalendarEventsAsync(...)` — current event listing path with time-range filters.
- `AccountEventService.IsEventVisibleToUserAsync(...)` — current shared visibility check.
- `NotableDaysService.GetNotableDays(...)` — current generated notable-day source.
- `NotableDaysController.ListNotableDays(...)` — current notable-day filtering pattern (`year`, `region`, `tag`, paging).
- `FriendsController.GetOverview(...)` — current account batch preload pattern via `DyAccountService.GetAccountBatchAsync(...)`.
- `PresenceActivityController.GetActivities(...)` / `AccountEventService.GetFilteredActivities(...)` — existing filterable + paged listing/search pattern to copy instead of inventing a new one.

## Steps
- [ ] Add `List<string> Tags` to `SnUserCalendarEvent`, `UserCalendarEventDto`, `CreateCalendarEventRequest`, and `UpdateCalendarEventRequest`, plus the EF migration/snapshot update.
- [ ] Add a `GET /api/accounts/me/calendar/tags`-style endpoint that returns the authenticated user's distinct normalized event tags, sourced from their stored events.
- [ ] Update calendar event create/update flows to normalize user-provided tag strings (trim, drop empties, dedupe) instead of introducing a separate tag table.
- [ ] Extend `GetUserCalendarEventsAsync(...)` and related event query helpers to support richer filters with the smallest diff: text query, exact account filter when relevant, start/end range, visibility-aware access, and tag filtering.
- [ ] Reuse the same tag-normalization rules in create/update/filter/tag-list paths so tags stay stable enough for filtering and listing.
- [ ] Preload event owner account info on event list/search/calendar responses using existing batch account lookup patterns, so callers do not have to fan out per event.
- [ ] Expand `/api/accounts/me/calendar/events` listing filters and response shape rather than adding a second overlapping list endpoint.
- [ ] Add a new detail endpoint for generated notable-day occurrences that resolves from `NotableDaysService` using a synthetic occurrence key (`region + date + source identity`).
- [ ] Add a new search endpoint for accessible calendar events + notable days only, sharing the same filters where possible (query, date/window, event tags, notable-day tag, region, paging).
- [ ] Keep notable-day generation in `NotableDaysService`; add helper methods there for search/detail over generated occurrences instead of persisting generated rows.
- [ ] Verify owner/friend/subscription visibility still gates event results in calendar/list/search paths.

## Verification
- Build/test the Passport project after model/API changes.
- Exercise event CRUD with user-defined tags, then verify list/search endpoints filter by tag and date range.
- Verify the user tag-list endpoint returns distinct normalized tags and updates as events are created/edited/deleted.
- Verify owner vs friend vs subscribed-account visibility in calendar listing and search results.
- Verify account preload is present on returned event items without extra client fetches.
- Verify generated notable-day detail/search works for recurring and period notable days, not just DB rows.
- Verify synthetic notable-day keys are stable for lookup across list/search/detail responses.

# Calendar Events API Updates

## Overview

This document covers the newer calendar event and notable day APIs added under Passport.

Local base route:
- `/api/accounts/me/...`

Gateway / production base route:
- `/passport/accounts/me/...`

All endpoints in this doc require authentication.

## What changed

- user calendar events now support free-form tags
- event list API now supports richer filtering and cross-account access within visibility rules
- event responses now preload owner account info
- you can list the current user's used event tags
- generated notable days now have a detail API via synthetic occurrence keys
- there is now a unified search API for accessible calendar events + notable days

## Event tags

Tags are:
- free-form strings
- normalized server-side
- trimmed
- lowercased
- deduplicated
- empty values are dropped

Example input:

```json
{
  "tags": ["Work", " work ", "meeting", ""]
}
```

Stored / returned as:

```json
["meeting", "work"]
```

---

## 1) List my used calendar tags

**Endpoint**
- `GET /api/accounts/me/calendar/tags`

**Response**
```json
["birthday", "meeting", "travel", "work"]
```

**Notes**
- returns distinct normalized tags from the authenticated user's non-deleted events
- no pagination

---

## 2) List accessible calendar events with filters

**Endpoint**
- `GET /api/accounts/me/calendar/events`

**Query params**
- `accountId` (guid, optional) - restrict to one accessible account
- `query` (string, optional) - matches title, description, location, or exact normalized tag
- `tags` (repeatable query param, optional) - filter by one or more tags
- `startTime` (ISO 8601 instant, optional)
- `endTime` (ISO 8601 instant, optional)
- `offset` (int, optional, default `0`)
- `take` (int, optional, default `50`)

**Examples**
- `/api/accounts/me/calendar/events?query=meeting`
- `/api/accounts/me/calendar/events?tags=work&tags=travel`
- `/api/accounts/me/calendar/events?accountId=<friend-guid>&startTime=2026-07-01T00:00:00Z&endTime=2026-07-31T23:59:59Z`

**Visibility rules**
The caller can see:
- their own events
- friends' `public` and `friends` events
- subscribed accounts' `public` events

If `accountId` is supplied for an inaccessible account, the result is empty.

**Response headers**
- `X-Total` - total matched rows after visibility + filter handling

**Response shape**
```json
[
  {
    "id": "7d5d4c4e-8e25-4e40-9c1b-8f2f2d4f0d11",
    "title": "Team sync",
    "description": "Weekly product sync",
    "location": "Room A",
    "start_time": "2026-07-03T09:00:00Z",
    "end_time": "2026-07-03T10:00:00Z",
    "is_all_day": false,
    "visibility": "Friends",
    "recurrence": null,
    "tags": ["meeting", "work"],
    "meta": {
      "meeting_link": "https://example.com/meet"
    },
    "icon": null,
    "background": null,
    "account_id": "0d5f32ba-e6b6-4dcf-8d85-4c5345c5db1a",
    "account": {
      "id": "0d5f32ba-e6b6-4dcf-8d85-4c5345c5db1a",
      "name": "alice",
      "nick": "Alice"
    },
    "created_at": "2026-07-01T10:00:00Z",
    "updated_at": "2026-07-01T10:00:00Z"
  }
]
```

**Notes**
- recurring events are expanded when a time window is provided
- `account` is preloaded so the client does not need a separate account lookup

---

## 3) Create / update calendar events with tags

### Create

**Endpoint**
- `POST /api/accounts/me/calendar/events`

**Request body**
```json
{
  "title": "Flight to Tokyo",
  "description": "Business trip",
  "location": "TPE Airport",
  "start_time": "2026-08-10T01:00:00Z",
  "end_time": "2026-08-10T05:00:00Z",
  "is_all_day": false,
  "visibility": "Private",
  "tags": ["travel", "work"],
  "meta": {
    "airline": "BR"
  },
  "icon_id": null,
  "background_id": null
}
```

### Update

**Endpoint**
- `PUT /api/accounts/me/calendar/events/{id}`

**Request body**
```json
{
  "tags": ["travel", "important"],
  "location": "HND Airport"
}
```

**Notes**
- `tags` are optional on create and update
- update replaces the stored tag set when `tags` is provided
- returned event includes preloaded `account`

---

## 4) Get generated notable day detail

Generated notable days are not stored one-row-per-occurrence, so detail lookup uses a synthetic `occurrenceKey`.

**Endpoint**
- `GET /api/accounts/me/calendar/notable-days/{occurrenceKey}`

**Occurrence key format**
- `region|yyyy-MM-dd|source-identity`

Example:
- `us|2026-12-25|christmas`
- `cn|2026-03-16|anniversary`

**Response**
```json
{
  "date": "2026-12-25T00:00:00Z",
  "local_name": "Christmas",
  "global_name": "Christmas",
  "localizable_key": "Christmas",
  "country_code": null,
  "description": null,
  "meta": null,
  "occurrence_key": "us|2026-12-25|christmas",
  "holidays": ["Public"],
  "tags": ["Holiday"]
}
```

**Notes**
- works for generated recurring notable days too
- returns `404` when the occurrence key cannot be resolved

---

## 5) Search accessible calendar events + notable days

**Endpoint**
- `GET /api/accounts/me/calendar/search`

**Query params**
- `query` (string, optional)
- `accountId` (guid, optional) - restrict event-side results to one accessible account
- `tags` (repeatable, optional) - event tag filter
- `startTime` (ISO 8601 instant, optional)
- `endTime` (ISO 8601 instant, optional)
- `notableDayTag` (enum, optional) - one of `Holiday`, `Event`, `Anniversary`, `Memorial`, `Festival`
- `region` (string, optional) - defaults to current user's region, then `us`
- `offset` (int, optional, default `0`)
- `take` (int, optional, default `50`)

**Examples**
- `/api/accounts/me/calendar/search?query=christmas`
- `/api/accounts/me/calendar/search?tags=work&startTime=2026-07-01T00:00:00Z&endTime=2026-07-31T23:59:59Z`
- `/api/accounts/me/calendar/search?notableDayTag=Holiday&region=us`

**Response headers**
- `X-Total`

**Response shape**
```json
[
  {
    "type": "UserEvent",
    "start_time": "2026-07-03T09:00:00Z",
    "end_time": "2026-07-03T10:00:00Z",
    "user_event": {
      "id": "7d5d4c4e-8e25-4e40-9c1b-8f2f2d4f0d11",
      "title": "Team sync",
      "tags": ["meeting", "work"],
      "account_id": "0d5f32ba-e6b6-4dcf-8d85-4c5345c5db1a",
      "account": {
        "id": "0d5f32ba-e6b6-4dcf-8d85-4c5345c5db1a",
        "name": "alice",
        "nick": "Alice"
      }
    },
    "notable_day": null
  },
  {
    "type": "NotableDay",
    "start_time": "2026-12-25T00:00:00Z",
    "end_time": "2026-12-25T00:00:00Z",
    "user_event": null,
    "notable_day": {
      "date": "2026-12-25T00:00:00Z",
      "local_name": "Christmas",
      "global_name": "Christmas",
      "occurrence_key": "us|2026-12-25|christmas",
      "tags": ["Holiday"]
    }
  }
]
```

**Notes**
- this endpoint only searches calendar events and notable days
- it does **not** include statuses or check-ins
- results are merged and ordered by start time

---

## 6) Calendar monthly responses now include richer event payloads

These existing endpoints now return richer `user_events` items:
- `GET /api/accounts/me/calendar`
- `GET /api/accounts/me/calendar/merged`

Each `user_events[]` item may now include:
- `tags`
- `icon`
- `background`
- `account`

This is especially useful when the month view includes friend/subscription events.

---

## Error notes

Common errors:
- `401 Unauthorized` - missing or invalid auth
- `400 Bad Request` - invalid query values such as malformed `notableDayTag`
- `404 Not Found` - event or generated notable day occurrence does not exist

## Related docs

- `docs/CALENDAR_EVENTS_API.md`
- `docs/CALENDAR_EVENT_SUBSCRIPTIONS.md`
- `docs/NOTABLE_DAYS.md`

# Notable Days & Calendar Events System

## Overview

The Notable Days system provides a local database-driven approach to managing holidays, events, and festivals. It replaces the previous remote Nager.Holiday API with a fully customizable solution that supports:

- **Multi-day periods** (e.g., Labour Day 5-day holiday, Spring Festival 7-day holiday)
- **Tag-based categorization** (Holiday, Event, Anniversary, Memorial, Festival)
- **Recurring events** with lunar calendar support
- **Localization** via `localizable_key` for client-side translations
- **Region-specific** holidays (defaults to "CN" for China)

## Architecture

```
┌─────────────────┐     ┌──────────────────┐     ┌─────────────────┐
│   Client App    │────▶│   REST API       │────▶│   Database      │
│                 │     │   (Passport)     │     │   (PostgreSQL)  │
└─────────────────┘     └──────────────────┘     └─────────────────┘
                              │
                              ▼
                        ┌──────────────────┐
                        │   gRPC Service   │
                        │   (Internal)     │
                        └──────────────────┘
```

## Data Model

### SnNotableDay

| Field | Type | Description |
|-------|------|-------------|
| `id` | UUID | Primary key |
| `name` | string | Event name (English) |
| `description` | string | Event description |
| `local_name` | string | Localized name (e.g., Chinese) |
| `localizable_key` | string | Key for client-side localization |
| `start_date` | Instant | Start date |
| `end_date` | Instant | End date |
| `is_all_day` | bool | Whether this is an all-day event |
| `region` | string | Region code (e.g., "CN", "US") |
| `tags` | JSON array | Tags: Holiday, Event, Anniversary, Memorial, Festival |
| `meta` | JSON | Additional metadata |
| `is_recurring` | bool | Whether this recurs annually |
| `recurrence_pattern` | string | MM-DD format for solar, or lunar pattern |
| `is_period` | bool | Whether this spans multiple days |
| `holiday_days` | JSON array | Which days in period are actual holidays (MM-DD) |
| `display_order` | int | Sort order |

### NotableDayTag Enum

| Tag | Description | Example |
|-----|-------------|---------|
| `Holiday` | Public holidays with days off | Spring Festival, Labour Day |
| `Event` | Non-holiday observances | Arbor Day, Youth Day |
| `Anniversary` | Annual commemorations | Company founding |
| `Memorial` | Memorial/rememberance days | |
| `Festival` | Cultural festivals | Qixi, Mid-Autumn |

## REST API

### Base URL: `/api/notable-days`

### List Notable Days

```
GET /api/notable-days?year=2026&region=CN&tag=Holiday
```

**Query Parameters:**
- `year` (int, optional) - Year, defaults to current
- `region` (string, optional) - Region code, defaults to "CN"
- `tag` (string, optional) - Filter by tag
- `offset` (int, optional) - Pagination offset
- `take` (int, optional) - Number of results, defaults to 50

**Response:**
```json
[
  {
    "id": "uuid",
    "name": "Spring Festival",
    "local_name": "春节",
    "localizable_key": "SpringFestival",
    "start_date": "2026-01-28T00:00:00Z",
    "end_date": "2026-02-04T00:00:00Z",
    "is_period": true,
    "holiday_days": ["01-28", "01-29", "01-30", "01-31", "02-01", "02-02", "02-03"],
    "tags": ["Holiday", "Festival"],
    "region": "CN"
  }
]
```

### Get Notable Day by ID

```
GET /api/notable-days/{id}
```

### Create Notable Day

```
POST /api/notable-days
```

**Request Body:**
```json
{
  "name": "Spring Festival",
  "local_name": "春节",
  "localizable_key": "SpringFestival",
  "description": "Chinese New Year",
  "start_date": "2026-01-28T00:00:00Z",
  "end_date": "2026-02-04T00:00:00Z",
  "region": "CN",
  "tags": ["Holiday", "Festival"],
  "is_recurring": true,
  "recurrence_pattern": "01-01",
  "is_period": true,
  "holiday_days": ["01-28", "01-29", "01-30", "01-31", "02-01", "02-02", "02-03"],
  "display_order": 1
}
```

### Update Notable Day

```
PUT /api/notable-days/{id}
```

### Delete Notable Day

```
DELETE /api/notable-days/{id}
```

---

## Countdown API

### Get Countdown Events

```
GET /api/accounts/me/calendar/countdown?take=10&tag=Holiday
```

**Query Parameters:**
- `take` (int, optional) - Number of events, defaults to 5
- `offset` (int, optional) - Pagination offset
- `includeNotableDays` (bool, optional) - Include notable days, defaults to true
- `tag` (string, optional) - Filter notable days by tag

**Response:**
```json
[
  {
    "event_id": "uuid",
    "type": "UserEvent",
    "title": "Team Meeting",
    "start_time": "2026-06-02T14:00:00Z",
    "end_time": "2026-06-02T15:00:00Z",
    "is_all_day": false,
    "days_remaining": 0,
    "hours_remaining": 0,
    "is_ongoing": true
  },
  {
    "event_id": "uuid",
    "type": "NotableDay",
    "title": "Spring Festival",
    "start_time": "2026-01-28T00:00:00Z",
    "end_time": "2026-02-04T00:00:00Z",
    "is_all_day": true,
    "days_remaining": 120,
    "hours_remaining": 0,
    "is_ongoing": false,
    "tags": ["Holiday", "Festival"]
  }
]
```

**Sorting:** Ongoing events first, then by proximity to now.

---

## gRPC Service

### Service: `DyProfileService`

#### GetNotableDays

```protobuf
rpc GetNotableDays(DyGetNotableDaysRequest) returns (DyGetNotableDaysResponse);
```

#### GetUserCalendarEvents

```protobuf
rpc GetUserCalendarEvents(DyGetUserCalendarEventsRequest) returns (DyGetUserCalendarEventsResponse);
```

#### GetCountdownEvents

```protobuf
rpc GetCountdownEvents(DyGetCountdownEventsRequest) returns (DyGetCountdownEventsResponse);
```

---

## Embeds

### NotableDayEmbed

Used in posts and chat messages to reference notable days.

```json
{
  "type": "notable_day",
  "id": "uuid"
}
```

### CalendarEventEmbed

Used in posts and chat messages to reference user calendar events.

```json
{
  "type": "calendar_event",
  "id": "uuid"
}
```

**Client-side:** Use the ID to fetch full details from:
- `GET /api/notable-days/{id}`
- `GET /api/accounts/me/calendar/events/{id}`

---

## Pre-seeded Chinese Holidays

| Holiday | Local Name | Period | Lunar/Solar | Tags |
|---------|-----------|--------|-------------|------|
| Spring Festival | 春节 | 7 days | Lunar 01-01 | Holiday, Festival |
| Qingming Festival | 清明节 | 3 days | Solar 04-04 | Holiday, Festival |
| Labour Day | 劳动节 | 5 days | Solar 05-01 | Holiday |
| Dragon Boat Festival | 端午节 | 3 days | Lunar 05-05 | Holiday, Festival |
| Mid-Autumn Festival | 中秋节 | 3 days | Lunar 08-15 | Holiday, Festival |
| National Day | 国庆节 | 7 days | Solar 10-01 | Holiday |
| New Year's Day | 元旦 | 3 days | Solar 01-01 | Holiday |
| Arbor Day | 植树节 | 1 day | Solar 03-12 | Event |
| Youth Day | 五四青年节 | 1 day | Solar 05-04 | Event, Memorial |
| Children's Day | 儿童节 | 1 day | Solar 06-01 | Event |
| Teachers' Day | 教师节 | 1 day | Solar 09-10 | Event |
| Qixi Festival | 七夕节 | 1 day | Lunar 07-07 | Festival |
| Double Ninth Festival | 重阳节 | 1 day | Lunar 09-09 | Festival |

---

## Localization

The `localizable_key` field allows clients to provide translations. Example keys:

| Key | English | Chinese | Japanese |
|-----|---------|---------|----------|
| `SpringFestival` | Spring Festival | 春节 | 春節 |
| `QingmingFestival` | Qingming Festival | 清明节 | 清明節 |
| `LabourDay` | Labour Day | 劳动节 | 労働記念日 |
| `DragonBoatFestival` | Dragon Boat Festival | 端午节 | 端午の節句 |
| `MidAutumnFestival` | Mid-Autumn Festival | 中秋节 | 中秋の名月 |
| `NationalDay` | National Day | 国庆节 | 国慶節 |

**Client implementation:**
```javascript
function getLocalizedName(notableDay, locale) {
  // Try localized key first, fall back to local_name, then name
  const key = notableDay.localizable_key;
  if (key && translations[locale]?.[key]) {
    return translations[locale][key];
  }
  return notableDay.local_name || notableDay.name;
}
```

---

## Database Migration

Run the EF Core migration to create the `notable_days` table:

```bash
dotnet ef database update --project DysonNetwork.Passport
```

The seed data is automatically loaded on application startup via `NotableDaysSeedService`.

---

## Implementation Notes

- Multi-day holidays generate individual `NotableDay` entries for each day in the period
- The `holiday_days` field specifies which days are actual holidays (for proper marking)
- Recurring events use `recurrence_pattern` in MM-DD format
- Lunar calendar dates are approximate (fixed solar dates for each year)
- Cache is invalidated when notable days are modified
- The gRPC service is registered as `CalendarServiceGrpc` in Passport

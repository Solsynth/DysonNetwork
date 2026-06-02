# Calendar Events API Documentation

## Overview

The Calendar Events API allows users to create, manage, and share personal calendar events with granular visibility controls. Events can be viewed in a merged calendar format alongside check-ins, status updates, and notable days (holidays).

This service is handled by the DysonNetwork.Passport service. When using with the gateway, replace `/api` with `/pass`.

**Note:** Status endpoints (`/statuses`, `/check-in`, `/calendar`) are now handled by `AccountEventController`. Profile-related endpoints remain in `AccountCurrentController`.

## Key Features

- **User-Created Events**: Create personal calendar events with title, description, location, and time
- **Visibility Controls**: Three levels - Public (everyone), Friends (friends only), Private (owner only)
- **Recurring Events**: Support for daily, weekly, monthly, and yearly recurrence patterns
- **Merged Calendar View**: Combines user events, check-ins, status updates, and notable days
- **24-Hour Caching**: Events cached with automatic invalidation on updates
- **Timezone Aware**: All times stored in UTC, converted by clients

## Naming Conventions

- **Query Parameters**: Use `camelCase` (e.g., `includeNotableDays`, `startTime`)
- **Request/Response Body**: Use `snake_case` (e.g., `start_time`, `is_all_day`)
- **Path Parameters**: Use `camelCase` (e.g., `accountId`, `eventId`)

## API Endpoints

### Base URL: `/api/accounts/me/calendar`

### Authentication
All endpoints require `[Authorize]` header. User context is automatically extracted.

---

## Get Calendar

Retrieve the daily calendar for the authenticated user, including check-ins, status updates, and user events.

**Endpoint:** `GET /api/accounts/me/calendar`

**Query Parameters:**
- `month` (int, optional) - Month number (1-12), defaults to current month
- `year` (int, optional) - Year, defaults to current year
- `includeNotableDays` (bool, optional) - Include holidays from user's region

**Response:**
```json
[
  {
    "date": "2026-04-21T00:00:00Z",
    "check_in_result": {
      "id": "550e8400-e29b-41d4-a716-446655440000",
      "level": "Better",
      "reward_points": 10,
      "reward_experience": 100,
      "tips": [
        {
          "is_positive": true,
          "title": "Great Fortune",
          "content": "Today brings unexpected opportunities"
        }
      ],
      "account_id": "user-guid",
      "created_at": "2026-04-21T08:30:00Z"
    },
    "statuses": [
      {
        "id": "660e8400-e29b-41d4-a716-446655440001",
        "attitude": "Positive",
        "type": "Default",
        "label": "Feeling great today!",
        "symbol": "☀️",
        "icon": {
          "id": "file-guid",
          "name": "happy.png",
          "mime_type": "image/png",
          "size": 12345,
          "url": "/drive/files/file-guid"
        },
        "background": {
          "id": "file-guid-2",
          "name": "sunset.jpg",
          "mime_type": "image/jpeg",
          "size": 54321,
          "url": "/drive/files/file-guid-2"
        },
        "created_at": "2026-04-21T09:00:00Z"
      }
    ],
    "user_events": [
      {
        "id": "770e8400-e29b-41d4-a716-446655440002",
        "title": "Team Meeting",
        "description": "Weekly sync with the engineering team",
        "location": "Conference Room A",
        "start_time": "2026-04-21T14:00:00Z",
        "end_time": "2026-04-21T15:00:00Z",
        "is_all_day": false,
        "visibility": "Friends",
        "recurrence": null,
        "meta": {
          "meeting_link": "https://meet.example.com/abc123"
        },
        "account_id": "user-guid",
        "created_at": "2026-04-20T10:00:00Z",
        "updated_at": "2026-04-20T10:00:00Z"
      }
    ],
    "notable_days": [
      {
        "date": "2026-04-21T00:00:00Z",
        "local_name": "复活节",
        "global_name": "Easter Monday",
        "localizable_key": "EasterMonday",
        "country_code": "CN",
        "holidays": ["Public", "Bank"]
      }
    ]
  }
]
```

**Response Codes:**
- `200 OK` - Success, returns array of daily events
- `400 Bad Request` - Invalid month or year parameters
- `401 Unauthorized` - Invalid or missing authentication

---

## Get Merged Calendar

Retrieve a flattened, merged view of all calendar events for easier consumption.

**Endpoint:** `GET /api/accounts/me/calendar/merged`

**Query Parameters:**
- `month` (int, optional) - Month number (1-12), defaults to current month
- `year` (int, optional) - Year, defaults to current year

**Response:**
```json
{
  "date": "2026-04-21T00:00:00Z",
  "check_in_result": { ... },
  "statuses": [ ... ],
  "user_events": [ ... ],
  "notable_days": [ ... ],
  "merged_events": [
    {
      "id": "550e8400-e29b-41d4-a716-446655440000",
      "type": "CheckIn",
      "title": "Check-in: Better",
      "description": "Daily check-in result: Better",
      "start_time": "2026-04-21T08:30:00Z",
      "end_time": "2026-04-21T08:30:00Z",
      "is_all_day": true,
      "meta": {
        "level": "Better",
        "reward_points": 10,
        "reward_experience": 100
      }
    },
    {
      "id": "770e8400-e29b-41d4-a716-446655440002",
      "type": "UserEvent",
      "title": "Team Meeting",
      "description": "Weekly sync with the engineering team",
      "location": "Conference Room A",
      "start_time": "2026-04-21T14:00:00Z",
      "end_time": "2026-04-21T15:00:00Z",
      "is_all_day": false,
      "meta": {
        "meeting_link": "https://meet.example.com/abc123"
      }
    },
    {
      "type": "NotableDay",
      "title": "Easter Monday",
      "description": "复活节",
      "start_time": "2026-04-21T00:00:00Z",
      "end_time": "2026-04-22T00:00:00Z",
      "is_all_day": true,
      "meta": {
        "local_name": "复活节",
        "country_code": "CN",
        "holiday_types": ["Public", "Bank"]
      }
    }
  ]
}
```

**Merged Event Types:**
- `UserEvent` - User-created calendar events
- `CheckIn` - Daily check-in results
- `Status` - Status updates
- `NotableDay` - Holidays and special days

**Response Codes:**
- `200 OK` - Success
- `400 Bad Request` - Invalid parameters
- `401 Unauthorized` - Invalid authentication

---

## List Calendar Events

Retrieve all calendar events for the authenticated user with pagination.

**Endpoint:** `GET /api/accounts/me/calendar/events`

**Query Parameters:**
- `startTime` (ISO 8601 timestamp, optional) - Filter events starting after this time
- `endTime` (ISO 8601 timestamp, optional) - Filter events ending before this time
- `offset` (int, optional) - Pagination offset, defaults to 0
- `take` (int, optional) - Number of results to return, defaults to 50

**Response:**
```json
[
  {
    "id": "770e8400-e29b-41d4-a716-446655440002",
    "title": "Team Meeting",
    "description": "Weekly sync with the engineering team",
    "location": "Conference Room A",
    "start_time": "2026-04-21T14:00:00Z",
    "end_time": "2026-04-21T15:00:00Z",
    "is_all_day": false,
    "visibility": "Friends",
    "recurrence": {
      "frequency": "Weekly",
      "interval": 1,
      "end_date": null,
      "occurrences": null,
      "days_of_week": ["Monday"],
      "day_of_month": null,
      "month_of_year": null
    },
    "meta": {
      "meeting_link": "https://meet.example.com/abc123"
    },
    "account_id": "user-guid",
    "created_at": "2026-04-20T10:00:00Z",
    "updated_at": "2026-04-20T10:00:00Z"
  }
]
```

**Response Headers:**
- `X-Total` - Total number of events matching the query

**Response Codes:**
- `200 OK` - Success
- `401 Unauthorized` - Invalid authentication

---

## Create Calendar Event

Create a new calendar event.

**Endpoint:** `POST /api/accounts/me/calendar/events`

**Request Body:**
```json
{
  "title": "Team Meeting",
  "description": "Weekly sync with the engineering team",
  "location": "Conference Room A",
  "start_time": "2026-04-21T14:00:00Z",
  "end_time": "2026-04-21T15:00:00Z",
  "is_all_day": false,
  "visibility": "Friends",
  "recurrence": {
    "frequency": "Weekly",
    "interval": 1,
    "days_of_week": ["Monday"]
  },
  "meta": {
    "meeting_link": "https://meet.example.com/abc123"
  },
  "icon_id": "file-guid",
  "background_id": "file-guid-2"
}
```

**Field Details:**
- `title` (required, max 256 chars) - Event title
- `description` (optional, max 4096 chars) - Event description
- `location` (optional, max 512 chars) - Event location
- `start_time` (required, ISO 8601) - Event start time in UTC
- `end_time` (required, ISO 8601) - Event end time in UTC (must be after start)
- `is_all_day` (optional, default: false) - Whether this is an all-day event
- `visibility` (optional, default: "Private") - One of: "Private", "Friends", "Public"
- `recurrence` (optional) - Recurrence pattern object
- `meta` (optional) - Custom metadata dictionary
- `icon_id` (optional) - File ID for event icon (from shared attachment system)
- `background_id` (optional) - File ID for event background image

**Recurrence Pattern:**
- `frequency` (required if recurrence provided) - "None", "Daily", "Weekly", "Monthly", "Yearly"
- `interval` (optional, default: 1) - Interval between occurrences
- `end_date` (optional, ISO 8601) - When recurrence ends
- `occurrences` (optional, int) - Maximum number of occurrences
- `days_of_week` (optional, array) - For weekly recurrence: ["Monday", "Wednesday", "Friday"]
- `day_of_month` (optional, int) - For monthly/yearly recurrence
- `month_of_year` (optional, int) - For yearly recurrence

**Response:** Returns the created `SnUserCalendarEvent` object (201 Created)

**Response Codes:**
- `201 Created` - Event created successfully
- `400 Bad Request` - Invalid data (e.g., end_time before start_time)
- `401 Unauthorized` - Invalid authentication

**Example cURL:**
```bash
curl -X POST "/api/accounts/me/calendar/events" \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{
    "title": "Birthday Party",
    "start_time": "2026-05-15T18:00:00Z",
    "end_time": "2026-05-15T22:00:00Z",
    "visibility": "Friends",
    "location": "My House"
  }'
```

---

## Get Calendar Event

Retrieve a specific calendar event by ID.

**Endpoint:** `GET /api/accounts/me/calendar/events/{id}`

**Path Parameters:**
- `id` (GUID, required) - Event ID

**Response:** Returns the `SnUserCalendarEvent` object

**Response Codes:**
- `200 OK` - Success
- `401 Unauthorized` - Invalid authentication
- `404 Not Found` - Event not found or not visible to user

---

## Update Calendar Event

Update an existing calendar event.

**Endpoint:** `PUT /api/accounts/me/calendar/events/{id}`

**Path Parameters:**
- `id` (GUID, required) - Event ID

**Request Body:** (all fields optional)
```json
{
  "title": "Updated: Team Meeting",
  "description": "Updated description",
  "location": "Conference Room B",
  "start_time": "2026-04-21T15:00:00Z",
  "end_time": "2026-04-21T16:00:00Z",
  "is_all_day": false,
  "visibility": "Public",
  "recurrence": null,
  "meta": {
    "meeting_link": "https://meet.example.com/updated"
  },
  "icon_id": "file-guid",
  "background_id": "file-guid-2"
}
```

**Response:** Returns the updated `SnUserCalendarEvent` object

**Response Codes:**
- `200 OK` - Event updated successfully
- `400 Bad Request` - Invalid data
- `401 Unauthorized` - Invalid authentication
- `404 Not Found` - Event not found or doesn't belong to user

---

## Delete Calendar Event

Soft-delete a calendar event.

**Endpoint:** `DELETE /api/accounts/me/calendar/events/{id}`

**Path Parameters:**
- `id` (GUID, required) - Event ID

**Response:** No content (204)

**Response Codes:**
- `204 No Content` - Event deleted successfully
- `401 Unauthorized` - Invalid authentication
- `404 Not Found` - Event not found or doesn't belong to user

---

## View Other User's Calendar

View another user's public calendar (respects visibility settings).

**Endpoint:** `GET /api/accounts/{name}/calendar`

**Path Parameters:**
- `name` (string, required) - Account name/username

**Query Parameters:**
- `month` (int, optional) - Month number (1-12)
- `year` (int, optional) - Year
- `includeNotableDays` (bool, optional) - Include holidays

**Response:** Same format as `/api/accounts/me/calendar`

**Visibility Rules:**
- Private events are never shown to others
- Friends-only events are shown only if viewer is a friend
- Public events are shown to everyone
- If viewer is not authenticated, only public events are visible

**Response Codes:**
- `200 OK` - Success
- `400 Bad Request` - Invalid parameters or user not found

---

## View Other User's Merged Calendar

View another user's merged calendar (respects visibility settings).

**Endpoint:** `GET /api/accounts/{name}/calendar/merged`

**Path Parameters:**
- `name` (string, required) - Account name/username

**Query Parameters:**
- `month` (int, optional) - Month number (1-12)
- `year` (int, optional) - Year

**Response:** Same format as `/api/accounts/me/calendar/merged`

**Response Codes:**
- `200 OK` - Success
- `400 Bad Request` - Invalid parameters or user not found

---

## View Other User's Calendar Events

List another user's calendar events with pagination. Respects visibility rules based on the viewer's relationship to the account owner.

**Endpoint:** `GET /api/accounts/{name}/calendar/events`

**Path Parameters:**
- `name` (string, required) - Account name/username

**Query Parameters:**
- `startTime` (ISO 8601 timestamp, optional) - Filter events starting after this time
- `endTime` (ISO 8601 timestamp, optional) - Filter events ending before this time
- `offset` (int, optional) - Pagination offset, defaults to 0
- `take` (int, optional) - Number of results to return, defaults to 50

**Response:** Same format as `/api/accounts/me/calendar/events`, with each event including the resolved account object (with profile).

**Visibility Rules:**
- Unauthenticated users see only `Public` events
- Authenticated non-friends see only `Public` events
- Friends see `Public` and `Friends` events
- The account owner sees all events (when authenticated as themselves)

**Response Headers:**
- `X-Total` - Total number of events matching the query

**Response Codes:**
- `200 OK` - Success
- `400 Bad Request` - User not found

---

## View Specific Calendar Event by Username

Retrieve a specific calendar event belonging to another user.

**Endpoint:** `GET /api/accounts/{name}/calendar/events/{id}`

**Path Parameters:**
- `name` (string, required) - Account name/username
- `id` (GUID, required) - Event ID

**Response:** Returns the `SnUserCalendarEvent` object with the resolved account (including profile).

**Visibility Rules:**
- Same as "View Other User's Calendar Events" above
- Returns 404 if the event doesn't belong to the specified account or is not visible to the viewer

**Response Codes:**
- `200 OK` - Success
- `400 Bad Request` - User not found
- `404 Not Found` - Event not found or not visible

---

## Status Management

### Create/Update Status

Create or update the user's status with optional icon and background images.

**Create Endpoint:** `POST /api/accounts/me/statuses`
**Update Endpoint:** `PATCH /api/accounts/me/statuses`

**Request Body:**
```json
{
  "attitude": "Positive",
  "type": "Default",
  "label": "Working on new features",
  "symbol": "💻",
  "icon_id": "file-guid",
  "background_id": "file-guid-2",
  "is_automated": false,
  "meta": {}
}
```

**Field Details:**
- `attitude` (required) - "Positive", "Negative", or "Neutral"
- `type` (optional, default: "Default") - "Default", "Busy", "DoNotDisturb", "Invisible"
- `label` (optional, max 1024 chars) - Status text
- `symbol` (optional, max 128 chars) - Emoji or symbol
- `icon_id` (optional, max 32 chars) - File ID for status icon (from shared attachment system)
- `background_id` (optional, max 32 chars) - File ID for status background image
- `is_automated` (optional) - Whether this is an automated status
- `app_identifier` (optional, max 4096 chars) - For automated statuses
- `meta` (optional) - Custom metadata dictionary
- `cleared_at` (optional, ISO 8601) - When to clear the status

**Response:** Returns the `SnAccountStatus` object with resolved icon/background objects

---

### Get Current Status

**Endpoint:** `GET /api/accounts/me/statuses`

**Response:**
```json
{
  "id": "660e8400-e29b-41d4-a716-446655440001",
  "attitude": "Positive",
  "type": "Default",
  "label": "Working on new features",
  "symbol": "💻",
  "icon": {
    "id": "file-guid",
    "name": "happy.png",
    "mime_type": "image/png",
    "size": 12345,
    "url": "/drive/files/file-guid"
  },
  "background": {
    "id": "file-guid-2",
    "name": "sunset.jpg",
    "mime_type": "image/jpeg",
    "size": 54321,
    "url": "/drive/files/file-guid-2"
  },
  "is_online": true,
  "is_idle": false,
  "account_id": "user-guid",
  "created_at": "2026-04-21T09:00:00Z"
}
```

---

### Delete Status

**Endpoint:** `DELETE /api/accounts/me/statuses`

**Query Parameters:**
- `app` (string, optional) - Delete only automated statuses from this app

**Response:** No content (204)

---

## Event Countdown

Get upcoming events with countdown information, including currently ongoing events.

**Endpoint:** `GET /api/accounts/me/calendar/countdown`

**Query Parameters:**
- `take` (int, optional, default: 5) - Number of events to return
- `offset` (int, optional, default: 0) - Pagination offset
- `includeNotableDays` (bool, optional, default: true) - Include notable days (holidays/events)
- `tag` (string, optional) - Filter notable days by tag: "Holiday", "Event", "Anniversary", "Memorial", "Festival"

**Response:**
```json
[
  {
    "event_id": "770e8400-e29b-41d4-a716-446655440002",
    "type": "UserEvent",
    "title": "Team Meeting",
    "description": "Weekly sync with the engineering team",
    "location": "Conference Room A",
    "start_time": "2026-06-02T14:00:00Z",
    "end_time": "2026-06-02T15:00:00Z",
    "is_all_day": false,
    "days_remaining": 0,
    "hours_remaining": 0,
    "is_ongoing": true,
    "meta": {},
    "account_id": "user-guid"
  },
  {
    "event_id": "880e8400-e29b-41d4-a716-446655440003",
    "type": "UserEvent",
    "title": "Birthday Party",
    "description": null,
    "location": "My House",
    "start_time": "2026-06-15T18:00:00Z",
    "end_time": "2026-06-15T22:00:00Z",
    "is_all_day": false,
    "days_remaining": 13,
    "hours_remaining": 4,
    "is_ongoing": false,
    "meta": {},
    "account_id": "user-guid"
  }
]
```

**Sorting Behavior:**
- Ongoing events (`is_ongoing: true`) are always returned first
- Upcoming events are sorted by distance from now (closest first)
- Includes user calendar events and notable days (if region configured)

**Response Headers:**
- `X-Total` - Total number of events matching the query

**Example Requests:**
```
GET /api/accounts/me/calendar/countdown?take=10
GET /api/accounts/me/calendar/countdown?tag=Holiday&includeNotableDays=true
GET /api/accounts/me/calendar/countdown?tag=Festival&offset=5&take=10
```

**Response Codes:**
- `200 OK` - Success
- `401 Unauthorized` - Invalid authentication

---

## Data Models

### EventVisibility Enum
```csharp
public enum EventVisibility
{
    Private = 0,   // Only owner can see
    Friends = 100, // Friends can see
    Public = 200   // Everyone can see
}
```

### RecurrenceFrequency Enum
```csharp
public enum RecurrenceFrequency
{
    None,    // No recurrence
    Daily,   // Every N days
    Weekly,  // Every N weeks on specific days
    Monthly, // Every N months on specific day
    Yearly   // Every N years on specific date
}
```

### SnUserCalendarEvent
```json
{
  "id": "uuid",
  "title": "string (max 256)",
  "description": "string (max 4096, optional)",
  "location": "string (max 512, optional)",
  "start_time": "ISO 8601 timestamp (UTC)",
  "end_time": "ISO 8601 timestamp (UTC)",
  "is_all_day": "boolean",
  "visibility": "Private | Friends | Public",
  "recurrence": {
    "frequency": "None | Daily | Weekly | Monthly | Yearly",
    "interval": "integer (default: 1)",
    "end_date": "ISO 8601 timestamp (optional)",
    "occurrences": "integer (optional, max occurrences)",
    "days_of_week": ["Monday", "Tuesday", ...] (optional),
    "day_of_month": "integer (optional, 1-31)",
    "month_of_year": "integer (optional, 1-12)"
  },
  "meta": "object (optional, JSON)",
  "icon": "SnCloudFileReferenceObject | null",
  "background": "SnCloudFileReferenceObject | null",
  "account_id": "uuid",
  "created_at": "ISO 8601 timestamp",
  "updated_at": "ISO 8601 timestamp",
  "deleted_at": "ISO 8601 timestamp (null if not deleted)"
}
```

### DailyEventResponse
```json
{
  "date": "ISO 8601 timestamp",
  "check_in_result": "SnCheckInResult | null",
  "statuses": ["SnAccountStatus"],
  "user_events": ["UserCalendarEventDto"],
  "notable_days": ["NotableDay"]
}
```

### MergedCalendarEvent
```json
{
  "id": "uuid | null",
  "type": "UserEvent | CheckIn | Status | NotableDay",
  "title": "string",
  "description": "string | null",
  "location": "string | null",
  "start_time": "ISO 8601 timestamp",
  "end_time": "ISO 8601 timestamp",
  "is_all_day": "boolean",
  "meta": "object | null"
}
```

### EventCountdownItem
```json
{
  "event_id": "uuid | null",
  "type": "UserEvent | NotableDay",
  "title": "string",
  "description": "string | null",
  "location": "string | null",
  "start_time": "ISO 8601 timestamp",
  "end_time": "ISO 8601 timestamp",
  "is_all_day": "boolean",
  "days_remaining": "integer",
  "hours_remaining": "integer",
  "is_ongoing": "boolean",
  "meta": "object | null",
  "account_id": "uuid | null"
}
```

### NotableDay
```json
{
  "date": "ISO 8601 timestamp",
  "local_name": "string | null",
  "global_name": "string | null",
  "localizable_key": "string | null",
  "country_code": "string | null",
  "holidays": ["Public", "Bank", "School", "Authorities", "Optional", "Observance"],
  "tags": ["Holiday", "Event", "Anniversary", "Memorial", "Festival"]
}
```

### SnAccountStatus
```json
{
  "id": "uuid",
  "attitude": "Positive | Negative | Neutral",
  "type": "Default | Busy | DoNotDisturb | Invisible",
  "label": "string (max 1024, optional)",
  "symbol": "string (max 128, optional)",
  "icon": "SnCloudFileReferenceObject | null",
  "background": "SnCloudFileReferenceObject | null",
  "is_online": "boolean (not persisted)",
  "is_idle": "boolean (not persisted)",
  "is_customized": "boolean (not persisted)",
  "is_automated": "boolean",
  "app_identifier": "string (max 4096, optional)",
  "meta": "object (optional, JSON)",
  "cleared_at": "ISO 8601 timestamp (null if active)",
  "account_id": "uuid",
  "created_at": "ISO 8601 timestamp",
  "updated_at": "ISO 8601 timestamp"
}
```

### SnCloudFileReferenceObject (Attachment)
```json
{
  "id": "string",
  "name": "string",
  "mime_type": "string | null",
  "hash": "string | null",
  "size": "long",
  "url": "string | null",
  "width": "integer | null",
  "height": "integer | null",
  "blurhash": "string | null",
  "usage": "string | null",
  "application_type": "string | null",
  "created_at": "ISO 8601 timestamp",
  "updated_at": "ISO 8601 timestamp"
}
```

---

## Behavior & Constraints

### Visibility Rules

1. **Private Events (visibility: 0)**
   - Only visible to the event owner
   - Not shown in any other user's calendar view

2. **Friends Events (visibility: 100)**
   - Visible to owner
   - Visible to users who are friends (mutual relationship status = Friends)
   - Not visible to blocked users or non-friends

3. **Public Events (visibility: 200)**
   - Visible to everyone, including unauthenticated users
   - Shown on public profile calendars

### Recurring Events

- Recurring events are expanded on the server when generating calendar views
- Each occurrence appears as a separate event in the calendar response
- The original `recurrence` field is set to `null` for expanded occurrences
- Recurrence ends when `end_date` is reached or `occurrences` limit is hit

### Time Handling

- All times are stored and returned in UTC
- Clients are responsible for converting to local timezone
- All-day events span exactly 24 hours (00:00:00 to 23:59:59 in UTC)

### Caching

- User calendar events are cached for 24 hours
- Cache is automatically invalidated when events are created, updated, or deleted
- Notable days (holidays) are cached separately for 1 day

### Soft Deletion

- Deleted events have their `deleted_at` field set to the deletion timestamp
- Soft-deleted events are filtered out of all queries automatically
- Events are permanently removed by a background job after 7 days

---

## Usage Examples

### Create a Weekly Meeting

```javascript
const event = await fetch('/api/accounts/me/calendar/events', {
  method: 'POST',
  headers: { 
    'Content-Type': 'application/json',
    'Authorization': 'Bearer <token>'
  },
  body: JSON.stringify({
    title: 'Weekly Team Standup',
    description: 'Daily standup meeting with the team',
    location: 'Zoom',
    start_time: '2026-04-21T09:00:00Z',
    end_time: '2026-04-21T09:30:00Z',
    visibility: 'Friends',
    recurrence: {
      frequency: 'Weekly',
      interval: 1,
      days_of_week: ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday']
    },
    meta: {
      zoom_link: 'https://zoom.us/j/123456789'
    }
  })
});
```

### Get Calendar with Notable Days

```javascript
const calendar = await fetch('/api/accounts/me/calendar?month=4&year=2026&includeNotableDays=true', {
  headers: { 'Authorization': 'Bearer <token>' }
});

const days = await calendar.json();
days.forEach(day => {
  console.log(`Date: ${day.date}`);
  console.log(`Events: ${day.user_events.length}`);
  console.log(`Holidays: ${day.notable_days.map(d => d.global_name).join(', ')}`);
});
```

### View Friend's Calendar

```javascript
const friendCalendar = await fetch('/api/accounts/alice/calendar?month=4&year=2026', {
  headers: { 'Authorization': 'Bearer <token>' }
});

const days = await friendCalendar.json();
// Only shows public events and friends-only events (if friends)
```

### Update Event Visibility

```javascript
await fetch('/api/accounts/me/calendar/events/770e8400-e29b-41d4-a716-446655440002', {
  method: 'PUT',
  headers: { 
    'Content-Type': 'application/json',
    'Authorization': 'Bearer <token>'
  },
  body: JSON.stringify({
    visibility: 'Public'
  })
});
```

---

## Error Handling

Common error responses follow REST API conventions:

```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "request": ["End time must be after start time."]
  }
}
```

```json
{
  "code": "not_found",
  "message": "Calendar event not found.",
  "status": 404,
  "trace_id": "00-abc123..."
}
```

**Common Status Codes:**
- `200 OK` - Success
- `201 Created` - Resource created
- `204 No Content` - Success, no response body
- `400 Bad Request` - Invalid request data
- `401 Unauthorized` - Missing or invalid authentication
- `404 Not Found` - Resource not found or not visible

---

## Implementation Notes

- Built with ASP.NET Core and Entity Framework Core
- Uses NodaTime for precise timestamp handling
- PostgreSQL with JSONB for metadata, recurrence patterns, and file references
- 24-hour caching via Redis for performance
- Automatic cache invalidation on mutations
- Soft deletion with 7-day retention
- Integration with existing relationship (friends) system
- Local notable days system with multi-day holiday support (e.g., Labour Day 5-day holiday)
- Status icon and background use shared attachment system (`SnCloudFileReferenceObject`)
- File IDs are validated against `DyFileService` before storing references

## Database Migrations

### AddUserCalendarEvents
Creates the `user_calendar_events` table with:
- UUID primary key
- Title, description, location fields
- Start/end timestamps with timezone
- Visibility enum (integer)
- JSONB columns for recurrence and metadata
- Standard audit fields (created_at, updated_at, deleted_at)
- Account ID foreign key (implicit)

### AddNotableDaysTable
Creates the `notable_days` table with:
- UUID primary key
- Name, description, local_name, localizable_key fields
- Start/end dates for multi-day periods
- Region code (defaults to "CN")
- Tags array (Holiday, Event, Anniversary, Memorial, Festival)
- Recurrence support (is_recurring, recurrence_pattern)
- Period support (is_period, holiday_days)
- Display order
- Standard audit fields

---

## Notable Days API

Manage system-wide notable days (holidays, events, festivals). These appear in calendar views and countdown.

### Base URL: `/api/notable-days`

### List Notable Days

**Endpoint:** `GET /api/notable-days`

**Query Parameters:**
- `year` (int, optional) - Year, defaults to current
- `region` (string, optional) - Region code, defaults to "CN"
- `tag` (string, optional) - Filter by tag: "Holiday", "Event", "Anniversary", "Memorial", "Festival"
- `offset` (int, optional) - Pagination offset
- `take` (int, optional) - Number of results, defaults to 50

**Response:**
```json
[
  {
    "id": "uuid",
    "name": "Spring Festival",
    "description": "Chinese New Year, the most important traditional festival in China",
    "local_name": "春节",
    "localizable_key": "SpringFestival",
    "start_date": "2026-01-28T00:00:00Z",
    "end_date": "2026-02-04T00:00:00Z",
    "is_all_day": true,
    "region": "CN",
    "tags": ["Holiday", "Festival"],
    "meta": null,
    "is_recurring": true,
    "recurrence_pattern": "01-01",
    "is_period": true,
    "holiday_days": ["01-28", "01-29", "01-30", "01-31", "02-01", "02-02", "02-03"],
    "display_order": 1,
    "created_at": "2026-01-01T00:00:00Z",
    "updated_at": "2026-01-01T00:00:00Z"
  }
]
```

**Response Headers:**
- `X-Total` - Total number of notable days matching the query

### Create Notable Day

**Endpoint:** `POST /api/notable-days`

**Request Body:**
```json
{
  "name": "Spring Festival",
  "description": "Chinese New Year",
  "local_name": "春节",
  "localizable_key": "SpringFestival",
  "start_date": "2026-01-28T00:00:00Z",
  "end_date": "2026-02-04T00:00:00Z",
  "is_all_day": true,
  "region": "CN",
  "tags": ["Holiday", "Festival"],
  "is_recurring": true,
  "recurrence_pattern": "01-01",
  "is_period": true,
  "holiday_days": ["01-28", "01-29", "01-30", "01-31", "02-01", "02-02", "02-03"],
  "display_order": 1
}
```

**Field Details:**
- `name` (required, max 256 chars) - Event name
- `description` (optional, max 4096 chars) - Event description
- `local_name` (optional, max 256 chars) - Localized name
- `localizable_key` (optional, max 256 chars) - Key for localization
- `start_date` (required) - Start date/time
- `end_date` (required) - End date/time
- `region` (optional, default "CN") - Region code
- `tags` (optional) - Array of tags: "Holiday", "Event", "Anniversary", "Memorial", "Festival"
- `is_recurring` (optional) - Whether this recurs annually
- `recurrence_pattern` (optional, max 16 chars) - MM-DD format for recurring events
- `is_period` (optional) - Whether this is a multi-day period
- `holiday_days` (optional) - Array of MM-DD strings indicating which days are actual holidays
- `display_order` (optional) - Sort order

**Response:** Returns the created `SnNotableDay` object (200 OK)

**Response Codes:**
- `200 OK` - Notable day created
- `400 Bad Request` - Invalid data
- `401 Unauthorized` - Invalid authentication
- `403 Forbidden` - Missing required permissions

### Update Notable Day

**Endpoint:** `PUT /api/notable-days/{id}`

**Request Body:** Same as create

### Delete Notable Day

**Endpoint:** `DELETE /api/notable-days/{id}`

**Response:** No content (204)

---

## Pre-seeded Chinese Holidays

The system automatically seeds the following Chinese holidays on startup:

| Holiday | Local Name | Period | Tags |
|---------|-----------|--------|------|
| Spring Festival | 春节 | 7 days | Holiday, Festival |
| Qingming Festival | 清明节 | 3 days | Holiday, Festival |
| Labour Day | 劳动节 | 5 days | Holiday |
| Dragon Boat Festival | 端午节 | 3 days | Holiday, Festival |
| Mid-Autumn Festival | 中秋节 | 3 days | Holiday, Festival |
| National Day | 国庆节 | 7 days | Holiday |
| New Year's Day | 元旦 | 3 days | Holiday |
| Arbor Day | 植树节 | 1 day | Event |
| Youth Day | 五四青年节 | 1 day | Event, Memorial |
| Children's Day | 儿童节 | 1 day | Event |
| Teachers' Day | 教师节 | 1 day | Event |
| Qixi Festival | 七夕节 | 1 day | Festival |
| Double Ninth Festival | 重阳节 | 1 day | Festival |

**Note:** Holiday days within multi-day periods are specified using `holiday_days` field (e.g., Labour Day has 5 holiday days from 05-01 to 05-05). Non-holiday days within a period (like weekends that are part of the extended break but not official holidays) are marked accordingly.

# Calendar Events API Documentation

## Overview

The Calendar Events API allows users to create, manage, and share personal calendar events with granular visibility controls. Events can be viewed in a merged calendar format alongside check-ins, status updates, and notable days (holidays).

This service is handled by the DysonNetwork.Passport service. When using with the gateway, replace `/api` with `/pass`.

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
  }
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
  }
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

### NotableDay
```json
{
  "date": "ISO 8601 timestamp",
  "local_name": "string | null",
  "global_name": "string | null",
  "localizable_key": "string | null",
  "country_code": "string | null",
  "holidays": ["Public", "Bank", "School", "Authorities", "Optional", "Observance"]
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
- PostgreSQL with JSONB for metadata and recurrence pattern storage
- 24-hour caching via Redis for performance
- Automatic cache invalidation on mutations
- Soft deletion with 7-day retention
- Integration with existing relationship (friends) system
- Notable days fetched from Nager.Holiday API with global holiday support

## Database Migration

The feature includes an EF Core migration (`AddUserCalendarEvents`) that creates the `user_calendar_events` table with:

- UUID primary key
- Title, description, location fields
- Start/end timestamps with timezone
- Visibility enum (integer)
- JSONB columns for recurrence and metadata
- Standard audit fields (created_at, updated_at, deleted_at)
- Account ID foreign key (implicit)

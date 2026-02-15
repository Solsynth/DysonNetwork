# Ticket API Documentation

## Overview

The Ticket system allows users to create and manage support tickets with messages and file attachments.

## Base URL

```
/api/tickets
```

## Enums

### Ticket Type

| Value | Description |
|-------|-------------|
| `0` | General support request |
| `1` | Bug report |
| `2` | Feature request |
| `3` | Billing related issue |
| `4` | Other issues |

### Ticket Status

| Value | Description |
|-------|-------------|
| `0` | Ticket is open |
| `1` | Ticket is being worked on |
| `2` | Ticket has been resolved |
| `3` | Ticket is closed |

### Ticket Priority

| Value | Description |
|-------|-------------|
| `0` | Low priority |
| `1` | Medium priority |
| `2` | High priority |
| `3` | Critical priority |

---

## Endpoints

### Create Ticket

Create a new support ticket.

**Endpoint:** `POST /api/tickets`

**Authorization:** Required

**Request Body:**

```json
{
  "title": "string (required, 3-256 chars)",
  "content": "string (optional, max 16384 chars)",
  "type": "ticket_type (required)",
  "priority": "ticket_priority (optional, default: 1)",
  "fileIds": ["string array (optional)"]
}
```

**Example Request:**

```json
{
  "title": "Cannot login to my account",
  "content": "I've been trying to login but keep getting an error",
  "type": 0,
  "priority": 2
}
```

**Response:** `200 OK`

```json
{
  "id": "uuid",
  "title": "Cannot login to my account",
  "content": "I've been trying to login but keep getting an error",
  "type": 0,
  "status": 0,
  "priority": 2,
  "creator_id": "uuid",
  "assignee_id": null,
  "resolved_at": null,
  "created_at": "timestamp",
  "updated_at": "timestamp",
  "deleted_at": null,
  "messages": [],
  "files": []
}
```

---

### List Tickets

Get a list of tickets with optional filters.

**Endpoint:** `GET /api/tickets`

**Authorization:** Required

**Query Parameters:**

| Parameter | Type | Description |
|-----------|------|-------------|
| `creator_id` | uuid | Filter by creator |
| `assignee_id` | uuid | Filter by assignee |
| `type` | int | Filter by ticket type |
| `status` | int | Filter by status |
| `priority` | int | Filter by priority |
| `offset` | int | Pagination offset (default: 0) |
| `take` | int | Pagination limit (default: 20) |

**Response:** `200 OK`

```json
[
  {
    "id": "uuid",
    "title": "Cannot login to my account",
    "content": "...",
    "type": 0,
    "status": 0,
    "priority": 2,
    "creator_id": "uuid",
    "assignee_id": null,
    "resolved_at": null,
    "created_at": "timestamp",
    "updated_at": "timestamp",
    "files": []
  }
]
```

---

### Get My Tickets

Get tickets created by the current user.

**Endpoint:** `GET /api/tickets/me`

**Authorization:** Required

**Query Parameters:**

| Parameter | Type | Description |
|-----------|------|-------------|
| `status` | int | Filter by status |
| `offset` | int | Pagination offset (default: 0) |
| `take` | int | Pagination limit (default: 20) |

**Response:** `200 OK`

---

### Get Ticket by ID

Get details of a specific ticket.

**Endpoint:** `GET /api/tickets/{id}`

**Authorization:** Required

**Path Parameters:**

| Parameter | Type | Description |
|-----------|------|-------------|
| `id` | uuid | Ticket ID |

**Response:** `200 OK` or `404 Not Found`

---

### Update Ticket

Update ticket information.

**Endpoint:** `PUT /api/tickets/{id}`

**Authorization:** Required

**Request Body:**

```json
{
  "title": "string (optional, 3-256 chars)",
  "type": "ticket_type (optional)",
  "priority": "ticket_priority (optional)"
}
```

**Response:** `200 OK` or `404 Not Found`

---

### Delete Ticket

Delete a ticket.

**Endpoint:** `DELETE /api/tickets/{id}`

**Authorization:** Required

**Response:** `204 No Content` or `404 Not Found`

---

### Add Message

Add a message to a ticket.

**Endpoint:** `POST /api/tickets/{id}/messages`

**Authorization:** Required

**Request Body:**

```json
{
  "content": "string (required, 1-16384 chars)",
  "fileIds": ["string array (optional)"]
}
```

**Response:** `200 OK`

```json
{
  "id": "uuid",
  "ticket_id": "uuid",
  "sender_id": "uuid",
  "content": "message content",
  "created_at": "timestamp",
  "updated_at": "timestamp",
  "deleted_at": null
}
```

---

### Add File

Attach a file to a ticket. The file must already be uploaded to the Drive service.

**Endpoint:** `POST /api/tickets/{id}/files`

**Authorization:** Required

**Request Body:**

```json
{
  "file_id": "string (required, max 32 chars - Drive file ID)"
}
```

**Response:** `200 OK`

```json
{
  "id": "uuid",
  "ticket_id": "uuid",
  "file_id": "string",
  "created_at": "timestamp",
  "updated_at": "timestamp",
  "deleted_at": null
}
```

---

### Update Ticket Status

Update the status of a ticket.

**Endpoint:** `POST /api/tickets/{id}/status`

**Authorization:** Required

**Request Body:**

```json
{
  "status": "int (required)"
}
```

**Response:** `200 OK`

---

### Assign Ticket

Assign a ticket to a user.

**Endpoint:** `POST /api/tickets/{id}/assign`

**Authorization:** Required

**Request Body:**

```json
{
  "assignee_id": "uuid (optional - set to null to unassign)"
}
```

**Response:** `200 OK`

---

### Get Ticket Count

Get the total count of tickets.

**Endpoint:** `GET /api/tickets/count`

**Authorization:** Required

**Query Parameters:**

| Parameter | Type | Description |
|-----------|------|-------------|
| `creator_id` | uuid | Filter by creator |
| `assignee_id` | uuid | Filter by assignee |
| `status` | int | Filter by status |

**Response:** `200 OK`

```json
{
  "count": 10
}
```

---

## Response Format

All responses use snake_case naming convention for properties.

**Example:**

```json
{
  "created_at": "2026-02-15T10:30:00Z",
  "type": 0,
  "is_resolved": false
}
```

---

## Error Responses

| Status Code | Description |
|-------------|-------------|
| `400 Bad Request` | Invalid request body or parameters |
| `401 Unauthorized` | Authentication required |
| `403 Forbidden` | Access denied |
| `404 Not Found` | Resource not found |
| `500 Internal Server Error` | Server error |

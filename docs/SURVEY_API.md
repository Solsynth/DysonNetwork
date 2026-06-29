# Survey API Documentation

## Overview

The Survey system (formerly Polls) lets publishers create questionnaires — single choice,
multiple choice, yes/no, rating, or free-text — and collect responses from accounts.
Surveys follow a **Draft → Published → Archived** lifecycle:

- **Draft** — editable; no responses accepted.
- **Published** — immutable; responses accepted (until `ended_at` passes). Clone to a new
  Draft to revise. Existing answers stay bound to the frozen Published row.
- **Archived** — fully locked; no edits, no new responses.

Surveys can be attached to posts and chat messages via the `survey` embed type.
The legacy `poll` embed type is still read for back-compat during cleanup.

## Base URL

```
/sphere/surveys
```

## Roles

Survey operations are scoped to the owning **Publisher**. The role checks below are reused
from the Publisher system.

| Role    | Capabilities                                                    |
|---------|-----------------------------------------------------------------|
| Viewer  | Answer surveys, view own answers, view non-anonymous feedback   |
| Editor  | Everything a Viewer can do, plus create/edit/publish/clone/delete |

---

## Enums

### Survey Status

| Value | Name       | Description                                                   |
|-------|------------|---------------------------------------------------------------|
| `0`   | `Draft`    | Editable; no responses accepted                               |
| `1`   | `Published` | Immutable; responses accepted (until `ended_at`). Default for legacy rows |
| `2`   | `Archived` | Fully locked                                                  |

### Survey Question Type

| Value | Name              | Answer shape                  | Extra fields                              |
|-------|-------------------|-------------------------------|-------------------------------------------|
| `0`   | `SingleChoice`    | string (option id)            | `options` (≥2), `max_selections` (opt)    |
| `1`   | `MultipleChoice`  | array of strings (option ids) | `options` (≥2), `max_selections` (opt)    |
| `2`   | `YesNo`           | boolean                       | —                                         |
| `3`   | `Rating`          | number                        | `min_value`, `max_value` (opt)            |
| `4`   | `FreeText`        | string                        | `max_length` (opt)                        |

---

## Data Model

### Survey

```json
{
  "id": "uuid",
  "title": "string?",
  "description": "string?",
  "ended_at": "timestamp?",
  "is_anonymous": "boolean",
  "status": "SurveyStatus",
  "published_at": "timestamp?",
  "notify_subscribers": "boolean",
  "hide_results": "boolean",
  "attachments": [SnCloudFileReferenceObject],
  "publisher_id": "uuid",
  "questions": [SurveyQuestion],
  "created_at": "timestamp",
  "updated_at": "timestamp",
  "deleted_at": "timestamp?"
}
```

### Survey Question

```json
{
  "id": "uuid",
  "type": "SurveyQuestionType",
  "options": [SurveyOption]?,
  "title": "string",
  "description": "string?",
  "order": "int",
  "is_required": "boolean",
  "attachments": [SnCloudFileReferenceObject],
  "max_selections": "int?",
  "max_length": "int?",
  "min_value": "double?",
  "max_value": "double?"
}
```

### Survey Option

```json
{
  "id": "uuid",
  "label": "string",
  "description": "string?",
  "order": "int"
}
```

### Survey Answer (Submission)

```json
{
  "id": "uuid",
  "answer": { "<question_id>": <value> },
  "account_id": "uuid",
  "survey_id": "uuid",
  "account": "SnAccount?",
  "created_at": "timestamp",
  "updated_at": "timestamp",
  "deleted_at": "timestamp?"
}
```

The `answer` map's value shape depends on the question `type` (see enum table above).
When the survey is anonymous, `account` is not populated on feedback listings.

---

## Endpoints

### Get Survey

Fetch a single survey, including its questions. If the caller is authenticated, the
response also includes the caller's answer (if any). Aggregate stats per question are
included **only when** the survey's `hide_results` flag is `false` OR the caller is a
member of the owning publisher (Viewer+ role); otherwise `stats` is an empty map.

**Endpoint:** `GET /api/surveys/{id}`

**Authorization:** Optional

**Path Parameters:**

| Parameter | Type | Description |
|-----------|------|-------------|
| `id`      | uuid | Survey ID   |

**Response:** `200 OK`

Returns a `SurveyWithStats` envelope (a `Survey` plus two extra fields):

```json
{
  "id": "uuid",
  "title": "...",
  "questions": [...],
  "user_answer": null,
  "stats": {
    "<question_id>": { "<option_id|'true'|'false'|'rating'>": <count> }
  }
}
```

| Field         | Type             | Notes                                                       |
|---------------|------------------|-------------------------------------------------------------|
| `user_answer` | `SurveyAnswer?`  | Populated only when the caller is authenticated and answered |
| `stats`       | `map<uuid, map<string, int>>` | Per-question aggregate counts. `rating` key holds the rounded average |

---

### List My Surveys

List surveys owned by publishers the caller can access.

**Endpoint:** `GET /api/surveys/me`

**Authorization:** Required

**Query Parameters:**

| Parameter | Type     | Description                                              |
|-----------|----------|----------------------------------------------------------|
| `pub`     | string?  | Publisher name; when omitted, all publishers the user belongs to are used |
| `active`  | boolean  | When true, only return surveys that have not ended (default false) |
| `offset`  | int      | Pagination offset (default 0)                            |
| `take`    | int      | Page size (default 20)                                   |

**Response:** `200 OK`

`X-Total` header carries the total count. Body is `Survey[]` (questions included).

---

### Create Survey

Create a new survey in **Draft** status.

**Endpoint:** `POST /api/surveys?pub={publisherName}`

**Authorization:** Required — caller must be an Editor of the named publisher.

**Request Body:**

```json
{
  "title": "string?",
  "description": "string?",
  "ended_at": "timestamp?",
  "clear_ended_at": "boolean?",
  "is_anonymous": "boolean?",
  "notify_subscribers": "boolean?",
  "hide_results": "boolean?",
  "attachments": ["string (file id)"]?,
  "questions": [
    {
      "id": "uuid (optional; assigned if missing/empty)",
      "type": "SurveyQuestionType",
      "options": [
        { "id": "uuid?", "label": "string", "description": "string?", "order": "int" }
      ]?,
      "title": "string (max 1024)",
      "description": "string? (max 4096)",
      "order": "int",
      "is_required": "boolean",
      "attachments": ["string (file id)"]?
    }
  ]
}
```

**Notes:**

- `attachments` (intro and per-question) are lists of Drive file IDs. The server resolves
  them into denormalized `SnCloudFileReferenceObject` snapshots via the Drive (file) service,
  preserving the requested order. IDs that cannot be resolved are dropped.
- Validation runs before save (see [Validation](#validation)).

**Response:** `200 OK` with the created `Survey`.

---

### Update Survey

Patch a survey. Only **Draft** surveys are mutable; Published/Archived surveys return
`409 SURVEY_IMMUTABLE` — clone a new draft to revise.

**Endpoint:** `PATCH /api/surveys/{id}`

**Authorization:** Required — Editor of the owning publisher.

**Path Parameters:**

| Parameter | Type | Description |
|-----------|------|-------------|
| `id`      | uuid | Survey ID   |

**Request Body:** Same shape as `POST /api/surveys`, but every field is optional. When a
field is `null`/omitted, it is left untouched. Notable PATCH semantics:

- `clear_ended_at: true` clears `ended_at`; otherwise `ended_at` is set if provided.
- `attachments` replaces the intro attachment list **only when non-null**.
- `hide_results` replaces the flag **only when non-null**.
- `questions` is replaced wholesale when provided. Each question's `attachments` is
  replaced only when non-null in that question's request object.
- Per-question validation fields are honored when provided.

**Response:** `200 OK` with the updated `Survey`.

**Errors:**

| Status | Code               | When                                             |
|--------|--------------------|--------------------------------------------------|
| 409    | `SURVEY_IMMUTABLE` | Survey is not Draft                               |
| 422    | `VALIDATION_ERROR` | Survey fails structural validation                |
| 403    | —                  | Caller is not an Editor of the owning publisher  |
| 404    | `NOT_FOUND`        | Survey does not exist                             |

---

### Delete Survey

Hard-delete a survey along with its questions and answers.

**Endpoint:** `DELETE /api/surveys/{id}`

**Authorization:** Required — Editor of the owning publisher.

**Response:** `204 No Content` or `404 Not Found`.

---

### Publish Survey

Transition a Draft survey to Published. Marks it immutable and sets `published_at` (only
if not already set). Re-runs structural validation; a Draft that fails validation cannot
be published.

**Endpoint:** `POST /api/surveys/{id}/publish`

**Authorization:** Required — Editor of the owning publisher.

**Response:** `200 OK` with the updated `Survey` (`status = 1`, `published_at` populated).

**Errors:**

| Status | Code             | When                                                |
|--------|------------------|-----------------------------------------------------|
| 409    | `INVALID_STATE`  | Survey is not in Draft status                        |
| 422    | `VALIDATION_ERROR` | Draft failed validation prior to publishing         |

---

### Archive Survey

Transition a Published survey to Archived. Archived surveys are fully locked — no edits,
no new responses. Drafts cannot be archived (delete them instead).

**Endpoint:** `POST /api/surveys/{id}/archive`

**Authorization:** Required — Editor of the owning publisher.

**Response:** `200 OK` with the updated `Survey` (`status = 2`).

**Errors:**

| Status | Code             | When                                                |
|--------|------------------|-----------------------------------------------------|
| 409    | `INVALID_STATE`  | Survey is Draft (delete instead) or already Archived |

---

### Clone Survey

Create a new Draft as a copy of any survey (Draft, Published, or Archived). All question
and option IDs are regenerated; attachment snapshots and configuration fields are copied.
The new survey has its own `id`, `created_at`, `status = Draft`, and `published_at = null`.

This is the recommended revision path for an already-published survey: clone → edit → publish.

**Endpoint:** `POST /api/surveys/{id}/clone`

**Authorization:** Required — Editor of the owning publisher.

**Response:** `200 OK` with the new Draft `Survey` (fresh `id`).

---

### Answer Survey

Submit (or replace) the caller's answer to a published survey. Only surveys in
**Published** status (and whose `ended_at` has not passed) accept answers. Each account
may have at most one answer per survey; submitting again replaces the prior answer.

**Endpoint:** `POST /api/surveys/{id}/answer`

**Authorization:** Required

**Request Body:**

```json
{
  "answer": {
    "<question_id>": "<value matching the question type>"
  }
}
```

The value for each key depends on the question `type`:

| Type              | JSON value                              | Constraints                                          |
|-------------------|-----------------------------------------|------------------------------------------------------|
| `SingleChoice`    | string (option id)                      | Must match one of the question's options             |
| `MultipleChoice`  | array of strings (option ids)           | Each must match an option; length ≤ `max_selections` |
| `YesNo`           | boolean                                 | —                                                    |
| `Rating`          | number                                  | Within `[min_value, max_value]` when set             |
| `FreeText`        | string                                  | Length ≤ `max_length` when set                       |

Required questions must have a non-empty value; optional questions may be omitted or `null`.

**Response:** `200 OK` with the saved `SurveyAnswer`.

**Errors:**

| Status | Code               | When                                                |
|--------|--------------------|-----------------------------------------------------|
| 409    | `INVALID_STATE`    | Survey is not Published, or `ended_at` has passed    |
| 422    | `VALIDATION_ERROR` | One or more answers fail per-question validation     |
| 404    | `NOT_FOUND`        | Survey does not exist                                |

---

### Delete My Answer

Soft-delete the caller's answer to a survey.

**Endpoint:** `DELETE /api/surveys/{id}/answer`

**Authorization:** Required

**Response:** `204 No Content`.

---

### Get Survey Feedback

List all answers to a survey. Non-anonymous surveys include the submitting `account`
populated on each answer; anonymous surveys omit it. Requires Viewer role on the owning
publisher.

**Endpoint:** `GET /api/surveys/{id}/feedback`

**Authorization:** Required — Viewer of the owning publisher.

**Query Parameters:**

| Parameter | Type | Description                                                                |
|-----------|------|----------------------------------------------------------------------------|
| `offset`  | int  | Pagination offset (default 0, clamped ≥ 0)                                  |
| `take`    | int  | Page size (default 20, clamped to `[1, 100]` to bound result sets)         |

**Response:** `200 OK`

`X-Total` header carries the total count. Body is `SurveyAnswer[]` ordered by `created_at`
descending. Each answer includes `account` (unless anonymous).

---

## Subscriptions & Notifications

A subscriber to a survey receives a push notification (topic `surveys.answer`) when any
account answers it, **as long as** the survey's `notify_subscribers` flag is set. The
answering user never receives their own answer notification.

Notifications are pushed via the Ring service (`DyRingService.SendPushNotificationToUser`).
Recipients who have been blocked or muted by the survey's publisher account are filtered
out (mirrors `PostService.NotifyPostSubscribersAsync`). The notification itself is
best-effort: if Ring is unreachable the answer write still succeeds.

### Subscribe to a Survey

Create (or return the existing) subscription for the current user. Idempotent — calling
`subscribe` twice returns the same row.

**Endpoint:** `POST /api/surveys/{id}/subscribe`

**Authorization:** Required

**Response:** `200 OK` with the `SurveySubscription`:

```json
{
  "id": "uuid",
  "survey_id": "uuid",
  "account_id": "uuid",
  "created_at": "timestamp",
  "updated_at": "timestamp",
  "deleted_at": null
}
```

### Unsubscribe from a Survey

Remove the current user's subscription. Returns `204` whether or not a subscription
existed (idempotent).

**Endpoint:** `POST /api/surveys/{id}/unsubscribe`

**Authorization:** Required

**Response:** `204 No Content`.

### Get Current Subscription

Fetch the current user's subscription to a survey, if any.

**Endpoint:** `GET /api/surveys/{id}/subscription`

**Authorization:** Required

**Response:** `200 OK` with the `SurveySubscription`, or `404 Not Found` if no
subscription exists.

### Data Model

```json
{
  "id": "uuid",
  "survey_id": "uuid",
  "account_id": "uuid",
  "created_at": "timestamp",
  "updated_at": "timestamp",
  "deleted_at": "timestamp?"
}
```

A unique partial index on `(account_id, survey_id, deleted_at)` enforces "at most one
active subscription per account per survey". A cascade FK on `survey_id` ensures
subscriptions are removed when the survey is hard-deleted.

---

## Validation

Structural validation runs on Create, Update, and Publish. Per-answer validation runs on
Answer. Failures return `422 Unprocessable Entity` with an `ApiError` envelope:

```json
{
  "code": "VALIDATION_ERROR",
  "message": "One or more validation errors occurred.",
  "status": 422,
  "errors": {
    "questions[<id>].options": ["Choice question must have at least two options"],
    "answers[<question_id>]": ["Answer for question 'Rate us' is below the minimum (1)"]
  }
}
```

### Survey-level rules

- At least one question.
- Each question has a non-empty `title` (≤ 1024 chars).
- `SingleChoice` / `MultipleChoice` questions require ≥ 2 options; each option needs a
  non-empty `label`.
- `max_selections` (when set) must be ≥ 1 and ≤ the number of options.
- `max_length` (when set, FreeText) must be > 0.
- `min_value` / `max_value` (when both set, Rating) must satisfy `min_value < max_value`.

### Answer-level rules

- Required questions must be answered.
- Type/shape must match (string/array/number/boolean).
- Option references must match an existing option.
- `max_selections` bounds the number of selected options.
- Free-text length ≤ `max_length`.
- Rating value within `[min_value, max_value]`.

---

## Embeds

### In Posts

A post may embed a survey via the `survey` embed type:

```json
{
  "type": "survey",
  "data": { "id": "<survey uuid>" }
}
```

The legacy `poll` type is still accepted on read for back-compat during the rename
migration; new writes should use `survey`.

### In Chat Messages

Chat messages embed surveys the same way:

```json
{
  "type": "survey",
  "data": { "id": "<survey uuid>" }
}
```

Messager resolves embeds into full survey payloads by calling the Sphere survey gRPC
service (`DySurveyService.GetSurvey`).

---

## gRPC

Sphere exposes `DySurveyService` for inter-service use:

| Method                   | Input                          | Output                       | Description                            |
|--------------------------|--------------------------------|------------------------------|----------------------------------------|
| `GetSurvey`              | `DyGetSurveyRequest`           | `DySurvey`                   | Fetch one survey by id                 |
| `GetSurveyBatch`         | `DyGetSurveyBatchRequest`      | `DyGetSurveyBatchResponse`   | Fetch multiple surveys by id           |
| `ListSurveys`            | `DyListSurveysRequest`         | `DyListSurveysResponse`      | Paginated list filtered by publisher   |
| `GetSurveyAnswer`        | `DyGetSurveyAnswerRequest`     | `DySurveyAnswer`             | Fetch one account's answer             |
| `GetSurveyStats`         | `DyGetSurveyStatsRequest`      | `DyGetSurveyStatsResponse`   | Per-question aggregate counts (JSON)   |
| `GetSurveyQuestionStats` | `DyGetSurveyQuestionStatsRequest` | `DyGetSurveyQuestionStatsResponse` | Single-question aggregate counts |

Proto lives in `Spec/proto/survey.proto`. Generated C# bindings are in
`DysonNetwork.Shared/Proto/Survey.cs` + `SurveyGrpc.cs`; register a client via
`ServiceInjectionHelper.AddGrpcClientWithSharedChannel<DySurveyService.DySurveyServiceClient>(...)`.

---

## Caching

- **Answer lookup** — `survey:answer:{surveyId}:{accountId}` (30 min TTL). A `null` value
  is cached to represent "no answer yet".
- **Per-question stats** — `survey:stats:{questionId}` (1 hour TTL).
- All survey caches live under the `survey:{surveyId}` group; submitting, replacing, or
  deleting an answer invalidates that group via `RemoveGroupAsync`.

---

## Database

Tables (snake_case, EF Core convention):

- `surveys` — root survey row. New columns: `status`, `published_at`, `notify_subscribers`,
  `hide_results`, `attachments` (jsonb).
- `survey_questions` — one row per question. New columns: `attachments` (jsonb),
  `max_selections`, `max_length`, `min_value`, `max_value`.
- `survey_answers` — one row per account submission. `answer` column is jsonb.
- `survey_subscriptions` — per-survey push subscription. Unique partial index on
  `(account_id, survey_id, deleted_at)`; cascade FK to `surveys`. (Added by
  migration `20260627122542_AddSurveySubscriptions`.)

Migration `20260627115823_RenamePollToSurvey`:

- Renames `polls` → `surveys`, `poll_questions` → `survey_questions`,
  `poll_answers` → `survey_answers` (and the matching `poll_id` columns/indexes/FKs).
- Adds the new columns above.
- Backfills existing rows: `status = 1` (Published) and `published_at = COALESCE(published_at, created_at)`
  so prior submissions remain immutable under the new lifecycle.

Migration `20260629123628_AddSurveyHideResults`:
- Adds `hide_results boolean NOT NULL DEFAULT false` to `surveys`. When `true`, aggregate
  stats are hidden from non-publisher members in `GET /api/surveys/{id}`.

---

## Error Envelope

All endpoints return errors as `ApiError` (RFC7807-inspired):

```json
{
  "code": "string",
  "message": "string",
  "status": "int?",
  "detail": "string?",
  "trace_id": "string?",
  "errors": { "<field>": ["string"] }?,
  "meta": { "<key>": "<value>" }?
}
```

Common codes:

| Code               | Status | Meaning                                              |
|--------------------|--------|------------------------------------------------------|
| `VALIDATION_ERROR` | 422    | Field-level validation failure; see `errors`         |
| `INVALID_STATE`    | 409    | Lifecycle conflict (e.g. answering a Draft)          |
| `SURVEY_IMMUTABLE` | 409    | Edit attempted on a non-Draft survey                 |
| `NOT_FOUND`        | 404    | Survey does not exist                                 |
| `FORBIDDEN`        | 403    | Caller lacks the required publisher role             |
| `SERVER_ERROR`     | 500    | Unexpected server error                              |

---

## Response Format

All responses use snake_case naming convention for properties (see
`AGENTS.md` → JSON Serialization). Timestamps are ISO 8601 strings in UTC.

```json
{
  "created_at": "2026-06-27T10:30:00Z",
  "is_anonymous": false,
  "notify_subscribers": true
}
```

# Email Sending Plan API

This document describes the Ring-owned admin APIs for scheduled or rate-limited email delivery.

These routes live in `DysonNetwork.Ring/Email/EmailSendingPlanAdminController.cs`.

Local development base route:

```text
/api/admin/email-plans
```

Production gateway route:

```text
/ring/admin/email-plans
```

All routes below require:

- `emails.send`

## Design

- `.Ring` owns the sending-plan state, interval history, pause/resume control, and progress tracking.
- `.Ring` snapshots target account ids when the plan is created.
- On each interval advance, `.Ring` resolves the current verified email contact from Padlock over gRPC before sending.
- If no verified email contact is available for an account at send time, that recipient is recorded as `skipped`.

## Status values

- `scheduled`
- `paused`
- `completed`

Recipient progress counts are exposed as:

- `pending`
- `sent`
- `skipped`
- `failed`

## POST /api/admin/email-plans

Creates a new sending plan.

Request body:

```json
{
  "account_ids": [
    "550e8400-e29b-41d4-a716-446655440000",
    "7a1bd1c9-9d7d-4e77-b25c-c48a7c6b8956"
  ],
  "broadcast_to_all": false,
  "sending_plan_key": "summer-campaign-2026-07",
  "subject": "Important account notice",
  "html_body": "<html><body><h1>Hello</h1><p>This is a scheduled admin email.</p></body></html>",
  "planned_start_at": "2026-07-09T00:00:00Z",
  "max_emails_per_interval": 200,
  "interval_minutes": 30,
  "max_emails_per_day": 2000
}
```

Notes:

- Provide `account_id`, `account_ids`, or `broadcast_to_all=true`.
- `max_emails_per_day` must be greater than or equal to `max_emails_per_interval`.
- `sending_plan_key` is optional but can be useful for operator-side deduplication or campaign lookup.

Response shape:

```json
{
  "id": "2d22478e-5410-4a6f-b5c5-fceef4ef8e82",
  "sending_plan_key": "summer-campaign-2026-07",
  "created_by_account_id": "6a5b640e-ec9c-4dc7-9838-c7fd7fa5b1ab",
  "subject": "Important account notice",
  "broadcast_to_all": false,
  "recipient_count": 2,
  "max_emails_per_interval": 200,
  "interval_minutes": 30,
  "max_emails_per_day": 2000,
  "status": 0,
  "advanced_intervals_count": 0,
  "planned_start_at": "2026-07-09T00:00:00Z",
  "next_interval_at": "2026-07-09T00:00:00Z",
  "last_advanced_at": null,
  "paused_at": null,
  "completed_at": null,
  "counts": {
    "total": 2,
    "pending": 2,
    "sent": 0,
    "skipped": 0,
    "failed": 0
  },
  "advances": []
}
```

## GET /api/admin/email-plans

Lists sending plans.

Query parameters:

- `take` default `20`, max `100`
- `offset` default `0`
- `status` optional enum filter

Response headers:

- `X-Total` total plan count after filtering

The response is a list of plan summaries with aggregate progress counts.

## GET /api/admin/email-plans/{plan_id}

Returns one plan with recent interval history.

The `advances` array contains the most recent interval runs, including:

- `interval_number`
- `is_manual`
- `attempted_count`
- `sent_count`
- `skipped_count`
- `failed_count`
- `pending_count_after`
- `started_at`
- `completed_at`

## POST /api/admin/email-plans/{plan_id}/pause

Pauses automatic interval advancement.

Behavior:

- The plan remains queryable.
- Manual advance is still available to operators.
- The scheduler job ignores paused plans.

## POST /api/admin/email-plans/{plan_id}/resume

Resumes a paused plan.

Behavior:

- Status becomes `scheduled`.
- If the previous `next_interval_at` is already in the past, resume resets it to the current time so the next interval can run immediately.

## POST /api/admin/email-plans/{plan_id}/advance

Manually advances exactly one interval worth of work.

Behavior:

- Uses the same per-interval and per-day limits as automatic advancement.
- Can be used while a plan is paused.
- Records a new interval entry in `advances` when any recipients are processed.

## Operational notes

- Plan membership is fixed at creation time through the stored recipient rows.
- Contact resolution is live at send time, so updated primary/verified emails in Padlock are picked up by later intervals.
- Failed recipients are recorded as `failed` and are not retried automatically by this first version.

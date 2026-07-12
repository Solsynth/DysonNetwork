# Notification Observability

Ring records notification send requests and delivery outcomes in its own database. This is a built-in service feature and does not require OTLP or an external metrics pipeline.

Each queued or batch target creates one `notification_send_records` row, giving an unambiguous total-send count and topic share. It contains only topic, app ID, push type, source, and timestamp.

The delivery path records one `notification_delivery_records` row for every attempted channel delivery:

- WebSocket fanout (`provider=websocket`)
- Google / FCM (`provider=google`)
- Apple APNS (`provider=apple`)
- Apple PushKit (`provider=appk`)
- UnifiedPush (`provider=unifiedpush`)
- SOP-only subscriptions (`provider=sop`, `outcome=skipped`), because no separate provider push is sent

Records include the notification topic, app ID, push type, provider, outcome, elapsed milliseconds, timestamp, and a truncated error message. They deliberately exclude account IDs, device IDs, subscription IDs, tokens, titles, contents, and metadata.

## Outcomes and success rate

- `success`: the channel or provider accepted the delivery attempt.
- `failure`: the attempt failed.
- `invalid_token`: the provider rejected a token and Ring queued subscription cleanup.
- `skipped`: no provider send was required; it is excluded from the success-rate denominator.

```text
success_rate = success / (success + failure + invalid_token)
```

This allows the built-in view to report total deliveries, provider distribution, topic distribution, and meaningful success rates. Provider success remains submission success, not confirmation that a user saw the notification.

## Admin API

`GET /api/admin/delivery-observability/notifications`

Requires `notifications.send`. It returns the total send requests and their topic distribution, together with delivery totals and their provider/topic breakdowns. Query parameters:

- `from`: optional ISO-8601 UTC start time
- `to`: optional ISO-8601 UTC end time

When omitted, the range is the last 30 days through the current time.

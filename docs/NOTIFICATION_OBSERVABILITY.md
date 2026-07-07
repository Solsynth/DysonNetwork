# Notification Observability

This document proposes tracing and metrics for Ring notification delivery, with emphasis on:

- total notification sends
- topic distribution
- provider distribution
- delivery success rate
- delivery latency

The main notification API surface lives in `DysonNetwork.Ring/Notification/NotificationController.cs`, but the actual delivery work happens in `DysonNetwork.Ring/Notification/PushService.cs`.

## Current Flow

### API entry

Manual/batch sends enter through:

```http
POST /api/notifications/send
```

Controller method:

- `NotificationController.SendNotification(...)`

This endpoint builds a `SnNotification` and forwards it to:

- `PushService.SendNotificationBatch(...)`

### Service entry

The main service paths are:

- `PushService.SendNotification(...)` for one-account sends
- `PushService.SendNotificationBatch(...)` for multi-account sends
- `PushService.DeliverPushNotification(...)` for single notification fanout
- `PushService.SendPushNotificationAsync(...)` for provider-specific delivery

### Delivery fanout

For each target account, Ring may deliver through multiple channels:

- WebSocket
- SOP stream / replay buffer
- Google / FCM
- Apple / APNS
- Apple PushKit / Appk
- UnifiedPush

## Existing OpenTelemetry Setup

Ring already uses shared OpenTelemetry wiring through:

- `builder.AddServiceDefaults()` in `DysonNetwork.Ring/Program.cs`
- `DysonNetwork.Shared/Extensions.cs`

That shared setup already enables:

- ASP.NET Core instrumentation
- HTTP client instrumentation
- gRPC client instrumentation
- runtime instrumentation
- OTLP export when `OTEL_EXPORTER_OTLP_ENDPOINT` is configured

Because of that, Ring only needs service-specific `ActivitySource` and `Meter` instrumentation for notification business signals.

## Recommended Tracing

Use one `ActivitySource` for Ring notification delivery, for example:

```csharp
DysonNetwork.Ring.Notification
```

Recommended spans:

### 1. `notifications.send`

Create this around:

- `NotificationController.SendNotification(...)`
- `PushService.SendNotification(...)`
- `PushService.SendNotificationBatch(...)`

Suggested tags:

- `notification.topic`
- `notification.app_id`
- `notification.push_type`
- `notification.save`
- `notification.batch_size`

This span measures request-level intent, not provider delivery success.

### 2. `notifications.deliver.account`

Create this around each target account fanout inside:

- `PushService.SendNotificationBatch(...)`
- optionally `PushService.DeliverPushNotification(...)`

Suggested tags:

- `notification.topic`
- `notification.app_id`
- `notification.push_type`
- `notification.account_has_subscriptions`
- `notification.subscription_count`

This span is useful for understanding how expensive delivery is per recipient.

### 3. `notifications.push.provider`

Create this around each provider send in:

- `PushService.SendPushNotificationAsync(...)`

Suggested tags:

- `notification.topic`
- `notification.app_id`
- `notification.push_type`
- `notification.provider`
- `notification.priority`

Suggested status:

- `Ok` for successful provider submission
- `Error` for failed provider submission

Do not attach raw `device_token`, `device_id`, or `account_id` as metric dimensions. If needed for traces, keep them out of metrics and treat them as sensitive.

## Recommended Metrics

Use one `Meter`, for example:

```csharp
DysonNetwork.Ring.Notification
```

## Counters

### `ring.notifications.send_requests`

Count API or service-level send requests.

Emit from:

- `NotificationController.SendNotification(...)`
- `PushService.SendNotification(...)`

Tags:

- `topic`
- `app_id`
- `push_type`
- `save`
- `source=api|service`

### `ring.notifications.target_accounts`

Count recipient accounts targeted by a send.

Emit from:

- `PushService.SendNotificationBatch(...)`
- `PushService.SendNotification(...)`

Value:

- number of target accounts

Tags:

- `topic`
- `app_id`
- `push_type`

This is the most useful denominator for "how many accounts were targeted".

### `ring.notifications.delivery_attempts`

Count concrete delivery attempts per channel/provider.

Emit from:

- WebSocket delivery path
- provider send path in `SendPushNotificationAsync(...)`

Tags:

- `topic`
- `app_id`
- `push_type`
- `provider`

Suggested provider values:

- `websocket`
- `sop`
- `google`
- `apple`
- `appk`
- `unifiedpush`

### `ring.notifications.delivery_results`

Count delivery outcomes.

Emit from the same place as `delivery_attempts`.

Tags:

- `topic`
- `app_id`
- `push_type`
- `provider`
- `outcome`

Suggested outcome values:

- `success`
- `failure`
- `invalid_token`
- `skipped`
- `no_subscription`

This metric should be the source for:

- success rate
- topic percentage
- provider percentage
- token invalidation monitoring

### `ring.notifications.preference_results`

Count preference gate results in `PushService.SendNotification(...)`.

Tags:

- `topic`
- `app_id`
- `preference`

Suggested preference values:

- `normal`
- `silent`
- `reject`

This helps explain why sends may be lower than upstream event volume.

## Histograms

### `ring.notifications.batch_size`

Emit from:

- `PushService.SendNotificationBatch(...)`

Value:

- recipient count

Tags:

- `topic`
- `app_id`
- `push_type`

### `ring.notifications.delivery_duration_ms`

Measure end-to-end provider send latency.

Emit from:

- `PushService.SendPushNotificationAsync(...)`

Tags:

- `topic`
- `app_id`
- `push_type`
- `provider`

### `ring.notifications.account_delivery_duration_ms`

Measure per-account fanout cost.

Emit from:

- `PushService.SendNotificationBatch(...)`
- or `PushService.DeliverPushNotification(...)`

Tags:

- `topic`
- `app_id`
- `push_type`

## Success Rate Definition

Success rate should be defined carefully.

Recommended formula:

```text
success_rate = delivery_results{outcome="success"} / delivery_attempts
```

This should be computed:

- by provider
- by topic
- by app
- globally

Do not define success rate from API `200 OK` responses alone, because the controller returns before downstream provider outcomes are fully reflected.

## Topic Percentage Definition

There are two useful percentages:

### Topic share of send requests

```text
send_requests{topic=*} / send_requests{all topics}
```

This answers:

- which topics are most frequently requested

### Topic share of delivery attempts

```text
delivery_attempts{topic=*} / delivery_attempts{all topics}
```

This answers:

- which topics produce the most actual downstream delivery work

These two views are both useful and should not be mixed.

## Important Current Caveat

The current implementation has one behavior that will distort success metrics if instrumented naively.

In `PushService.SendPushNotificationAsync(...)`:

- provider-specific failures are caught and logged
- the exception is swallowed to keep the worker alive
- after the catch block, the method still logs a success message

That means success logging is currently not a reliable source of truth for metrics.

Before using logs or counters for success rate, the send path should clearly distinguish:

- actual success
- recoverable failure
- invalid token cleanup
- intentionally skipped delivery

The safest pattern is:

1. set a local outcome variable
2. update it inside each provider branch
3. record metrics in one place
4. log success only when the outcome is actually success

## Suggested Low-Cardinality Tags

Prefer these tags in metrics:

- `topic`
- `provider`
- `app_id`
- `push_type`
- `outcome`
- `save`

Avoid these as metric tags:

- `account_id`
- `device_id`
- `subscription_id`
- `notification_id`
- `device_token`
- arbitrary metadata keys

High-cardinality tags are acceptable only in traces or logs when necessary.

## Suggested Dashboard Views

Useful charts:

1. total send requests per minute
2. total target accounts per minute
3. delivery attempts by provider
4. delivery results by outcome
5. success rate by provider
6. success rate by topic
7. p50/p95/p99 provider delivery latency
8. top topics by request volume
9. top topics by delivery volume
10. invalid token cleanup volume by provider

## Suggested Implementation Order

1. add `ActivitySource` and `Meter` for Ring notification delivery
2. instrument `NotificationController.SendNotification(...)` with request-level counters
3. instrument `PushService.SendNotification(...)` and `SendNotificationBatch(...)`
4. instrument `PushService.SendPushNotificationAsync(...)` with provider attempt/result counters and latency histogram
5. fix false-success logging in `SendPushNotificationAsync(...)`
6. build dashboards from `delivery_attempts` and `delivery_results`

## Summary

If the goal is to measure real notification health, the most important instrumentation point is not the controller, but the provider delivery path in `PushService.SendPushNotificationAsync(...)`.

The controller should report request intent. The service should report fanout and actual delivery outcomes. That separation will make success rate, topic percentage, and provider distribution meaningful.

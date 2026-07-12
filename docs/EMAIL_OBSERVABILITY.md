# Email Observability

This document describes Ring telemetry for email delivery. It covers queue-backed email requests and email-sending plans, with emphasis on accepted queue work, SMTP submission outcomes, and delivery latency.

The relevant implementation points are:

- `DysonNetwork.Ring/Services/RingServiceGrpc.cs` for gRPC email intake
- `DysonNetwork.Ring/Services/QueueService.cs` for NATS queue publishing
- `DysonNetwork.Ring/Services/QueueBackgroundService.cs` for queued email processing
- `DysonNetwork.Ring/Email/EmailService.cs` for SMTP delivery
- `DysonNetwork.Ring/Email/EmailSendingPlanService.cs` for scheduled-plan delivery

## OpenTelemetry Setup

Ring uses the shared `builder.AddServiceDefaults()` setup and additionally registers:

- activity source: `DysonNetwork.Ring.Email`
- meter: `DysonNetwork.Ring.Email`

Set `OTEL_EXPORTER_OTLP_ENDPOINT` to export metrics and traces through the configured OTLP exporter.

## Metrics

### `ring.emails.enqueue_requests`

Counts emails successfully published to Ring's NATS queue.

Tags:

- `source=grpc`

This measures accepted asynchronous work, not SMTP delivery.

### `ring.emails.delivery_attempts`

Counts concrete SMTP submission attempts.

Tags:

- `source=queue|sending_plan|direct`
- `provider=smtp`

### `ring.emails.delivery_results`

Counts SMTP outcomes after the actual provider call returns or throws.

Tags:

- `source=queue|sending_plan|direct`
- `provider=smtp`
- `outcome=success|failure`

`success` means the configured SMTP server accepted the message. It does not guarantee later mailbox delivery, bounce handling, or recipient engagement.

### `ring.emails.delivery_duration_ms`

Measures SMTP send duration from message construction through connect, authentication, submission, and disconnect.

Tags:

- `source=queue|sending_plan|direct`
- `provider=smtp`
- `outcome=success|failure`

## Tracing

Each SMTP send creates an `emails.deliver.smtp` span with:

- `email.source`
- `email.provider=smtp`

The span status is `Ok` only after SMTP submission succeeds; SMTP exceptions set it to `Error` and propagate to the caller. Email addresses, subjects, message bodies, account IDs, and credentials are deliberately excluded from telemetry tags.

## Success Rate

Calculate SMTP submission success rate as:

```text
delivery_results{outcome="success"} /
(delivery_results{outcome="success"} + delivery_results{outcome="failure"})
```

Break it down by `source` to distinguish failures in queued transactional mail from failures in sending plans. Do not use gRPC `200 OK` or queue-enqueue volume as the success denominator, because queue processing and SMTP submission happen later.

## Suggested Dashboards

1. queued email requests per minute
2. SMTP attempts and outcomes per minute
3. SMTP submission success rate by source
4. p50, p95, and p99 SMTP delivery duration
5. sending-plan failures compared with skipped recipients from plan-admin data
6. queue enqueue rate versus SMTP attempt rate, to identify stalled consumers

## Operational Caveat

Ring only observes the SMTP server's response. Provider-side bounces, spam filtering, suppression, and mailbox delivery require webhook or event ingestion from the email provider before they can be counted as delivered, bounced, or deferred.

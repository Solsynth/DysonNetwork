# Email Observability

Ring keeps durable, service-local email delivery records in its own database. This is built-in application observability; it does not depend on OTLP, an external metrics backend, or SMTP-provider webhooks.

The records are written after every SMTP submission attempt from:

- queued gRPC email requests (`source=queue`)
- email sending plans (`source=sending_plan`)
- direct Ring email sends (`source=direct`)

Each `email_delivery_records` row contains only the delivery source, provider (`smtp`), outcome, elapsed milliseconds, timestamp, and a truncated error message. Recipient addresses, subjects, bodies, account IDs, and credentials are deliberately not recorded.

## Outcomes and success rate

- `success`: Ring's SMTP server accepted the message.
- `failure`: Ring could not complete SMTP submission.

The success rate is calculated as:

```text
success / (success + failure)
```

It measures SMTP acceptance, not final mailbox delivery, bounces, spam filtering, or recipient engagement.

## Admin API

`GET /api/admin/delivery-observability/emails`

Requires `emails.send`. It returns totals and a per-source breakdown for the requested time range. Query parameters:

- `from`: optional ISO-8601 UTC start time
- `to`: optional ISO-8601 UTC end time

When omitted, the range is the last 30 days through the current time.

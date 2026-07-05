# Custom App Notifications

This document describes the custom app notification endpoint and its authorization rules.

Related docs:
- `docs/AUTHORIZED_APPS.md`
- `docs/APP_PRODUCTS.md`

---

## Endpoint

Padlock sends custom-app notifications through:

```http
POST /api/private/apps/{appId}/notifications
X-Api-Key: <custom_app_api_key>
```

The API key must be a valid custom app `ApiKey` secret from Develop.

---

## Authorization rules

A notification is only delivered to accounts that:
- authorized the target custom app
- still have an active authorization record
- granted the `notifications.send` scope

This is the same consent model used for app contact reading, except that contact reading uses `contacts.read`.

---

## Targeting modes

### Single account

```json
{
  "account_id": "00000000-0000-0000-0000-000000000001",
  "topic": "order.created",
  "title": "Order created",
  "body": "Your order was created."
}
```

### Multiple accounts

```json
{
  "account_ids": [
    "00000000-0000-0000-0000-000000000001",
    "00000000-0000-0000-0000-000000000002"
  ],
  "topic": "campaign.update",
  "title": "Campaign update",
  "body": "A new update is available."
}
```

### Broadcast to all authorized users

```json
{
  "broadcast_to_all": true,
  "topic": "maintenance",
  "title": "Maintenance notice",
  "body": "Scheduled maintenance starts soon."
}
```

If `broadcast_to_all` is `false`, at least one of `account_id` or `account_ids` must be provided.

---

## Request fields

Supported request body fields:
- `account_id`
- `account_ids`
- `broadcast_to_all`
- `topic`
- `title`
- `subtitle`
- `body`
- `action_uri`
- `push_type`
- `is_silent`
- `is_savable`
- `meta`

---

## Topic format

The client topic is rewritten to:

- `<publisher_name>.<app_slug>.<developer_topic>`

Example:
- developer topic: `order.created`
- app slug: `shop`
- publisher name: `Acme`
- final topic: `Acme.shop.order.created`

The prefix is system-generated and cannot be overridden.

---

## Meta behavior

Padlock preserves custom `meta`, but always injects:
- `sent_by_app`

Example:

```json
{
  "foo": "bar",
  "sent_by_app": {
    "id": "00000000-0000-0000-0000-000000000123",
    "slug": "shop",
    "name": "Shop",
    "publisher": "Acme"
  }
}
```

`sent_by_app` should be treated as trusted system metadata.

---

## Delivery behavior

Padlock resolves targets from `SnAuthorizedApp` records.

A user is eligible only when all are true:
- `app_id == {appId}`
- auth record is not deleted
- auth `type == Oidc`
- scopes include `notifications.send`

If no eligible targets are found, the endpoint still succeeds with `sent = 0`.

---

## Response

```json
{
  "sent": 12,
  "scope": "notifications.send",
  "broadcast_to_all": true
}
```

---

## Notes

- This is an app-authorized route, not a developer CRUD route.
- Product management still uses developer Bearer tokens.
- Contact-reading uses the same pattern but a different endpoint and scope:

```http
GET /api/private/apps/{appId}/accounts/{accountId}/contacts
X-Api-Key: <custom_app_api_key>
```

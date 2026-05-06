# Wallet PIN Validation

This document describes the current PIN validation behavior for wallet transfers and order payments.

## Overview

Wallet payment flows can optionally require a PIN code, depending on whether the current account has an enabled PIN auth factor.

The backend now supports two related behaviors:

- clients can query whether PIN validation is required before showing a PIN prompt
- payment endpoints accept `pin_code` as nullable when the account has no PIN configured

## PIN Status Endpoint

Route:

```text
GET /api/accounts/me/pin-status
```

Production gateway path:

```text
/passport/accounts/me/pin-status
```

Authorization:

- requires authenticated user

Response:

```json
{
  "has_pin": true,
  "validation_required": true
}
```

Response fields:

- `has_pin`: whether the current account has an enabled PIN auth factor
- `validation_required`: whether the client should require PIN input before payment actions

When the account has no enabled PIN:

```json
{
  "has_pin": false,
  "validation_required": false
}
```

## Payment Endpoint Behavior

The following wallet endpoints accept `pin_code` as optional input:

- `POST /api/wallets/transfer`
- `POST /api/wallets/funds`
- `POST /api/orders/{id}/pay`

If `pin_code` is omitted or blank, the request is normalized internally to this placeholder value:

```text
NO_PIN_PROVEDED
```

This allows older or shared validation flows to receive a non-null string value even when the client intentionally does not provide a PIN.

## No-PIN Validation Behavior

The auth validation path now treats accounts without an enabled PIN as valid for PIN verification purposes.

That means:

- accounts with a PIN still use normal PIN validation behavior
- accounts without a PIN do not fail with a server error
- payment and order flows continue normally when no PIN is configured

## Recommended Client Flow

1. Call `GET /api/accounts/me/pin-status`
2. If `validation_required` is `true`, show a PIN prompt and send the user-provided `pin_code`
3. If `validation_required` is `false`, call the payment endpoint with `pin_code: null` or omit the field

Example transfer request when PIN is required:

```json
{
  "currency": "points",
  "amount": 10,
  "payee_account_id": "11111111-1111-1111-1111-111111111111",
  "pin_code": "123456"
}
```

Example transfer request when PIN is not required:

```json
{
  "currency": "points",
  "amount": 10,
  "payee_account_id": "11111111-1111-1111-1111-111111111111",
  "pin_code": null
}
```

Example fund-creation request when PIN is not required:

```json
{
  "recipient_account_ids": [
    "11111111-1111-1111-1111-111111111111"
  ],
  "currency": "points",
  "total_amount": 25,
  "amount_of_splits": 1,
  "split_type": 0,
  "pin_code": null
}
```

Example pay-order request when PIN is not required:

```json
{
  "payer_wallet_id": "22222222-2222-2222-2222-222222222222",
  "pin_code": null
}
```

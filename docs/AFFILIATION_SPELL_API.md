# Affiliation Spell API

This document covers the generic affiliation spell recording flow and the signup integration added for create-user events.

## Overview

Affiliation spells are lightweight codes used for invite/referral style tracking.

The flow now supports two paths:

- a direct API for recording any affiliation event
- an optional create-account field that carries the affiliation spell through Padlock into Passport

## Shared Event Contract

`AccountCreatedEvent` now includes:

- `affiliation_spell` `string?`

Shared file:

- `DysonNetwork.Shared/Queue/AccountEvent.cs`

When Padlock creates an account, it publishes the field if the client supplied one.

## Padlock Create Account API

### Request

`POST /api/accounts`

Request body:

```json
{
  "name": "john_doe",
  "nick": "John",
  "email": "john@example.com",
  "password": "secret",
  "language": "en-us",
  "captcha_token": "token",
  "affiliation_spell": "ABCD1234"
}
```

### Notes

- `affiliation_spell` is optional
- if omitted, no affiliation event is recorded
- the field is passed through the account-created event

Controller:

- `DysonNetwork.Padlock/Account/AccountController.cs`

Publisher:

- `DysonNetwork.Padlock/Account/AccountService.cs`

## Passport Recording API

### Record affiliation result

`POST /api/affiliations/{spell}/results`

Request body:

```json
{
  "resource_identifier": "account:uuid"
}
```

### Response

Returns the created `SnAffiliationResult` record.

### Behavior

- resolves the spell by its spell word
- stores a result row in `affiliation_results`
- returns `400 Bad Request` if the spell does not exist

Controller:

- `DysonNetwork.Passport/Affiliation/AffiliationSpellController.cs`

Service:

- `DysonNetwork.Passport/Affiliation/AffiliationSpellService.cs`

## Automatic Create-User Recording

Passport also listens to `AccountCreatedEvent` and records the affiliation result automatically when `affiliation_spell` is present.

Recorded resource identifier:

- `account:{accountId}`

Listener:

- `DysonNetwork.Passport/Startup/ServiceCollectionExtensions.cs`

## Example Flow

1. Client creates an account and includes `affiliation_spell`.
2. Padlock publishes `AccountCreatedEvent` with the spell.
3. Passport receives the event.
4. Passport records `account:{accountId}` against the spell.

## Error Cases

- invalid spell word -> `400 Bad Request`
- missing `resource_identifier` -> model validation failure

# Wallet Transaction Lifecycle & Fund Raising

## Overview

This document describes the new wallet transaction lifecycle system with opt-in frozen transfers, opt-in manual confirmation, and the fund raising mode for collecting contributions.

## Transaction Lifecycle

### New TransactionStatus Enum

```csharp
public enum TransactionStatus
{
    Pending,      // Created, funds held from payer, not yet credited to payee
    Frozen,       // Funds held for 24hr settlement period
    Confirmed,    // Payee confirmed (or freeze period elapsed), funds released
    Refunded,     // Returned to payer (rejection or expiration)
    Cancelled     // Cancelled before funds were held
}
```

### Behavior Matrix

| `freeze` | `requireConfirmation` | Flow |
|----------|----------------------|------|
| `false`  | `false`              | **Instant**: Debit payer + credit payee immediately. Status = `Confirmed`. |
| `true`   | `false`              | **Frozen**: Deduct payer pocket, start 24hr freeze. Auto-release after 24hr. Payer cannot cancel. |
| `false`  | `true`               | **Confirm-required**: Deduct payer pocket, wait for payee confirm. Auto-refund after 24hr if no confirm. |
| `true`   | `true`               | **Both**: Deduct payer pocket, 24hr freeze + payee must confirm. Both conditions met → release; otherwise auto-refund. |

### Held Funds Tracking

When a transaction is `Pending` or `Frozen`:
- Amount is deducted from payer's pocket `Amount`
- Amount is added to payer's pocket `HeldAmount`
- Payee does NOT receive funds until transaction is confirmed
- Wallet balance: `Available = Amount - HeldAmount`

---

## API Changes

### Transfer Endpoint (Extended)

**`POST /api/wallets/transfer`**

New request body fields:

```json
{
  "remark": "Optional note",
  "amount": 100.00,
  "currency": "points",
  "payee_account_id": "uuid",
  "freeze": true,
  "require_confirmation": true
}
```

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `freeze` | `bool` | `false` | Hold funds for 24hr before clearing |
| `require_confirmation` | `bool` | `false` | Require payee to confirm receipt |

**Response**: Returns the created `SnWalletTransaction` with `status`, `expires_at`, etc.

---

### Confirm Transaction

**`POST /api/wallets/transactions/{id}/confirm`**

Payee confirms receipt of a pending/frozen transaction. Releases held funds to payee.

**Authorization**: User must be the payee of the transaction.

**Response**:
```json
{
  "id": "uuid",
  "status": "Confirmed",
  "confirmed_at": "2024-01-15T10:30:00Z"
}
```

**Errors**:
- `400` — Transaction not found, already processed, or expired
- `403` — User is not the payee

---

### Reject Transaction

**`POST /api/wallets/transactions/{id}/reject`**

Payee rejects a pending/frozen transaction. Refunds held funds to payer.

**Authorization**: User must be the payee of the transaction.

**Response**:
```json
{
  "id": "uuid",
  "status": "Refunded"
}
```

**Errors**:
- `400` — Transaction not found or already processed
- `403` — User is not the payee

---

### List Pending Transactions

**`GET /api/wallets/transactions/pending`**

Returns transactions awaiting the authenticated user's confirmation.

**Query Parameters**:

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `offset` | `int` | `0` | Pagination offset |
| `take` | `int` | `20` | Number of results |

**Response Headers**:
- `X-Total` — Total count of pending transactions

**Response**: Array of `SnWalletTransaction` objects with payer wallet and account info hydrated.

---

## Fund Raising Mode

### New Enums

```csharp
public enum ContributionType
{
    Free,   // Contributor chooses any amount
    Fixed   // Contributor pays a predefined amount
}
```

### New Fields on SnWalletFund

| Field | Type | Description |
|-------|------|-------------|
| `is_raising` | `bool` | `false` = distribute mode, `true` = collect contributions |
| `target_amount` | `decimal` | Fundraising goal (0 = no goal / unlimited) |
| `contribution_type` | `ContributionType` | `Free` or `Fixed` |
| `contribution_amount` | `decimal` | Per-person amount when `Fixed` |
| `deadline_at` | `Instant?` | Optional deadline for raising period |

### IsOpen Reuse

The existing `IsOpen` property is reused:
- `IsOpen = false` (closed) — Specific invited participants only
- `IsOpen = true` (open) — Anyone can contribute

---

## Fund Raising API

### Create Fund (Extended)

**`POST /api/wallets/funds`**

New request body fields for raising mode:

```json
{
  "currency": "points",
  "is_raising": true,
  "target_amount": 1000.00,
  "amount_of_splits": 50,
  "contribution_type": "Free",
  "contribution_amount": 0,
  "is_open": true,
  "deadline_at": "2024-12-31T23:59:59Z",
  "message": "Help us reach our goal!",
  "expiration_hours": 72
}
```

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `is_raising` | `bool` | `false` | Enable raising mode |
| `target_amount` | `decimal` | `0` | Fundraising goal (0 = unlimited) |
| `contribution_type` | `ContributionType` | `Free` | `Free` (any amount) or `Fixed` |
| `contribution_amount` | `decimal` | `0` | Required when `contribution_type` is `Fixed` |
| `is_open` | `bool` | `true` | `true` = open to all, `false` = invited only |
| `deadline_at` | `Instant?` | `null` | Optional deadline for contributions |
| `recipient_account_ids` | `List<Guid>` | `[]` | Invited participants (when `is_open` = `false`) |

**Behavior**:
- For `Free` contribution: `amount_of_splits` sets max participants (0 = unlimited)
- For `Fixed` contribution: `contribution_amount` × `amount_of_slics` should equal `target_amount`
- `deadline_at` is optional; fund expires at `expiration_hours` if no deadline

---

### Contribute to Fund

**`POST /api/wallets/funds/{id}/contribute`**

Contribute money to a raising fund.

**Request Body**:
```json
{
  "amount": 50.00
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `amount` | `decimal` | Only for `Free` type | Contribution amount (ignored for `Fixed` type) |

**Response**: Returns a `SnWalletTransaction` for the contribution.

**Errors**:
- `400` — Fund not found, not in raising mode, expired, or already contributed
- `400` — Insufficient funds or amount exceeds target
- `403` — Not invited (for closed funds)

---

### List Contributors

**`GET /api/wallets/funds/{id}/contributors`**

Returns list of contributors for a raising fund.

**Response**: Array of `SnWalletFundRecipient` objects with account info hydrated.

**Errors**:
- `404` — Fund not found
- `400` — Fund is not in raising mode

---

## Background Jobs

### TransactionExpirationJob

**Schedule**: Every 15 minutes

**Logic**:
1. Find transactions where `expires_at < now` AND `status IN (Pending, Frozen)`
2. If `is_frozen = true` AND `require_confirmation = false`:
   - Release funds to payee
   - Status → `Confirmed`
3. If `require_confirmation = true`:
   - Refund funds to payer
   - Status → `Refunded`

---

### FundRaisingDeadlineJob

**Schedule**: Every 15 minutes

**Logic**:
1. Find raising funds where `deadline_at < now` AND `status IN (Created, PartiallyReceived)`
2. If `target_amount > 0` AND `raised_amount >= target_amount`:
   - Status → `FullyReceived`
3. If target not reached:
   - Status → `Expired`
   - Refund all contributions to contributors

---

## Model Changes Summary

### SnWalletTransaction (New Fields)

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `status` | `TransactionStatus` | `Confirmed` | Transaction lifecycle status |
| `is_frozen` | `bool` | `false` | Payer opted into 24hr hold |
| `require_confirmation` | `bool` | `false` | Payer opted into payee-confirm flow |
| `frozen_at` | `Instant?` | `null` | When freeze started |
| `expires_at` | `Instant?` | `null` | Confirmation/freeze deadline |
| `confirmed_at` | `Instant?` | `null` | When payee confirmed or freeze resolved |

### SnWalletPocket (New Fields)

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `held_amount` | `decimal` | `0` | Amount held in pending/frozen transactions |
| `available_amount` | `decimal` | (computed) | `Amount - HeldAmount` |

### SnWalletFund (New Fields)

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `is_raising` | `bool` | `false` | Fund is in raising mode |
| `target_amount` | `decimal` | `0` | Fundraising goal |
| `contribution_type` | `ContributionType` | `Free` | Free or Fixed contribution |
| `contribution_amount` | `decimal` | `0` | Amount for Fixed type |
| `deadline_at` | `Instant?` | `null` | Optional deadline |
| `raised_amount` | `decimal` | (computed) | Sum of contributions |

---

## Migration Notes

New database columns will be added to:
- `payment_transactions` — `status`, `is_frozen`, `require_confirmation`, `frozen_at`, `expires_at`, `confirmed_at`
- `wallet_pockets` — `held_amount`
- `wallet_funds` — `is_raising`, `target_amount`, `contribution_type`, `contribution_amount`, `deadline_at`

All new fields have default values ensuring backward compatibility with existing data.

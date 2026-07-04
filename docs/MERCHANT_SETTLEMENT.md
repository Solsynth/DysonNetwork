# Merchant Settlement System

The merchant settlement system unifies payout handling for apps and publishers. Instead of funds being credited to the developer/publisher wallet immediately, they are **held in escrow** and released through a settlement step — either automatically at midnight or manually on request.

## Overview

Two entities can receive payments:

| Entity | Wallet field | Source of funds |
|---|---|---|
| **Custom App** (`SnCustomApp`) | `PaymentWalletId` | Product purchases via orders |
| **Publisher** (`SnPublisher`) | `PayoutWalletId` | Post awards / tips |

Both are modeled as a **Merchant** (`SnMerchant`), auto-created when a wallet is configured. Incoming payments create **Merchant Settlement** records (`SnMerchantSettlement`) in `Pending` status. The actual wallet credit happens only on settlement.

## API Endpoints

### Check settlement status

Resolves the merchant for an app or publisher and returns pending totals by currency.

**App:**
```
GET /api/private/apps/{appId}/settlements?dev={dev}&proj={proj}
```
Authorization: must be an editor of the developer.

**Publisher:**
```
GET /api/publishers/{name}/settlements
```
Authorization: must be an editor of the publisher.

**Response (200):**
```json
{
  "merchantId": "a1b2c3d4-...",
  "hasWallet": true,
  "walletId": "e5f6g7h8-...",
  "pending": {
    "points": { "count": 12, "total": 1500.0 },
    "golds":  { "count": 3,  "total": 300.0 }
  }
}
```

If no wallet is configured:
```json
{
  "merchantId": null,
  "hasWallet": false,
  "pending": {}
}
```

### List all settlements (paginated)
```
GET /api/merchants/{merchantId}/settlements?offset=0&take=20
```
Authorization: wallet owner (the account that owns the payout wallet).

Returns a paginated list of `SnMerchantSettlement` objects, newest first.

### Manual settlement
```
POST /api/merchants/{merchantId}/settlements/settle
```
Authorization: wallet owner.

Settles all pending settlements for this merchant immediately. Funds are credited to the wallet in one transaction per currency.

**Response (200):**
```json
{
  "message": "Settled 2 currency group(s)",
  "transactions": [
    { "id": "...", "currency": "points", "amount": 1500.0 },
    { "id": "...", "currency": "golds", "amount": 300.0 }
  ]
}
```

If nothing is pending:
```json
{ "message": "No pending funds to settle" }
```

## Lifecycle

```
┌──────────────┐     ┌───────────────┐     ┌──────────────┐
│ Payment made │ ──→ │ Settlement    │ ──→ │ Funds        │
│ (order/award)│     │ Pending       │     │ in wallet    │
└──────────────┘     └───────┬───────┘     └──────────────┘
                             │
                    ┌────────┴────────┐
                    │                 │
              Daily job         Manual API
              (midnight)     (POST /settle)
```

### App Product Purchase Flow

1. User creates an order for an app product (`POST /api/orders`)
2. User pays the order (`POST /api/orders/{id}/pay`)
3. Payer's wallet is **deducted**. Funds are NOT credited to the developer yet.
4. A `SnMerchantSettlement` is created (Status = `Pending`)
5. At midnight, `AppSettlementJob` settles all pending settlements:
   - Groups by wallet + currency
   - Creates one credit transaction per group: system → developer wallet
   - Marks settlements as `Settled`
6. Or the developer calls manual settle to trigger step 5 immediately.

### Post Award Flow

1. User awards a post with a positive attitude (`POST /api/posts/{id}/awards`)
2. Award record is created (`SnPostAward` with `SettledAt = null`)
3. A `SnMerchantSettlement` is created for 80% of the award amount
4. `PublisherService.SettlePostAwardsAsync` (midnight) ensures all awards have settlements and marks them settled
5. `AppSettlementJob` (midnight, after step 4) transfers funds to publisher wallets

### Cancellation / Refund

When an app order is cancelled **before settlement**:
1. `PATCH /api/orders/{id}/status` with `Status = Cancelled`
2. Pending settlement is cancelled (`Status = Cancelled`)
3. Payer is refunded: system → payer wallet (since funds were held in escrow, not yet credited to developer)

If cancelled **after settlement**, the standard `RefundOrderAsync` path applies (developer wallet → payer).

## Settlement Data Model

```
SnMerchant                          (one per publisher)
├── Id
├── PublisherId                     (SnPublisher.Id — works for both app-developers
│                                   and standalone publishers)
├── PaymentWalletId                 (target wallet for payouts)
├── Name
└── Settlements[] → SnMerchantSettlement
                     ├── OrderId / AwardId      (source reference)
                     ├── PaymentTransactionId   (escrow tx, nullable for awards)
                     ├── PaymentWalletId        (where funds land)
                     ├── Currency, Amount
                     ├── Status (Pending|Settled|Cancelled)
                     ├── SettledBy (Automatic|Manual)
                     ├── SettledAt
                     └── SettlementTransactionId (system → wallet credit tx)
```

Apps and publishers share the same merchant concept: the **publisher** is the entity that receives money. An app belongs to a developer, which has a publisher — so the app's publisher is the merchant. A standalone publisher is its own merchant. One merchant per publisher, regardless of how many apps they own.

## Scheduled Jobs

| Job | Schedule | What it does |
|---|---|---|
| `AppSettlementJob` | Midnight daily (`0 0 0 * * ?`) | Settles all pending settlements grouped by wallet+currency |
| `SettlePostAwardsAsync` | Midnight daily (via Quartz in Sphere) | Ensures awards have settlements; marks `SnPostAward.SettledAt` |

## Idempotency

- `SettleWalletAsync` only processes settlements with `Status = Pending`. Calling it twice on the same wallet is safe — the second call finds nothing pending.
- `SettlePostAwardsAsync` skips awards that already have a corresponding `MerchantSettlement` (checked via `existingSettlementAwardIds`).
- Cancelling an already-settled settlement is a no-op (check: `Status == Pending`).

## Migration Notes

When deploying this system:
1. Run the `AddMerchantSettlement` migration (creates `merchants` and `merchant_settlements` tables)
2. Run the `MakePaymentTransactionIdNullable` migration
3. Merchants are auto-created on-the-fly when a payment arrives for an app or publisher with a configured wallet. No manual backfill needed.

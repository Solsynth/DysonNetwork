# Merchant Settlement System — Plan

## Context

Two separate domains have the exact same problem:

### 1. Develop — CustomApp payments (`CustomAppController` / `PaymentService`)

When a user buys an app product:
- `OrderController.CreateOrder` resolves payee from `customApp.PaymentWalletId`
- `PaymentService.PayOrderAsync` calls `CreateTransactionAsync(payer, payee, amount)` — payer is **deducted** and payee (developer's wallet) is **credited immediately**
- If `PaymentWalletId` is null: payer is deducted but **nobody receives the funds** — money is silently burned

### 2. Sphere — Publisher post award settlement (`PublisherService.SettlePostAwardsAsync`)

When a user awards/tips a post:
- `SnPostAward` record is created with `SettledAt = null`
- Some scheduler invokes `SettlePostAwardsAsync()` which loops over unsettled awards
- If `publisher.AccountId` is set → credits that account (via gRPC)
- Else if `publisher.PayoutWalletId` is set → credits that wallet (via gRPC)
- Else → **silently holds** (logs and skips): `"Holding {Count} post awards for publisher {PublisherId}: no payout wallet configured"`

Both need the same thing: **hold funds on payment, then settle later** (daily batch or manual trigger).

---

## The Merchant Abstraction

A "merchant" is any entity that receives incoming payments. We model it as a first-class concept in the Wallet domain.

```
┌─────────────────────────────────────────────────┐
│                   Merchant                       │
│  Id, Type (App|Publisher), EntityId,            │
│  PaymentWalletId, CreatedAt, UpdatedAt          │
└──────────────────┬──────────────────────────────┘
                   │ 1:N
                   ▼
┌─────────────────────────────────────────────────┐
│              MerchantSettlement                  │
│  Id, MerchantId, OrderId/AwardId,               │
│  TransactionId (escrow tx), Currency, Amount,   │
│  Status (Pending|Settled|Cancelled),            │
│  SettledBy (Automatic|Manual), SettledAt,       │
│  SettlementTransactionId                        │
└─────────────────────────────────────────────────┘
```

**MerchantTypes:**
- `App` — maps to `SnCustomApp.Id`
- `Publisher` — maps to `SnPublisher.Id`

### Where the `PaymentWalletId` lives

Currently `PaymentWalletId` is on `SnCustomApp` and `PayoutWalletId` is on `SnPublisher` — two different names for the same concept. The merchant record consolidates this: one field, one place to change it. The existing fields on the entity models stay as-is (for backward compat) but the **merchant record is the source of truth** for settlement destination.

**Auto-creation:** When `SnCustomApp.PaymentWalletId` or `SnPublisher.PayoutWalletId` is set/changed, a `SnMerchant` record is created or updated. This can be done:
- In `CustomAppService.UpdateAppAsync` (already detects when `PaymentWalletId` changes)
- In `PublisherController` update method (already checks `request.PayoutWalletId.HasValue`)
- Both call a shared `MerchantService.UpsertMerchantAsync(type, entityId, walletId)`

---

## Payment Flow Changes

### For App payments

**Before:** buyer pays → `PayOrderAsync` → `CreateTransactionAsync(payer, appWallet)` → developer wallet credited instantly

**After:** buyer pays → `PayOrderAsync` detects order has `AppIdentifier`:
1. `CreateTransactionAsync(payer, payee=null, holdInEscrow=true)` — payer deducted, **nobody credited yet**
2. `MerchantService.CreateSettlementAsync(appId, orderId, txId, currency, amount)` — settlement record created, Status=Pending
3. Funds are in escrow (deducted from payer, not credited to anyone)

### For Publisher post awards

**Before:** `SettlePostAwardsAsync()` called by a scheduler, iterates unsettled awards, immediately credits wallet

**After:** When `SnPostAward` is created (positive attitude, i.e., tipping event):
1. A `MerchantSettlement` record is created immediately, Status=Pending
2. The existing `SettlePostAwardsAsync` is **replaced** by the merchant settlement system
3. Award's `SettledAt` is set when the merchant settlement is settled

---

## Settlement Execution

### Daily automatic settlement (Quartz cron: midnight)

```
AppSettlementJob.Execute():
  1. Group all Pending settlements by PaymentWalletId + Currency
  2. For each group:
     a. Sum amounts
     b. CreateTransactionAsync(payer=null, payee=wallet, total, remarks="Settlement: N orders/awards")
     c. Mark each settlement: Status=Settled, SettledBy=Automatic, SettledAt=now
     d. If PostAward: set award.SettledAt = now
```

### Manual settlement (developer/publisher API)

```
POST /api/merchants/{merchantId}/settle
  → same logic as daily job but scoped to a single merchant
  → SettledBy=Manual
```

---

## Files to Create

| File | Location | Purpose |
|---|---|---|
| `SnMerchant.cs` | `DysonNetwork.Shared/Models/` | Merchant + MerchantSettlement models |
| `MerchantService.cs` | `DysonNetwork.Wallet/Payment/` | Core merchant/settlement logic |
| `MerchantController.cs` | `DysonNetwork.Wallet/Payment/` | Settlement query + manual settle APIs |
| `AppSettlementJob.cs` | `DysonNetwork.Wallet/Payment/` | Quartz daily settlement job |

## Files to Modify

| File | Change | Why |
|---|---|---|
| `SnCustomApp.cs` | No change needed (keep `PaymentWalletId`) | Backward compat; merchant record is new source of truth |
| `SnPublisher.cs` | No change needed (keep `PayoutWalletId`) | Same reasoning |
| `PaymentService.cs` (`PayOrderAsync`) | ~3 lines: detect app orders, skip payee credit, call `MerchantService.CreateSettlementAsync` | Escrow instead of instant payout |
| `PaymentService.cs` (`CreateTransactionAsync`) | Add optional `holdInEscrow` parameter; skip payee credit when true | Reuses existing null-payee path |
| `PublisherService.cs` (`SettlePostAwardsAsync`) | Replace with settlement-based flow; call `MerchantService.CreateSettlementAsync` per award | Unify into the merchant system |
| `CustomAppService.cs` (`UpdateAppAsync`) | Call `MerchantService.UpsertMerchantAsync` when `PaymentWalletId` changes | Auto-create merchant record |
| `PublisherController.cs` | Call `MerchantService.UpsertMerchantAsync` when `PayoutWalletId` changes | Auto-create merchant record |
| `ScheduledJobsConfiguration.cs` (Wallet) | Register `AppSettlementJob` | Daily cron |
| `EventBus` / post-award creation | When `SnPostAward` is created with `Positive` attitude → create `MerchantSettlement` | Hooks into the tipping flow |

### Database (AppDatabase — shared across services)

```csharp
public DbSet<SnMerchant> Merchants { get; set; }
public DbSet<SnMerchantSettlement> MerchantSettlements { get; set; }
```

---

## API Endpoints

```
GET  /api/merchants/{merchantId}/settlements          → list settlements (paginated)
GET  /api/merchants/{merchantId}/settlements/pending  → pending totals grouped by currency
POST /api/merchants/{merchantId}/settlements/settle   → manual settlement (Auth: must be Owner/Editor of the underlying entity)
```

Authentication: resolve the entity from the merchant record (app → check developer membership; publisher → check publisher membership), then enforce `Editor` role minimum.

---

## Steps

- [ ] 1. Create `SnMerchant` and `SnMerchantSettlement` models in `DysonNetwork.Shared/Models/Merchant.cs`
- [ ] 2. Add `DbSet<SnMerchant>` and `DbSet<SnMerchantSettlement>` to `AppDatabase` (Wallet)
- [ ] 3. Create migration
- [ ] 4. Create `MerchantService` with:
  - `UpsertMerchantAsync(type, entityId, walletId)` — creates/updates merchant
  - `CreateSettlementAsync(merchantId, orderId/awardId, txId, currency, amount)` — creates pending settlement
  - `GetPendingSettlementsAsync(walletId)` — grouped by wallet + currency
  - `SettleAsync(walletId, trigger)` — groups pending by currency, credits wallet, marks settled
- [ ] 5. Add `holdInEscrow` parameter to `CreateTransactionAsync` in `PaymentService`
- [ ] 6. Modify `PayOrderAsync`: when `order.AppIdentifier` is set, pass `holdInEscrow=true` and call `MerchantService.CreateSettlementAsync`
- [ ] 7. Modify `CustomAppService.UpdateAppAsync`: on `PaymentWalletId` change, call `MerchantService.UpsertMerchantAsync`
- [ ] 8. Modify `PublisherController.Update` endpoint: on `PayoutWalletId` change, call `MerchantService.UpsertMerchantAsync`
- [ ] 9. Modify `PublisherService.SettlePostAwardsAsync`: replace per-award settlement logic with `MerchantService.CreateSettlementAsync` per award + remove immediate payout
- [ ] 10. Find where `SnPostAward` is created for tipping (positive attitude) — add `MerchantService.CreateSettlementAsync` call
- [ ] 11. Add `MerchantController` with list/query/manual-settle endpoints
- [ ] 12. Create `AppSettlementJob` (Quartz `IJob`) for daily batch settlement
- [ ] 13. Register `AppSettlementJob` in `ScheduledJobsConfiguration` with midnight cron
- [ ] 14. Handle cancellation/refund: when order is cancelled before settlement, mark settlement as `Cancelled` and refund payer

---

## Verification

1. **Unit test flow:** Create app → set wallet → create order → pay → verify settlement record is Pending → trigger daily job → verify settlement is Settled and wallet is credited
2. **Manual settle flow:** Same as above but call `POST /api/merchants/{id}/settlements/settle` instead of waiting for job
3. **Publisher awards:** Award a post → verify `SnMerchantSettlement` created → settle via job or API → verify `SnPostAward.SettledAt` is set
4. **No wallet configured:** Create app/publisher without wallet → pay/award → verify merchant doesn't exist → order/award rejected or held gracefully (no silent burn)
5. **Cancellation before settlement:** Cancel order → verify settlement record → `Cancelled`, refund transaction created to payer
6. **No duplicate credits:** Verify settle is idempotent — calling settle twice on same records doesn't double-credit

# Merchant Settlement System

The merchant settlement system centralizes payout handling for apps and publishers in the Wallet service. Instead of crediting wallets immediately, funds are **held in escrow** and released through settlement — either daily (midnight cron) or manual API call.

## Core Concept

**Merchant = publisher identity in Wallet.** A developer in Develop is a copy of the same publisher. Both share the same `PublisherId`. The merchant is Wallet's mirror of that identity.

```
Sphere/Develop                 Wallet
─────────────                  ──────
SnPublisher (id: X)  ←→  SnMerchant (publisher_id: X)
SnDeveloper (publisher_id: X)    ↕
                                 SnMerchantSettlement[]
```

One merchant per publisher. Settlements from all apps and post awards under that publisher flow through the same merchant.

## Architecture

```
Sphere (dyson_sphere)               Wallet (dyson_wallet)
───────────────────                 ──────────────────
SnPublisher                         SnMerchant (publisher_id)
SnDeveloper ──────────────────→     SnMerchantSettlement[]
SnPostAward

HTTP endpoints:                    HTTP endpoints:
  /api/publishers                   /api/merchants/{id}/settlements
  /api/posts/{id}/awards            /api/merchants/{id}/settlements/pending
                                    /api/merchants/{id}/settlements/settle

gRPC client:                       gRPC server:
  RemoteMerchantService ────────→   MerchantServiceGrpc
```

| Component | Service | Location | Purpose |
|---|---|---|---|
| `DyMerchantService` proto | Shared | `Spec/proto/wallet.proto` | gRPC contract |
| `RemoteMerchantService` | Sphere, Develop | `Shared/Registry/` | Client wrapper |
| `MerchantServiceGrpc` | **Wallet** | `Wallet/Payment/` | gRPC server implementation |
| `MerchantService` | **Wallet** | `Wallet/Payment/` | Business logic |
| `MerchantController` | **Wallet** | `Wallet/Payment/` | HTTP API for queries + manual settle |
| `AppSettlementJob` | **Wallet** | `Wallet/Payment/` | Quartz midnight batch settlement |
| `SnMerchant` model | **Wallet** | `Shared/Models/Merchant.cs` | Merchant entity |
| `SnMerchantSettlement` model | **Wallet** | `Shared/Models/Merchant.cs` | Settlement entity |

## API Endpoints

### Wallet Service (`DysonNetwork.Wallet`)

| Method | Endpoint | Auth | Purpose |
|---|---|---|---|
| `GET` | `/api/merchants/{merchantId}/settlements?offset=0&take=20` | wallet owner | List settlements (paginated) |
| `GET` | `/api/merchants/{merchantId}/settlements/pending` | wallet owner | Pending totals by currency |
| `POST` | `/api/merchants/{merchantId}/settlements/settle` | wallet owner | Manual instant settlement |
| `GET` | `/api/merchants/{merchantId}/stats/overview` | wallet owner | Overview stats: pending/settled/all-time counts + by-currency breakdowns |
| `GET` | `/api/merchants/{merchantId}/stats/incoming?from&to&currency` | wallet owner | Period incoming: settlements in date range, grouped by status + currency |
| `GET` | `/api/merchants/{merchantId}/stats/daily?from&to&currency` | wallet owner | Daily incoming totals for charting |

### Sphere Service (`DysonNetwork.Sphere`)

| Method | Endpoint | Auth | Purpose |
|---|---|---|---|
| `POST` | `/api/publishers` | account | Create publisher (triggers merchant creation via gRPC) |
| `PATCH` | `/api/publishers/{name}` | owner | Update publisher — setting `PayoutWalletId` triggers `UpsertMerchant` gRPC |
| `POST` | `/api/posts/{id}/awards` | account | Award a post — triggers `CreateMerchantSettlement` gRPC for positive tips |

### Develop Service (`DysonNetwork.Develop`)

| Method | Endpoint | Auth | Purpose |
|---|---|---|---|
| `POST` | `/api/private/apps` | editor | Create app (no merchant side-effect) |
| `PATCH` | `/api/private/apps/{appId}` | editor | Update app — stores `PaymentWalletId` for reference, no merchant RPC |

### Cross-Service gRPC (`DyMerchantService`)

| RPC | Request | Response | Called by | Handles in |
|---|---|---|---|---|
| `UpsertMerchant` | publisherId, walletId?, name? | DyMerchant | Sphere, Develop | Wallet |
| `CreateMerchantSettlement` | publisherId, orderId?, awardId?, currency, amount | DyMerchantSettlement | Sphere | Wallet |
| `GetPendingSettlements` | paymentWalletId | settlements[] | Sphere, Develop | Wallet |
| `SettleMerchant` | paymentWalletId | transactions[] | Sphere, Develop | Wallet |

## Stats & Reporting

All stats endpoints require wallet owner auth and operate on the merchant's settlement data.

### Overview `GET /api/merchants/{merchantId}/stats/overview`

Returns aggregate counts and by-currency breakdowns for pending, settled, and current-month settlements.

```json
{
  "totalPending": 12,
  "totalSettled": 145,
  "totalAllTime": 157,
  "pending": {
    "points": { "count": 5, "total": 250.0 },
    " premium": { "count": 7, "total": 700.0 }
  },
  "settled": {
    "points": { "count": 80, "total": 4000.0 },
    "premium": { "count": 65, "total": 6500.0 }
  },
  "thisMonth": {
    "points": { "count": 20, "total": 1000.0 },
    "premium": { "count": 15, "total": 1500.0 }
  }
}
```

### Period Incoming `GET /api/merchants/{merchantId}/stats/incoming?from&to&currency`

Returns settlements created within a date range, grouped by status and currency.
`from` / `to` are optional `DateTime` (UTC); defaults to last 30 days.

```json
{
  "from": "2026-01-01T00:00:00Z",
  "to": "2026-01-31T00:00:00Z",
  "currency": null,
  "totalCount": 42,
  "totalAmount": 3150.0,
  "byStatus": {
    "Pending": { "count": 5, "total": 250.0 },
    "Settled": { "count": 37, "total": 2900.0 }
  },
  "byCurrency": {
    "points": { "count": 30, "total": 1500.0 },
    "premium": { "count": 12, "total": 1650.0 }
  },
  "settlements": [ { "id": "...", "amount": 50.0, "currency": "points", ... } ]
}
```

### Daily Incoming `GET /api/merchants/{merchantId}/stats/daily?from&to&currency`

Returns daily totals for charting revenue over time.

```json
{
  "from": "2026-01-01T00:00:00Z",
  "to": "2026-01-31T00:00:00Z",
  "currency": null,
  "daily": [
    { "date": "2026-01-01", "count": 3, "total": 150.0, "byCurrency": { "points": 100.0, "premium": 50.0 } },
    { "date": "2026-01-02", "count": 5, "total": 250.0, "byCurrency": { "points": 200.0, "premium": 50.0 } }
  ]
}
```

## Lifecycle

### App Product Purchase

| Step | Service | What happens |
|---|---|---|
| 1 | Sphere | User creates order for app product → `POST /api/orders` |
| 2 | Sphere | User pays order → `POST /api/orders/{id}/pay` |
| 3 | **Wallet** | `PayOrderAsync` resolves publisherId via `GetAppDeveloperAsync` gRPC to Develop |
| 4 | **Wallet** | Finds or auto-creates merchant for that publisher |
| 5 | **Wallet** | Deducts payer wallet, creates settlement (Status = Pending). Nobody credited yet. |
| 6 | **Wallet** | `AppSettlementJob` (midnight) or manual `POST /settle` → credits merchant wallet |

### Post Award

| Step | Service | What happens |
|---|---|---|
| 1 | Sphere | User awards post → `PostService.AwardPost` |
| 2 | Sphere → **Wallet** | `CreateMerchantSettlementAsync` gRPC with awardId, publisherId, amount |
| 3 | **Wallet** | Finds or auto-creates merchant; creates settlement (80% of award) |
| 4 | **Wallet** | `AppSettlementJob` settles to publisher wallet |

### Publisher Wallet Configuration

| Step | Service | What happens |
|---|---|---|
| 1 | Sphere | `PATCH /api/publishers/{name}` with `PayoutWalletId` |
| 2 | Sphere → **Wallet** | `UpsertMerchantAsync` gRPC |
| 3 | **Wallet** | Creates/updates merchant record with the wallet |

### Cancellation

| Step | Service | What happens |
|---|---|---|
| 1 | Sphere | `PATCH /api/orders/{id}/status` → `Cancelled` |
| 2 | **Wallet** | Cancels pending settlement + refunds payer (system → payer) |

### Merchant Auto-Creation

| Trigger | Service | How |
|---|---|---|
| Publisher sets `PayoutWalletId` | Sphere → **Wallet** | `UpsertMerchantAsync` gRPC |
| First app order paid | **Wallet** | `PayOrderAsync` auto-creates merchant with order's wallet |
| First post award | Sphere → **Wallet** | `CreateMerchantSettlementAsync` gRPC auto-creates merchant |

## Data Model

```
SnMerchant (in Wallet's DB, publisher_id is the key)
├── Id
├── PublisherId          ← SnPublisher.Id
├── PaymentWalletId      ← where settled funds land
├── Name
└── Settlements[] → SnMerchantSettlement
                     ├── OrderId? / AwardId?
                     ├── PaymentWalletId
                     ├── Currency, Amount
                     ├── Status (Pending|Settlement|Cancelled)
                     ├── SettledBy (Automatic|Manual?)
                     ├── SettledAt?
                     └── SettlementTransactionId?
```

## RPC Methods

| Method | Request | Response | Called by |
|---|---|---|---|
| `UpsertMerchant` | publisherId, walletId?, name? | DyMerchant | Sphere, Develop |
| `CreateMerchantSettlement` | publisherId, orderId?, awardId?, currency, amount | DyMerchantSettlement | Sphere |
| `GetPendingSettlements` | paymentWalletId | settlements[] | Sphere, Develop |
| `SettleMerchant` | paymentWalletId | transactions[] | Frontend (via MerchantController) |

## Registration

### Wallet service — gRPC server + job

In `ApplicationConfiguration.ConfigureAppMiddleware`:
```csharp
app.MapGrpcService<MerchantServiceGrpc>();
```

In `ServiceCollectionExtensions.AddAppBusinessServices`:
```csharp
services.AddScoped<MerchantService>();
services.AddScoped<MerchantServiceGrpc>();
```

In `ScheduledJobsConfiguration`:
```csharp
q.AddJob<AppSettlementJob>(opts => opts.WithIdentity("AppSettlement"));
q.AddTrigger(opts => opts
    .ForJob("AppSettlement")
    .WithCronSchedule("0 0 0 * * ?"));  // midnight UTC
```

### Sphere / Develop services — gRPC client

In `ServiceInjectionHelper.AddWalletService()`:
```csharp
services.AddGrpcClientWithSharedChannel<DyMerchantService.DyMerchantServiceClient>(
    "https://_grpc.wallet", "DyMerchantService");
services.AddSingleton<RemoteMerchantService>();
```

### Sphere — RemotePaymentService (existing, related)

```csharp
services.AddGrpcClientWithSharedChannel<DyPaymentService.DyPaymentServiceClient>(
    "https://_grpc.wallet", "DyPaymentService");
services.AddSingleton<RemotePaymentService>();
```

## Idempotency

- `SettleWalletAsync` only processes settlements with `Status = Pending`. Calling twice is safe.
- `SettlePostAwardsAsync` calls `CreateMerchantSettlementAsync` gRPC for each award; Wallet is idempotent per awardId (merchant auto-creates if needed).
- Cancelling an already-settled settlement is no-op.

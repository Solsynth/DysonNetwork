# Subscription Catalog Overhaul

## Summary

The subscription system is no longer driven by hard-coded tier metadata in code.

Wallet now owns a configurable subscription catalog backed by database tables, with appsettings used only as seed data for missing records. This catalog is the source of truth for:

- subscription identity and grouping
- display name
- base price and currency
- perk level
- minimum account level
- payment-method policy
- gift policy
- provider mappings for Afdian and Paddle

Downstream services no longer need to infer perk level from subscription identifiers.

## What Changed

### 1. Subscription definitions moved into Wallet catalog

New DB-backed models were added for subscription definitions and catalog settings.

Key behavior:

- Wallet seeds catalog rows from `Payment:SubscriptionCatalog` on startup if they do not already exist in DB.
- Existing DB rows are not overwritten by config.
- Runtime reads always use DB records, not appsettings directly.

Current seed config lives in:

- [DysonNetwork.Wallet/appsettings.json](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Wallet/appsettings.json)

Schema was added through:

- [20260314112018_AddSubscriptionCatalog.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Wallet/Migrations/20260314112018_AddSubscriptionCatalog.cs)

### 2. Subscription records now store catalog-derived metadata

`SnWalletSubscription` and `SnSubscriptionReferenceObject` now carry:

- `GroupIdentifier`
- `DisplayName`
- `PerkLevel`

This allows Wallet to return explicit perk metadata to other services without requiring identifier-to-level mapping logic elsewhere.

### 3. Hard-coded tier logic was removed from Wallet flow

Subscription creation, gift purchase, gift redemption, restore flows, and provider webhook handling now resolve definitions through the catalog service.

Important behavior changes:

- same subscription purchased again: extend the tail subscription
- different subscription in the same group: queue it to begin when the active group tail ends
- gift rules come from catalog policy instead of controller constants
- provider mappings are catalog-driven instead of separate hard-coded Afdian/Paddle maps

### 4. Renewal behavior is policy-driven

`SubscriptionRenewalJob` now checks catalog policy instead of assuming renewal behavior from payment method alone.

Current behavior:

- expired subscriptions are marked expired
- queued subscriptions are left to become effective by schedule
- only subscriptions that allow internal wallet renewal are auto-renewed by Wallet
- external providers like Paddle and Afdian are expected to extend subscriptions through webhook events

## Catalog Model

Each subscription definition can include:

- `Identifier`
- `GroupIdentifier`
- `DisplayName`
- `Currency`
- `BasePrice`
- `PerkLevel`
- `MinimumAccountLevel`
- `ExperienceMultiplier`
- `GoldenPointReward`
- `PaymentPolicy`
- `GiftPolicy`
- `ProviderMappings`

### Payment policy

Payment policy supports:

- `AllowInternalWallet`
- `AllowExternal`
- `AllowInternalWalletRenewal`
- explicit `AllowedMethods`

This allows a subscription to:

- block in-app wallet purchases
- block real-currency purchases
- restrict purchases to specific providers
- disable wallet-based auto renewal

### Gift policy

Gift policy supports:

- purchase enabled/disabled
- minimum account level
- perk-based bypass
- rolling purchase cap
- rolling time window
- gift expiration duration
- redeemed subscription duration

Catalog-level default gift policy is stored separately and merged with per-subscription overrides.

## Provider Changes

### Afdian

Afdian still behaves as a provider-side catalog. Wallet resolves Afdian IDs through the catalog `ProviderMappings`.

### Paddle

Paddle is now handled in two parts:

1. Checkout creation
2. Webhook-driven subscription application

New authenticated endpoint:

- `POST /api/subscriptions/{identifier}/checkout/paddle`

This endpoint:

- validates the subscription against the Wallet catalog
- resolves the configured Paddle price reference
- creates a Paddle transaction
- returns a `checkoutUrl` and `transactionId`

The webhook endpoint remains responsible for applying the actual subscription after payment completes:

- `POST /api/subscriptions/order/handle/paddle`

Important Paddle note:

- Checkout generation needs a Paddle `price_id`
- If a product has monthly and yearly prices, the caller should either provide the desired mapped reference or the catalog should distinguish them explicitly

## Perk Metadata Changes

Wallet subscription responses now carry explicit perk metadata.

Downstream consumers were updated to use Wallet-provided `PerkLevel` instead of calling `PerkSubscriptionPrivilege.GetPrivilegeFromIdentifier(...)`.

Updated areas include:

- Padlock account population
- Padlock auth
- Passport experience multiplier logic
- Drive quota and upload privilege checks
- Zone publication quota
- Insight perk gating

## API Changes

### New Paddle checkout endpoint

`POST /api/subscriptions/{identifier}/checkout/paddle`

Optional body:

```json
{
  "providerReferenceId": "pri_xxx"
}
```

Response:

```json
{
  "transactionId": "txn_xxx",
  "checkoutUrl": "https://checkout.paddle.com/...",
  "providerReferenceId": "pri_xxx"
}
```

### Existing endpoints now use catalog policy

These endpoints now validate against catalog configuration:

- subscription creation
- gift purchase
- gift redemption
- Afdian restore
- Paddle restore
- Afdian webhook handling
- Paddle webhook handling

## Migration Notes

### Config

Old provider-specific subscription maps in Wallet config were replaced by `Payment:SubscriptionCatalog`.

You should maintain:

- seed definitions in config for new environments
- DB rows for runtime truth

### Database

Run Wallet migrations so the new catalog tables and subscription columns exist.

### Proto / generated code

Wallet subscription responses now include extra metadata fields used by downstream services.

Important caveat:

- this workspace did not contain the original `wallet.proto` source file
- only generated `DysonNetwork.Shared/Proto/Wallet.cs` was present

Because of that, the contract update was applied directly to the checked-in generated Wallet proto C# so the implementation could compile and run. The recommended cleanup is:

1. restore or add the real `wallet.proto` source file
2. move these field additions into proto source
3. regenerate `Wallet.cs` and `WalletGrpc.cs`

## Files to Look At

Main implementation:

- [DysonNetwork.Wallet/Payment/SubscriptionCatalogService.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Wallet/Payment/SubscriptionCatalogService.cs)
- [DysonNetwork.Wallet/Payment/SubscriptionService.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Wallet/Payment/SubscriptionService.cs)
- [DysonNetwork.Wallet/Payment/SubscriptionRenewalJob.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Wallet/Payment/SubscriptionRenewalJob.cs)
- [DysonNetwork.Wallet/Payment/PaymentHandlers/PaddlePaymentHandler.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Wallet/Payment/PaymentHandlers/PaddlePaymentHandler.cs)
- [DysonNetwork.Shared/Models/SubscriptionCatalog.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Shared/Models/SubscriptionCatalog.cs)
- [DysonNetwork.Shared/Models/Subscription.cs](/Users/littlesheep/Documents/Projects/DysonNetwork/DysonNetwork.Shared/Models/Subscription.cs)

## Validation Performed

The following project builds were run successfully after the overhaul:

- `DysonNetwork.Wallet`
- `DysonNetwork.Padlock`
- `DysonNetwork.Passport`
- `DysonNetwork.Drive`
- `DysonNetwork.Zone`
- `DysonNetwork.Insight`

# Merchant API

Merchant routes live in `DysonNetwork.Wallet/Payment/MerchantController.cs`.

## Routing

- local/dev: `/api/merchants/...`
- production: `/wallet/merchants/...`

`{merchant}` accepts:
- merchant GUID
- publisher name like `solsynth`
- fallback `SnMerchant.Name`

Auth allows:
- publisher owner
- publisher manager
- linked wallet owner fallback

## Endpoints

### List settlements

`GET /wallet/merchants/{merchant}/settlements?offset=0&take=20`

Lists settlement records for the merchant, newest first.

### List orders

`GET /wallet/merchants/{merchant}/orders?status=Paid&offset=0&take=20`

Lists app orders for the merchant by the merchant's linked payout wallet, newest first.

Notes:
- only app orders are included
- filtered by `order.payee_wallet_id == merchant.payment_wallet_id`
- response includes line items
- `X-Total` header is set

### Pending settlements summary

`GET /wallet/merchants/{merchant}/settlements/pending`

Returns pending settlement totals grouped by currency.

### Manual settle

`POST /wallet/merchants/{merchant}/settlements/settle`

Settles all pending merchant funds.

### Overview stats

`GET /wallet/merchants/{merchant}/stats/overview`

Returns:
- `total_pending`
- `total_settled`
- `total_all_time`
- currency breakdowns for pending, settled, this month

### Incoming stats

`GET /wallet/merchants/{merchant}/stats/incoming?from=2026-01-01&to=2026-01-31&currency=points`

Returns incoming settlements within a date range, grouped by status and currency.

### Daily stats

`GET /wallet/merchants/{merchant}/stats/daily?from=2026-01-01&to=2026-01-31&currency=points`

Returns daily incoming totals for charting.

## Data model notes

A merchant is linked to a wallet via `SnMerchant.PaymentWalletId`.

App order flow:
- app order stores `payee_wallet_id`
- on payment, app orders are held in escrow
- wallet resolves merchant by app developer publisher
- settlement record is created for the merchant

Develop app create/update now also syncs the merchant record from the app developer publisher and app `payment_wallet_id`.

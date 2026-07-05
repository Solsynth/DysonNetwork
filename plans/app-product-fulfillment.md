# Plan: app product address collection + stock state

## Context
- Extend existing app products rather than custom apps themselves.
- Products may optionally collect a buyer address during ordering.
- Address data should reuse `SnAccountContact` / `AccountContactType.Address` instead of inventing another address model.
- Stock / selling behavior is needed per product, including limits such as per day, week, month, year, or manual restock.
- Stock is consumed on order creation and must be released again if the order later expires.
- Per-order fulfillment data should live in `SnWalletOrder.Meta` instead of changing the wallet order table schema.
- Apps also need explicit user authorization to read contacts / send notifications, with scopes aligned to the existing permission-node naming.
- For physical-style products, address collection can be required for purchase even when merchant sharing is not implicitly granted by the product itself; the client will drive a separate app authorization flow for broader contact access.

## Approach
- Keep the change centered on the existing product flow: `SnAppProduct` + `AppProductController` + wallet order creation.
- Add product-level configuration for:
  - whether address collection is required,
  - what extra app scopes are needed, using the same names as permission nodes (`contacts.read`, `notifications.send`, etc.).
- Store buyer-selected fulfillment data on each order in `SnWalletOrder.Meta`, including both a contact id reference and a copied address snapshot.
- Add a dedicated `ProductState` model for mutable stock/selling data so catalog definition and operational state stay separate.
- Reuse Padlock authorized-app records for user consent, adding an account endpoint to grant app-specific extra scopes manually.
- Keep app/product management CRUD developer-token only.
- Add app-authorized contact-read access separately, and keep notification sending on the existing app-authorized path.

## Files to modify
- `DysonNetwork.Shared/Models/AppProduct.cs`
- `DysonNetwork.Develop/Identity/AppProductController.cs`
- `DysonNetwork.Develop/Identity/AppProductService.cs`
- `DysonNetwork.Develop/Identity/CustomAppController.cs`
- likely a Padlock app-contact-read endpoint/controller
- `DysonNetwork.Develop/AppDatabase.cs`
- `DysonNetwork.Wallet/Payment/OrderController.cs`
- `DysonNetwork.Wallet/Payment/PaymentService.cs`
- order expiration / cleanup flow in wallet (for stock release)
- `DysonNetwork.Padlock/Account/AccountSecurityController.cs`
- `DysonNetwork.Padlock/Auth/AuthService.cs`
- likely a Padlock endpoint/controller for app-scoped contacts access
- `DysonNetwork.Shared/Models/Payment.cs`
- `DysonNetwork.Shared/Models/CustomApp.cs` (allowed scopes defaults / exposure if needed)
- `DysonNetwork.Develop/Migrations/*` (new migration likely needed)
- likely gRPC/proto surfaces that expose app products / contact access / auth decisions

## Reuse
- Product CRUD already exists in `DysonNetwork.Develop/Identity/AppProductController.cs` and `AppProductService.cs`.
- Product catalog model already exists in `DysonNetwork.Shared/Models/AppProduct.cs`.
- Address shape already exists as `SnAccountContact` with `AccountContactType.Address` in `DysonNetwork.Shared/Models/Account.cs`.
- Order metadata already exists as `SnWalletOrder.Meta` in `DysonNetwork.Shared/Models/Payment.cs`.
- Order creation already forwards arbitrary `Meta` in `DysonNetwork.Wallet/Payment/OrderController.cs` and `PaymentService.cs`.
- Authorized app consent already exists via `SnAuthorizedApp` and `AuthService.UpsertAuthorizedAppAsync(...)` in Padlock.
- Existing app-scoped notification permission pattern already exists in `DysonNetwork.Padlock/Auth/CustomAppNotificationController.cs` using `notifications.send` scope.
- Existing permission/filter plumbing already understands scope names, so new app scopes should reuse permission-node naming rather than inventing a parallel format.
- Account contact listing already exists in `DysonNetwork.Padlock/Account/AccountSecurityController.cs` and gRPC `AccountServiceGrpc.ListContacts(...)`.
- Custom app OAuth allowed scopes already exist on `SnCustomAppOauthConfig.AllowedScopes`.

## Steps
- [ ] Inspect existing product proto / gRPC surfaces so product config/state can be exposed without duplicating models.
- [ ] Add minimal product catalog fields for address collection requirements; do not over-model sharing on the product if broader merchant visibility is already driven by app scope authorization.
- [ ] Define `ProductState` for mutable selling/stock behavior (enabled/selling flag, stock mode, remaining stock, last restock data as needed).
- [ ] Wire product create/update/read endpoints to accept and return the new config/state.
- [ ] During order creation, validate selling status / stock availability, decrement stock immediately, and write fulfillment metadata with both contact id + address snapshot into `order.Meta`.
- [ ] Hook order expiration flow to release previously reserved stock.
- [ ] Add a Padlock account endpoint to let a user manually authorize extra app scopes such as contact reading.
- [ ] Add an app-authorized contact-read endpoint that checks `contacts.read` against `SnAuthorizedApp`.
- [ ] Reuse `SnAuthorizedApp` scope storage so merchant-side APIs can enforce `contacts.read` / `notifications.send` consistently.
- [ ] Keep product/custom-app management routes developer-authenticated only.
- [ ] Limit app-authorized routes to purpose-built endpoints like notifications and contact reading.
- [ ] Add persistence changes and migration for product state.
- [ ] Verify product CRUD, consent flow, contacts access, order creation, and stock release on expiration.

## Verification
- Create/update a product with address collection enabled and stock policy configured.
- Read product through private/public endpoints and confirm config/state is returned.
- Create an order for that product and confirm `Meta` contains both address contact id and copied address snapshot.
- Confirm stock is decremented on order creation.
- Expire an unpaid order and confirm stock is released back.
- Grant and revoke an app's extra contact-read authorization from account security endpoints.
- Verify merchant/app contact access is denied without `contacts.read` and allowed with it.
- Verify required-address products still require an address to create an order even before broader contact sharing exists.
- Exercise stock-limited and unavailable product cases.
- Run relevant develop/wallet tests if present; otherwise do focused API/manual checks.

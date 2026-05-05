# Apple Wallet Member Pass

This document describes the current Apple Wallet member pass implementation in `DysonNetwork.Passport`, including server-side setup and the client/API flows.

## Overview

Passport now supports two related capabilities:

1. Downloading a signed Apple Wallet member card as a `.pkpass` file
2. Serving the Apple PassKit web service endpoints used by Wallet to register devices and fetch updated passes

The implementation lives in `DysonNetwork.Passport` because the pass is based on account identity and profile data rather than payment or ticketing data.

## Server Components

The server-side implementation is split across these files:

1. `DysonNetwork.Passport/Account/ApplePassService.cs`
2. `DysonNetwork.Passport/Account/PassKitController.cs`
3. `DysonNetwork.Passport/Account/AppleWalletOptions.cs`
4. `DysonNetwork.Passport/Account/SnApplePass.cs`
5. `DysonNetwork.Passport/Account/SnApplePassRegistration.cs`
6. `DysonNetwork.Passport/Account/AccountCurrentController.cs`
7. `DysonNetwork.Passport/AppDatabase.cs`
8. `DysonNetwork.Passport/Startup/ServiceCollectionExtensions.cs`

## Data Model

Two Passport tables are used for Apple Wallet support.

### `apple_passes`

Stores one pass record per account and pass type.

Fields:

1. `account_id`
2. `pass_type_identifier`
3. `serial_number`
4. `authentication_token`
5. `last_updated_tag`
6. standard audit fields from `ModelBase`

Current behavior:

1. One member pass is created per account
2. `serial_number` is the account GUID
3. `authentication_token` is a random long-lived token used by Apple Wallet callback requests
4. `last_updated_tag` is a SHA-256 hash derived from account/profile/subscription state

### `apple_pass_registrations`

Stores Wallet device registrations for each pass.

Fields:

1. `pass_id`
2. `device_library_identifier`
3. `push_token`
4. standard audit fields from `ModelBase`

This supports:

1. device registration
2. device unregistration
3. tracking which devices should be notified when APNs push support is added

## Configuration

Apple Wallet settings are configured under `AppleWallet` in `DysonNetwork.Passport/appsettings.json`.

Current config keys:

```json
"AppleWallet": {
  "PassTypeIdentifier": "pass.solian.app.member",
  "TeamIdentifier": "W7HPZ53V6B",
  "OrganizationName": "Solsynth",
  "Description": "Solar Network Account",
  "LogoText": "Solarpass",
  "ForegroundColor": "rgb(255, 255, 255)",
  "BackgroundColor": "rgb(91, 93, 148)",
  "LabelColor": "rgb(224, 242, 254)",
  "TermsText": "Solsynth 2026 © All rights reserved.",
  "BarcodeAltText": "MEMBER CODE",
  "WebServiceUrl": "https://passport.solian.app/passkit/v1",
  "SigningCertificatePath": "./Keys/AppleWallet/member-pass.p12",
  "SigningCertificatePassword": "",
  "AppleWwdrCertificatePath": "./Keys/AppleWallet/AppleWWDRCAG4.cer",
  "IconPath": "./Resources/Passes/icon.png",
  "Icon2XPath": "./Resources/Passes/icon@2x.png",
  "Icon3XPath": "./Resources/Passes/icon@3x.png",
  "LogoPath": "./Resources/Passes/logo.png",
  "Logo2XPath": "./Resources/Passes/logo@2x.png",
  "Logo3XPath": "./Resources/Passes/logo@3x.png",
  "ThumbnailPath": "./Resources/Passes/thumbnail.png",
  "Thumbnail2XPath": "./Resources/Passes/thumbnail@2x.png",
  "Thumbnail3XPath": "./Resources/Passes/thumbnail@3x.png",
  "SiteUrl": "http://localhost:3000"
}
```

### Required Values

These values must be valid for pass generation to succeed:

1. `PassTypeIdentifier`
2. `TeamIdentifier`
3. `OrganizationName`
4. `Description`
5. `WebServiceUrl`
6. `SigningCertificatePath`
7. `AppleWwdrCertificatePath`
8. `IconPath`
9. `Icon2XPath`

### Important Routing Note

The PassKit controller is routed under:

```text
/api/passkit/v1
```

For real devices, `AppleWallet:WebServiceUrl` should point to the public HTTPS base for that route.

Recommended value:

```text
https://passport.solian.app/api/passkit/v1
```

If the URL does not match the public route Wallet can reach, pass update registration and refresh will fail.

## Certificates

Two certificates are required:

1. The Apple Wallet pass signing certificate with private key
2. The Apple WWDR certificate that matches the signing certificate generation era

Current implementation expectations:

1. The signing certificate is loaded from `SigningCertificatePath`
2. The WWDR certificate is loaded from `AppleWwdrCertificatePath`
3. The signing certificate should be a `.p12` or other file format that contains the private key

Current placeholder paths:

1. `./Keys/AppleWallet/member-pass.p12`
2. `./Keys/AppleWallet/AppleWWDRCAG4.cer`

If either file is missing, pass generation throws an `InvalidOperationException`.

## Pass Assets

The pass generator loads images from the Passport content root.

Configured assets:

1. `icon.png`
2. `icon@2x.png`
3. `icon@3x.png`
4. `logo.png`
5. `logo@2x.png`
6. `logo@3x.png`
7. `thumbnail.png`
8. `thumbnail@2x.png`
9. `thumbnail@3x.png`

Current implementation requires:

1. `IconPath`
2. `Icon2XPath`

Other image variants are optional in the current code path.

If a required asset path is configured but the file does not exist, pass generation fails.

### Profile Picture Preference

The current implementation now prefers the account profile picture for these Wallet images when available:

1. `icon`
2. `icon@2x`
3. `icon@3x`
4. `thumbnail`
5. `thumbnail@2x`
6. `thumbnail@3x`

Image loading behavior:

1. If `account.Profile.Picture.Url` is present, Passport downloads that image and uses it for the icon and thumbnail variants.
2. If `Picture.Url` is empty but `Picture.Id` exists and `FileUrl` is configured, Passport builds the public file URL as `{FileUrl}/{picture.Id}` and downloads it.
3. If the profile picture cannot be fetched, Passport falls back to the configured static asset paths.

This means:

1. user profile pictures can appear directly on the Wallet pass
2. the configured static icon files still act as the fallback for reliability

To make profile-picture-backed icons work reliably in production, ensure `FileUrl` is configured to a public URL that Passport can reach from the server side.

## Pass Content Mapping

The current member pass is generated from account and profile data.

### Top-Level Pass Fields

1. `passTypeIdentifier` from config
2. `teamIdentifier` from config
3. `serialNumber` from `SnApplePass.SerialNumber`
4. `description` from config
5. `organizationName` from config
6. `logoText` from config
7. `foregroundColor` from config
8. `backgroundColor` from config
9. `labelColor` from config
10. `authenticationToken` from `SnApplePass.AuthenticationToken`
11. `webServiceUrl` from config

### Generic Style Fields

The implementation uses `PassStyle.Generic`.

Visible field mapping:

1. Header field
   `USERNAME` => `@{account.Name}`
2. Primary field
   `NAME` => full profile name if available, otherwise `account.Nick`, otherwise `account.Name`
3. Secondary field
   `MEMBER SINCE` => account creation year, or profile creation year as fallback
4. Auxiliary field
   `STELLAR PROGRAM` => subscription display name, or perk level, or identifier, or `Standard`
5. Back field
   `Terms` => `AppleWallet:TermsText`
6. Back field
   `Profile` => `{SiteUrl}/accounts/{account.Name}`
7. Back field
   `Account ID` => account GUID

### Barcode

The member pass currently includes a QR code that encodes the profile URL:

```text
{SiteUrl}/accounts/{account.Name}
```

Encoding:

1. format: `PKBarcodeFormatQR`
2. message encoding: `iso-8859-1`
3. alt text: `AppleWallet:BarcodeAltText`

### User Info

The pass includes:

```json
{
  "id": "<account-guid>"
}
```

in `userInfo`.

## Server Setup Checklist

### 1. Apply the Migration

Apple Wallet support requires the `AddAppleWalletPasses` migration.

If you are running Passport normally, startup migrations will apply automatically through the existing Passport boot sequence.

For manual application:

```bash
dotnet ef database update --project DysonNetwork.Passport
```

### 2. Install Certificates

Place the real certificate files at the configured paths, or override the config in the deployment environment.

At minimum:

1. signing certificate with private key
2. matching WWDR certificate

### 3. Add Pass Assets

Place the Wallet image assets under `DysonNetwork.Passport/Resources/Passes/` or update the config to point at your actual asset location.

### 4. Set the Public Web Service URL

Set `AppleWallet:WebServiceUrl` to the public HTTPS URL that iPhone Wallet can reach.

Recommended format:

```text
https://<public-passport-host>/api/passkit/v1
```

### 5. Verify the Site URL

`AppleWallet:SiteUrl` is used for the profile page URL embedded in the pass and barcode.

Set it to your real front-end origin, for example:

```text
https://solian.app
```

### 6. Ensure TLS Is Publicly Reachable

Apple Wallet update callbacks require HTTPS on real devices.

## User-Facing Download API

### Endpoint

```http
GET /api/accounts/me/passbook/member
Authorization: Bearer <access-token>
```

### Behavior

1. Requires normal Passport user authentication
2. Loads the current account and profile
3. Hydrates perk subscription data from Wallet service if available
4. Creates or reuses a persistent `apple_passes` record
5. Builds and signs a `.pkpass`
6. Returns `application/vnd.apple.pkpass`

### Response

Successful response:

1. HTTP `200 OK`
2. content type `application/vnd.apple.pkpass`
3. file name `solian-member.pkpass`

### Typical Browser/App Client Flow

1. Call `GET /api/accounts/me/passbook/member` with the user access token
2. Treat the response as a binary file
3. On iOS Safari or in-app browser, open/share the `.pkpass` so Wallet can import it

## PassKit Update Service API

These APIs are called by Apple Wallet after the user adds the pass.

Authentication for these endpoints is not user bearer auth. Wallet sends:

```http
Authorization: ApplePass <authentication-token>
```

The token is generated and stored in `apple_passes.authentication_token`.

### 1. Register a Device

```http
POST /api/passkit/v1/devices/{deviceLibraryIdentifier}/registrations/{passTypeIdentifier}/{serialNumber}
Authorization: ApplePass <authentication-token>
Content-Type: application/json

{
  "push_token": "<wallet-push-token>"
}
```

Current behavior:

1. Validates the pass type and serial number
2. Validates the Apple pass auth token
3. Creates or updates `apple_pass_registrations`
4. Returns `201 Created`

### 2. Unregister a Device

```http
DELETE /api/passkit/v1/devices/{deviceLibraryIdentifier}/registrations/{passTypeIdentifier}/{serialNumber}
Authorization: ApplePass <authentication-token>
```

Current behavior:

1. Validates the pass and Apple pass auth token
2. Deletes the matching registration if present
3. Returns `200 OK`

### 3. Query Updated Serial Numbers

```http
GET /api/passkit/v1/devices/{deviceLibraryIdentifier}/registrations/{passTypeIdentifier}?passesUpdatedSince=<tag>
Authorization: ApplePass <authentication-token>
```

Current response body:

```json
{
  "serial_numbers": ["<serial-number>"],
  "last_updated": "<hash-tag>"
}
```

Current behavior:

1. Finds passes registered to the device and pass type
2. Filters by `last_updated_tag` if `passesUpdatedSince` is provided
3. Returns `204 No Content` if there are no newer serials
4. Returns `200 OK` with matching serials and the newest tag otherwise

Note:

1. The current implementation authorizes this request by validating the Apple pass auth token against the first returned pass.
2. That is acceptable for the current single member pass use case, but should be tightened if multiple pass types are added later.

### 4. Download the Latest Pass

```http
GET /api/passkit/v1/passes/{passTypeIdentifier}/{serialNumber}
Authorization: ApplePass <authentication-token>
```

Current behavior:

1. Validates the pass and auth token
2. Regenerates the latest member pass from current account/profile/subscription state
3. Returns `application/vnd.apple.pkpass`
4. Sets `ETag` to the current `last_updated_tag`

### 5. Receive Apple Wallet Logs

```http
POST /api/passkit/v1/log
Content-Type: application/json

{
  "logs": [
    "example log line"
  ]
}
```

Current behavior:

1. Writes each log line through Passport logging
2. Returns `200 OK`

## Client Integration Notes

### First-Party Web or Mobile Client

Your own client only needs the download endpoint.

Recommended flow:

1. Authenticate the user normally with Passport
2. Call `GET /api/accounts/me/passbook/member`
3. Save or open the `.pkpass` response
4. Let iOS Wallet take over installation

The PassKit update APIs are not meant for your normal app client. They are meant for Apple Wallet itself.

### Apple Wallet Flow After Install

Once the user adds the pass to Wallet, Apple Wallet will:

1. store the `authenticationToken` and `webServiceUrl` embedded in the pass
2. call the registration endpoint with the device library identifier and push token
3. later ask for updated serials
4. fetch the latest pass package when Wallet sees the pass has changed

## Update Trigger Logic

Current pass freshness is derived from a hash over these fields:

1. `account.UpdatedAt`
2. `account.Profile.UpdatedAt`
3. `account.PerkSubscription.Identifier`
4. `account.PerkSubscription.PerkLevel`
5. `account.Name`
6. `account.Nick`

This means changes to those values will change `last_updated_tag` the next time the pass is generated.

## Current Limitations

### APNs Push Notifications Are Not Implemented Yet

The current implementation stores `push_token`, but it does not yet send APNs push notifications to prompt Wallet to refresh automatically.

Current state:

1. registration storage exists
2. update service endpoints exist
3. APNs push sender does not exist yet

As a result, the server side is ready for the Wallet update protocol, but automatic refresh notifications still need a later implementation.

### Placeholder Paths in `appsettings.json`

The committed config uses placeholder paths and values for:

1. certificates
2. pass assets
3. public URL settings

These must be replaced in the real deployment environment.

## Troubleshooting

### Pass Generation Fails Immediately

Check:

1. signing certificate file exists
2. WWDR certificate file exists
3. `IconPath` and `Icon2XPath` files exist
4. `PassTypeIdentifier` and `TeamIdentifier` match your Apple setup

### Pass Adds but Update Service Never Gets Called

Check:

1. `AppleWallet:WebServiceUrl` points to `/api/passkit/v1`
2. the URL is reachable from the internet
3. the URL uses HTTPS

### Wallet Cannot Install the Pass

Common causes:

1. wrong WWDR certificate
2. signing certificate missing private key
3. invalid pass assets
4. invalid pass payload fields

### Update Registration Works but Pass Does Not Auto-Refresh

That is expected until APNs push support is added.

## Example User Download Request

```bash
curl \
  -H "Authorization: Bearer <token>" \
  -o solian-member.pkpass \
  "https://passport.solian.app/api/accounts/me/passbook/member"
```

## Example PassKit Registration Request

```bash
curl -X POST \
  -H "Authorization: ApplePass <token>" \
  -H "Content-Type: application/json" \
  -d '{"push_token":"example-device-token"}' \
  "https://passport.solian.app/api/passkit/v1/devices/device-123/registrations/pass.solian.app.member/00000000-0000-0000-0000-000000000001"
```

## Future Work

Recommended next improvements:

1. add APNs push delivery using stored `push_token`
2. improve authorization for the updated-serials endpoint if more pass types are added
3. expose admin tooling to inspect pass registrations
4. optionally add more visible member metadata, such as badge or level, to the pass

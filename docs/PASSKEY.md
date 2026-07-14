# Passkeys

Padlock supports WebAuthn passkeys as a high-trust authentication method. An account has one `Passkey` auth factor that controls whether passkey login is enabled, and zero or more separately stored passkey credentials.

All API JSON uses `snake_case`. In production, Padlock routes are prefixed with `/padlock`; for example, `/api/auth/passkey/start` is exposed as `/padlock/auth/passkey/start`.

Configure `WebAuthn:RpId` with the public web application's registrable domain, not the API host. Production uses `solian.app`, which is valid for both `solian.app` and `api.solian.app`; local development uses `localhost`. Configure `WebAuthn:RelatedOrigins` with the exact public web origins permitted to use that RP ID, such as `https://solian.app`.

## Related-origin discovery

Padlock serves `GET /.well-known/webauthn` directly, without the normal `/api` or `/padlock` route transformation. It returns:

```json
{
  "origins": ["https://solian.app"]
}
```

The production gateway must route the exact public RP-ID path, `https://solian.app/.well-known/webauthn`, to Padlock without rewriting it. The response must remain a `200` with `Content-Type: application/json`; browsers fetch it without credentials when validating a related origin.

## Model

### Passkey auth factor

`SnAccountAuthFactor` with `type: Passkey` (`7`) is the account-level enable flag.

| Property | Value |
|---|---|
| Trust level | `4` |
| Secret | None |
| Enables | All passkey credentials owned by the account |
| Disable | Prevents every passkey credential from being used |
| Delete | Removes the factor and every credential |

Create it through the normal factor endpoint after enabling a recovery code:

```http
POST /api/factors
Content-Type: application/json

{ "type": 7, "secret": null }
```

The factor is enabled when created. It can later be disabled or re-enabled with the standard factor endpoints.

### Passkey credential

`SnAccountPasskey` is Padlock-local (`DysonNetwork.Padlock/Models/AccountPasskey.cs`) and is intentionally not part of `DysonNetwork.Shared`.

| Field | Returned to client | Description |
|---|---:|---|
| `id` | Yes | Credential record ID |
| `label` | Yes | User-editable name, such as `MacBook Touch ID` |
| `account_id` | Yes | Owning account |
| `credential_id` | No | WebAuthn credential ID used to find the public key |
| `credential` | No | Serialized public key material and registration counter |
| `created_at`, `updated_at` | Yes | Audit timestamps |

`credential_id` has a unique partial index for active records. A deleted credential may be registered again.

## Managing credentials

These endpoints require an authenticated interactive session. The passkey factor must be enabled to register a credential.

### Start registration

```http
POST /api/factors/passkey/start
Content-Type: application/json
```

```json
{
  "device_id": "device-identifier",
  "device_name": "MacBook Pro",
  "rp_id": "example.com",
  "rp_name": "Solar Network"
}
```

The response contains the WebAuthn creation options, including `challenge`, `rp_id`, `user_id`, `user_name`, `display_name`, `pub_key_cred_params`, and `authenticator_selection`.

### Complete registration

```http
POST /api/factors/passkey/complete
Content-Type: application/json
```

```json
{
  "device_id": "device-identifier",
  "label": "MacBook Touch ID",
  "client_data_json": "base64url-or-JSON-client-data",
  "attestation_object": "base64url-attestation-object"
}
```

The response is the new credential record. Registering another credential repeats these two calls; it does not create another auth factor.

### List, rename, and remove credentials

```http
GET    /api/factors/passkey
PATCH  /api/factors/passkey/{passkey_id}
DELETE /api/factors/passkey/{passkey_id}
```

Rename request:

```json
{ "label": "Phone passkey" }
```

Removing one credential does not disable the passkey factor or affect the account's other credentials.

## Login flows

Both flows require the account's passkey factor to be enabled and use the credential selected by its WebAuthn `credential_id`.

### Account-known challenge login

Use this after the caller has already created a normal auth challenge for a username.

```http
POST /api/auth/challenge/{challenge_id}/passkey/start
POST /api/auth/challenge/{challenge_id}/passkey/complete
```

The start response contains `allow_credentials` for every active credential on the account. The completion request has no `factor_id`:

```json
{
  "credential_id": "base64url-credential-id",
  "client_data_json": "base64url-or-JSON-client-data",
  "authenticator_data": "base64url-authenticator-data",
  "signature": "base64url-signature",
  "user_handle": null
}
```

The passkey factor is marked as used for that challenge, so it cannot satisfy multiple steps of the same challenge.

### Discoverable login without a username

For a resident/discoverable passkey, create a login challenge without an account name:

```http
POST /api/auth/passkey/start
Content-Type: application/json
```

```json
{
  "device_id": "device-identifier",
  "device_name": "Chrome on macOS",
  "platform": 1,
  "audiences": [],
  "scopes": []
}
```

The response includes `auth_challenge_id`, `challenge`, `rp_id`, `timeout`, and an empty `allow_credentials` list. Pass an empty allow-credentials list to WebAuthn so the authenticator can choose a discoverable credential.

Complete it with the assertion:

```http
POST /api/auth/passkey/{auth_challenge_id}/complete
Content-Type: application/json
```

```json
{
  "credential_id": "base64url-credential-id",
  "client_data_json": "base64url-or-JSON-client-data",
  "authenticator_data": "base64url-authenticator-data",
  "signature": "base64url-signature",
  "user_handle": null
}
```

Padlock resolves the credential ID to its account, verifies the assertion, completes the auth challenge, and returns it. Exchange the challenge ID through the normal authorization-code token flow. Discoverable challenges expire after five minutes.

## Verification

Padlock verifies:

1. The assertion credential ID matches the selected credential record.
2. The authenticator data has the User Present flag.
3. The ECDSA P-256 signature validates against the stored public key.
4. `client_data_json.type` is `webauthn.get` and its challenge matches the cached one-time assertion challenge.

Registration verifies `webauthn.create`, the registration challenge, and the supported attestation statement before persisting a credential.

## Migration

Migration `20260713140242_RefactorPasskeys` creates `account_passkeys`, copies legacy passkey-factor secrets into labeled credential rows, and retains one passkey factor per account.

The credential index is partial (`WHERE deleted_at IS NULL`). Its data-copy SQL must therefore use:

```sql
ON CONFLICT (credential_id) WHERE deleted_at IS NULL DO NOTHING;
```

Do not use `ON CONFLICT (credential_id) DO NOTHING`; PostgreSQL cannot infer the partial unique index and returns `42P10`.

## Implementation references

- `DysonNetwork.Padlock/Account/AccountSecurityController.cs`
- `DysonNetwork.Padlock/Account/AccountService.cs`
- `DysonNetwork.Padlock/Auth/AuthController.cs`
- `DysonNetwork.Padlock/Models/AccountPasskey.cs`
- `DysonNetwork.Padlock/Migrations/20260713140242_RefactorPasskeys.cs`
- Island client: `packages/solar_network_sdk` and `lib/auth/login_content.dart`

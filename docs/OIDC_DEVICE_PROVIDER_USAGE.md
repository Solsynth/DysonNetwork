# Using the Padlock OIDC Device Flow

This guide is for app developers who want to authenticate users against the Padlock OpenID Connect provider from a CLI app, TV app, terminal tool, or other limited-input device.

## Base URLs

- Discovery: `https://api.solsynth.dev/.well-known/openid-configuration`
- Device authorization endpoint: `https://api.solsynth.dev/padlock/auth/open/device/code`
- Token endpoint: `https://api.solsynth.dev/padlock/auth/open/token`
- User verification page: `https://solsynth.dev/auth/device`

Use the discovery document as the source of truth when possible.

## Supported Grant Type

```text
urn:ietf:params:oauth:grant-type:device_code
```

## Step 1: Request a Device Code

Send a form-encoded request:

```bash
curl -X POST https://api.solsynth.dev/padlock/auth/open/device/code \
  -H 'Content-Type: application/x-www-form-urlencoded' \
  -d 'client_id=my-cli-tool' \
  -d 'scope=openid profile email'
```

Example response:

```json
{
  "device_code": "GmRhmhcxhwAzkoEqiMEg_DnyEysNkuNhszIySk9eS",
  "user_code": "WDJB-MJHT",
  "verification_uri": "https://solsynth.dev/auth/device",
  "verification_uri_complete": "https://solsynth.dev/auth/device?code=WDJB-MJHT",
  "expires_in": 600,
  "interval": 5
}
```

What to do with this response:

- show `user_code` clearly on the device
- direct the user to `verification_uri`
- if your device can render or open a URL, prefer `verification_uri_complete`
- wait at least `interval` seconds between token polling attempts

## Step 2: Tell the User What To Do

Your app should show something like:

```text
Open https://solsynth.dev/auth/device
Enter code: WDJB-MJHT
```

If you use `verification_uri_complete`, the code can already be prefilled in the browser.

## Step 3: Poll the Token Endpoint

Poll the token endpoint until the user approves, denies, or the code expires.

```bash
curl -X POST https://api.solsynth.dev/padlock/auth/open/token \
  -H 'Content-Type: application/x-www-form-urlencoded' \
  -d 'grant_type=urn:ietf:params:oauth:grant-type:device_code' \
  -d 'device_code=GmRhmhcxhwAzkoEqiMEg_DnyEysNkuNhszIySk9eS' \
  -d 'client_id=my-cli-tool'
```

For confidential clients, also include:

```text
client_secret=...
```

Successful response:

```json
{
  "access_token": "eyJhbGciOiJSUzI1NiIs...",
  "id_token": "eyJhbGciOiJSUzI1NiIs...",
  "refresh_token": "eyJhbGciOiJSUzI1NiIs...",
  "expires_in": 300,
  "token_type": "Bearer",
  "scope": "openid profile email"
}
```

## Polling Errors You Should Handle

Pending approval:

```json
{
  "error": "authorization_pending",
  "error_description": "Authorization pending."
}
```

Polling too fast:

```json
{
  "error": "slow_down",
  "error_description": "Slow down."
}
```

Other expected device-flow errors:

- `access_denied`: the user declined the request
- `expired_token`: the device code expired
- `invalid_grant`: the code is invalid, already used, or does not belong to the client
- `invalid_client`: confidential client credentials are missing or invalid

Recommended polling behavior:

1. Wait for the returned `interval` before the first poll.
2. On `authorization_pending`, wait the same interval and try again.
3. On `slow_down`, add at least 5 seconds before the next attempt.
4. Stop polling on any terminal error.

## Optional: Build Your Own Verification UI

If you are building the browser-side verification experience yourself, these endpoints are available:

- `GET /padlock/auth/open/device/code/{user_code}` to fetch request details
- `POST /padlock/auth/open/device/code/{user_code}/approve` to approve
- `POST /padlock/auth/open/device/code/{user_code}/decline` to decline

Example status response:

```json
{
  "user_code": "WDJB-MJHT",
  "client_id": "my-cli-tool",
  "client_name": "My CLI Tool",
  "client_slug": "my-cli-tool",
  "scopes": ["openid", "profile", "email"],
  "status": "pending",
  "expires_at": "2026-07-06T12:15:00Z",
  "expires_in": 534,
  "interval": 5,
  "verification_uri": "https://solsynth.dev/auth/device"
}
```

Approval endpoints require a logged-in interactive browser session.

## Minimal JavaScript Example

```javascript
const device = await fetch("https://api.solsynth.dev/padlock/auth/open/device/code", {
  method: "POST",
  headers: { "Content-Type": "application/x-www-form-urlencoded" },
  body: new URLSearchParams({
    client_id: "my-cli-tool",
    scope: "openid profile email"
  })
}).then((res) => res.json());

console.log(`Visit ${device.verification_uri} and enter code ${device.user_code}`);

let intervalMs = device.interval * 1000;

while (true) {
  await new Promise((resolve) => setTimeout(resolve, intervalMs));

  const tokenResponse = await fetch("https://api.solsynth.dev/padlock/auth/open/token", {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({
      grant_type: "urn:ietf:params:oauth:grant-type:device_code",
      device_code: device.device_code,
      client_id: "my-cli-tool"
    })
  });

  const payload = await tokenResponse.json();

  if (payload.error === "authorization_pending") {
    continue;
  }

  if (payload.error === "slow_down") {
    intervalMs += 5000;
    continue;
  }

  if (payload.error) {
    throw new Error(`${payload.error}: ${payload.error_description ?? ""}`);
  }

  console.log("Authenticated", payload);
  break;
}
```

## Discovery Example

```bash
curl https://api.solsynth.dev/.well-known/openid-configuration
```

Look for:

- `issuer`
- `device_authorization_endpoint`
- `token_endpoint`
- `userinfo_endpoint`
- `jwks_uri`

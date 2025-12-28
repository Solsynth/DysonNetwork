# Client-Side Onboarding Flow for New Users

This document outlines the steps for a client application to handle the onboarding of new users who authenticate via a third-party provider.

## 1. Initiate the OIDC Login Flow

This step remains the same as a standard OIDC authorization code flow. The client application redirects the user to the `/authorize` endpoint of the authentication server with the required parameters (`response_type=code`, `client_id`, `redirect_uri`, `scope`, etc.).

## 2. Handle the Token Response

After the user authenticates with the third-party provider and is redirected back to the client, the client will have an `authorization_code`. The client then exchanges this code for tokens at the `/token` endpoint.

The response from the `/token` endpoint will differ for new and existing users.

### For Existing Users

If the user already has an account, the token response will be a standard OIDC token response, containing:
- `access_token`
- `id_token`
- `refresh_token`
- `expires_in`
- `token_type: "Bearer"`

The client should proceed with the standard login flow.

### For New Users

If the user is new, the token response will contain a special `onboarding_token`:
- `onboarding_token`: A JWT containing information about the new user from the external provider.
- `token_type: "Onboarding"`

The presence of the `onboarding_token` is the signal for the client to start the new user onboarding flow.

## 3. Process the Onboarding Token

The `onboarding_token` is a JWT. The client should decode it to access the claims, which will include:

- `provider`: The name of the external provider (e.g., "Google", "Facebook").
- `provider_user_id`: The user's unique ID from the external provider.
- `email`: The user's email address (if available).
- `name`: The user's full name from the external provider (if available).
- `nonce`: The nonce from the initial authorization request.

Using this information, the client can now guide the user through a custom onboarding process. For example, it can pre-fill a registration form with the user's name and email, and prompt the user to choose a unique username for their new account.

## 4. Complete the Onboarding

To finalize the account creation, the client needs to send the collected information to the server. This requires a new API endpoint on the server that is not part of this change.

**Example Endpoint:** `POST /api/account/onboard`

The client would send a request to this endpoint, including:
- The `onboarding_token`.
- The username chosen by the user.
- Any other required information.

The server will validate the `onboarding_token` and create a new user account with the provided details.

## 5. Finalize Login

Upon successful account creation, the server's onboarding endpoint should return a standard set of OIDC tokens (`access_token`, `id_token`, `refresh_token`) for the newly created user.

The client can then use these tokens to log the user in, completing the onboarding and login process.

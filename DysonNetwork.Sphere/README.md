# OpenID Connect Integration

This project includes a reusable OpenID Connect client implementation that can be used with multiple providers.

## Supported Providers

- Apple Sign In
- Google Sign In

## How to Add a New Provider

1. Create a new class that inherits from `OidcService` in the `Auth/OpenId` directory
2. Implement the abstract methods and properties
3. Register the service in `Program.cs`
4. Add the provider's configuration to `appsettings.json`
5. Add the provider to the `GetOidcService` method in both `OidcController` and `AuthCallbackController`

## Configuration

### Apple Sign In

```json
"Apple": {
  "ClientId": "YOUR_APPLE_CLIENT_ID", // Your Service ID from Apple Developer portal
  "TeamId": "YOUR_APPLE_TEAM_ID", // Your Team ID from Apple Developer portal
  "KeyId": "YOUR_APPLE_KEY_ID", // Key ID for the private key
  "PrivateKeyPath": "./apple_auth_key.p8", // Path to your .p8 private key file
  "RedirectUri": "https://your-app.com/auth/callback/apple" // Your callback URL
}
```

### Google Sign In

```json
"Google": {
  "ClientId": "YOUR_GOOGLE_CLIENT_ID", // Your OAuth client ID
  "ClientSecret": "YOUR_GOOGLE_CLIENT_SECRET", // Your OAuth client secret
  "RedirectUri": "https://your-app.com/auth/callback/google" // Your callback URL
}
```

## Usage

To initiate the OpenID Connect flow, redirect the user to:

```
/auth/login/{provider}?returnUrl=/your-return-path
```

Where `{provider}` is one of the supported providers (e.g., `apple`, `google`).

## Authentication Flow

1. User is redirected to the provider's authentication page
2. After successful authentication, the provider redirects back to your callback endpoint
3. The callback endpoint processes the response and creates or retrieves the user account
4. A session is created for the user and a token is issued
5. The user is redirected back to the specified return URL with the token set as a cookie

## Customization

The base `OidcService` class provides common functionality for all providers. You can override any of its methods in your provider-specific implementations to customize the behavior.

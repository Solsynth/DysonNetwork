# Web to Local Credential Sharing for Flutter Desktop Apps

This document outlines the essential features and concepts for implementing secure web to local credential sharing for Flutter desktop applications. The goal is to allow a web application to establish an authenticated session with a running desktop application, leveraging the desktop app's existing authentication and maintaining a secure, hierarchical session structure.

## Core Concepts from DysonNetwork.Pass Refactoring

When accessing the Pass service through the Gateway, replace the `/api` with `/pass`

The recent refactoring of the authentication system in `DysonNetwork.Pass` introduces key mechanisms that directly support this web-to-local credential sharing:

1.  **Parent/Sub-Sessions (`SnAuthSession.ParentSessionId`)**:
    *   The `SnAuthSession` model now includes a `ParentSessionId` field. This allows an authenticated session to explicitly declare that it was derived from another session.
    *   This is crucial for the web-to-local flow, as the web session can be established as a child of the desktop app's primary session.

2.  **Recursive Session Revocation**:
    *   The `AuthService.RevokeSessionAsync` method has been updated to recursively revoke all child sessions (and their children) when a parent session is logged out.
    *   This ensures that if a user logs out of their desktop application, all web sessions that were derived from that desktop session are also automatically invalidated, enhancing security and maintaining consistency.

3.  **Login from Existing Session API (`AuthController.LoginFromSession`)**:
    *   A new API endpoint `POST /api/auth/login/session` has been added to `AuthController`.
    *   This endpoint is designed to create a new `SnAuthSession` (and issue a corresponding authentication token/cookie) by leveraging an *existing* authenticated session.
    *   It takes device information (`DeviceId`, `DeviceName`, `Platform`, `ExpiredAt`) and the `ParentSessionId` is implicitly set to the `currentSession` available in the `HttpContext`.
    *   This endpoint is the server-side counterpart to the desktop app's `/exchange` endpoint, allowing the desktop app to request a new, child session for the web application.

## Integration into the Web-to-Local Flow

The `AuthController.LoginFromSession` API endpoint plays a central role in the web-to-local credential sharing mechanism. After the Flutter desktop app's local HTTP server receives and verifies a server-signed challenge from the web app (via its `/exchange` endpoint), the desktop app would then call this `LoginFromSession` API endpoint.

By making this call:
*   The desktop app, being already authenticated with the server, provides its active session context.
*   The `LoginFromSession` endpoint uses this context to create a *new* session for the web application.
*   This new web session is automatically linked to the desktop app's session via `ParentSessionId`.
*   A new web session token (e.g., a JWT) is issued for the web app.

This setup ensures that:
*   The web session is securely tied to the desktop session.
*   The web session benefits from the recursive revocation logic, meaning if the desktop app session is terminated, the web session is also automatically invalidated.

---

## ✅ Feature Checklist for the Flutter Desktop App

1.  **Localhost HTTP Server**

    Your Flutter desktop app must include:
    *   A lightweight HTTP server (`dart:io HttpServer`)
    *   Bind to `127.0.0.1` only (never `0.0.0.0`)
    *   Use a random port on startup (e.g., `40000–60000`)
    *   Store this port in memory

    Endpoints required:
    1.  `GET /alive`
        *   Used by the web app to detect that the desktop app is running
        *   Returns JSON: `{ "status": "ok", "challenge": "<randomChallenge>" }`
    2.  `POST /exchange`
        *   Web app sends the server-signed challenge back
        *   Desktop verifies and replies with a signed token/session
        *   **Crucially, this is where the desktop app would call the `POST /api/auth/login/session` endpoint on the backend, using its existing session to create a new sub-session for the web app.**
    3.  `POST /handshake/done` (optional)
        *   For cleanup, closing UI, etc.
    4.  `GET /handshake` (optional)
        *   For some client information and ensure it's Solian's app

---

2.  **Challenge/Response Security System**

    To avoid any malicious website calling your localhost server, implement:

    Desktop app responsibilities:
    *   Generate a random challenge string (length `32–64`)
    *   Include it in `/alive` response
    *   When receiving `/exchange`, verify:
        *   The challenge was signed by your backend
        *   The signature or token is valid
        *   Only then return a desktop session token

    Requirements:
    *   Challenge must be valid only once
    *   Challenge must expire in `≤ 30` seconds
    *   Challenge tied to desktop session ID

---

3.  **Communication With Your Backend**

    The desktop app must:
    *   Use its existing authentication session (local token, refresh token, etc.)
    *   When receiving the signed challenge from web app:
        1.  Send the challenge + desktop login token to backend
        2.  Backend verifies that:
            *   Desktop user is authenticated
            *   Challenge matches web request
            *   Receive a web-session-token from backend (This is the token issued by `AuthController.LoginFromSession`)
        *   Return it to the web app via `/exchange`

---

4.  **Local HTTP Server CORS Headers**

    Your desktop server must include:

    `Access-Control-Allow-Origin: https://your-web-domain.com`
    `Access-Control-Allow-Headers: *`
    `Access-Control-Allow-Methods: GET, POST, OPTIONS`

    Also allow:
    *   Preflight `OPTIONS` requests

    This lets browser JavaScript call your localhost server directly.

---

5.  **Random Port Broadcasting**

    On startup, the desktop app picks a random port and exposes:
    *   `/alive`
    *   `/exchange`

    But the web app needs to know the port.

    Two solutions:

    Option A (simple):

    Web app scans 20 known ports (e.g., `41000–41020`).

    Option B (secure):

    Desktop app writes port to:
    *   macOS: `~/Library/Application Support/MyApp/port.json`
    *   Windows: `%APPDATA%/MyApp/port.json`
    *   Linux: `~/.config/MyApp/port.json`

    Web app then tries only one port if user clicks “Connect Desktop”.

---

6.  **Custom Protocol (Optional but helpful)**

    Register custom protocol:

    `solian://auth/connect`

    Used to trigger desktop app if it’s closed.

    Flow:
    1.  Web app tries localhost discovery
    2.  If not found → open `solian://auth/connect?some args`
    3.  Desktop app starts → exposes localhost server
    4.  Web page retries detection

---

7.  **App UI Behavior**

    The desktop UI should:
    *   Run the HTTP server silently in background
    *   Possibly show a “Connecting to web” indicator
    *   Close or hide handshake window after success
    *   Notify user if login sync succeeded

---

8.  **Logging**

    Implement basic logs:
    *   Server started on port `XXX`
    *   Received `/alive`
    *   Verified challenge
    *   Sent credentials to web
    *   Errors or invalid tokens

    Logs should never include full tokens.

---

9.  **Optional: WebSocket Support**

    Not required, but improves performance.
    *   Web app connects `ws://localhost:<port>/ws`
    *   Faster challenge exchange
    *   Real-time two-way handshake

---

## ⭐ Summary: Desktop App Needs to Implement

Core
*   Local HTTP server on `127.0.0.1:<random_port>`
*   `/alive` endpoint with random challenge
*   `/exchange` endpoint to finish login
*   CORS + preflight support
*   Challenge-response security
*   Only one-time challenges

Backend communication
*   Desktop app verifies web request with backend
*   Backend creates session for web (via `AuthController.LoginFromSession`)
*   Desktop returns session token to web

Optional
*   Custom URI protocol handler (`solian://`)
*   Port broadcast file
*   WebSocket tunnel

# WebSocket Gateway - Porting Guide

This document provides detailed technical information for porting the WebSocket gateway to another platform or language.

## Table of Contents

1. [Architecture Overview](#architecture-overview)
2. [Project Structure](#project-structure)
3. [Core Components](#core-components)
4. [Connection Lifecycle](#connection-lifecycle)
5. [Message Protocol](#message-protocol)
6. [Authentication](#authentication)
7. [Packet Handling](#packet-handling)
8. [Event System](#event-system)
9. [gRPC Integration](#grpc-integration)
10. [Message Queue Integration](#message-queue-integration)
11. [Configuration](#configuration)
12. [Dependencies](#dependencies)

---

## Architecture Overview

The WebSocket gateway is a real-time communication service in the DysonNetwork platform. It:

- Accepts WebSocket connections at `/ws` endpoint
- Authenticates users via token-based authentication
- Manages active connections in memory
- Routes messages to appropriate handlers
- Publishes connection events to NATS JetStream
- Provides gRPC endpoints for external services to push messages

**Technology Stack:**
- **Runtime:** .NET 10.0 (ASP.NET Core)
- **WebSocket:** System.Net.WebSockets
- **Message Queue:** NATS (via NATS.Client.Core)
- **Event Bus:** Custom EventBus with NATS JetStream
- **Authentication:** Custom token-based scheme (DysonTokenAuth)
- **Protocol:** JSON over WebSocket binary frames

---

## Project Structure

```
DysonNetwork.Ring/
├── Connection/
│   ├── WebSocketController.cs      # Main WebSocket endpoint
│   ├── WebSocketService.cs         # Connection management & packet routing
│   └── IWebSocketPacketHandler.cs  # Interface for packet handlers
├── Services/
│   └── PusherServiceGrpc.cs        # gRPC service for external push
└── Notification/
    └── PushService.cs              # Push notification delivery

DysonNetwork.Shared/
├── Models/
│   ├── WebSocketPacket.cs          # Packet model & serialization
│   ├── WebSocket.cs                 # Packet type constants
│   ├── Account.cs                   # Account model (SnAccount)
│   └── AuthSession.cs               # Session model
├── Queue/
│   └── WebSocketPacketEvent.cs     # Event models
└── Auth/
    ├── Startup.cs                   # Auth configuration
    ├── AuthScheme.cs                # DysonTokenAuthHandler
    └── AuthConstants.cs             # Auth constants
```

---

## Core Components

### 1. WebSocketController

**File:** `Connection/WebSocketController.cs`

The main WebSocket endpoint that handles the connection lifecycle.

**Key Properties:**
- Route: `/ws`
- Requires Authorization: Yes (`[Authorize]` attribute)
- Optional query parameter: `deviceAlt` (currently supports "watch")

**Key Methods:**

```csharp
[Route("/ws")]
[Authorize]
public async Task<ActionResult> TheGateway([FromQuery] string? deviceAlt)
```

**Connection Flow:**
1. Validates `deviceAlt` parameter (only "watch" is allowed)
2. Retrieves authenticated user from `HttpContext.Items["CurrentUser"]`
3. Retrieves session from `HttpContext.Items["CurrentSession"]`
4. Generates device ID from session's `ClientId`
5. Appends `deviceAlt` to device ID if provided (e.g., "deviceId+watch")
6. Accepts WebSocket connection with 60-second keep-alive interval
7. Attempts to add connection to WebSocketService
8. If duplicate connection exists, sends error and closes
9. Publishes `WebSocketConnectedEvent` via EventBus
10. Runs message receive loop
11. On disconnect, publishes `WebSocketDisconnectedEvent`

**Error Responses:**
- `400 Bad Request` - Unsupported device alternative
- `401 Unauthorized` - Authentication failed
- `1008 PolicyViolation` - Duplicate connection from same device/account

### 2. WebSocketService

**File:** `Connection/WebSocketService.cs`

Manages active connections and routes packets to handlers.

**Static Fields:**
```csharp
// Key: (AccountId, DeviceId) - Value: (WebSocket, CancellationTokenSource)
private static readonly ConcurrentDictionary<
    (Guid AccountId, string DeviceId),
    (WebSocket Socket, CancellationTokenSource Cts)
> ActiveConnections = new();

// Key: DeviceId - Value: ChatRoomId
private static readonly ConcurrentDictionary<string, string> ActiveSubscriptions = new();
```

**Public Methods:**

```csharp
// Try to add a new connection. Returns false if key already exists.
public bool TryAdd(
    (Guid AccountId, string DeviceId) key,
    WebSocket socket,
    CancellationTokenSource cts
)

// Disconnect a specific connection
public void Disconnect((Guid AccountId, string DeviceId) key, string? reason = null)

// Check if a device is connected
public static bool GetDeviceIsConnected(string deviceId)

// Check if any device of an account is connected
public static bool GetAccountIsConnected(Guid accountId)

// Get all connected user IDs
public static List<Guid> GetAllConnectedUserIds()

// Send packet to all devices of an account
public static void SendPacketToAccount(Guid accountId, WebSocketPacket packet)

// Send packet to a specific device
public void SendPacketToDevice(string deviceId, WebSocketPacket packet)

// Handle incoming packet from client
public async Task HandlePacket(
    Account currentUser,
    string deviceId,
    WebSocketPacket packet,
    WebSocket socket
)
```

**Packet Handling Logic (in `HandlePacket`):**

1. **Ping Packet** → Immediately reply with Pong packet
2. **Known Packet Type** → Find handler in `_handlerMap` and call `HandleAsync`
3. **Unknown Packet with Endpoint** → Forward to NATS queue with subject `websocket_{endpoint}`
4. **Unknown Packet without Endpoint** → Send error packet back to client

### 3. IWebSocketPacketHandler

**File:** `Connection/IWebSocketPacketHandler.cs`

Interface for implementing packet handlers.

```csharp
public interface IWebSocketPacketHandler
{
    // The packet type this handler processes (e.g., "messages.new")
    string PacketType { get; }

    Task HandleAsync(
        Account currentUser,
        string deviceId,
        WebSocketPacket packet,
        WebSocket socket,
        WebSocketService srv
    );
}
```

**Registration:**
Handlers are registered via dependency injection. The `WebSocketService` constructor uses:
```csharp
public WebSocketService(
    IEnumerable<IWebSocketPacketHandler> handlers,  // Auto-registered handlers
    ILogger<WebSocketService> logger,
    INatsConnection nats
)
{
    _handlerMap = handlers.ToDictionary(h => h.PacketType);
}
```

---

## Connection Lifecycle

### Connection Establishment

```
Client                          Gateway
  |                               |
  |--- CONNECT /ws?deviceAlt=watch|
  |   + Authorization: Bearer X  |
  |------------------------------>|
  |                               |
  |         [Authenticate]        |
  |         [Validate Token]     |
  |         [Get Session]         |
  |         [Get Account]        |
  |                               |
  |<-- 101 Switching Protocols --|
  |       (WebSocket Accept)      |
  |                               |
  |         [TryAdd to            |
  |         ActiveConnections]   |
  |                               |
  |         [Publish              |
  |         WebSocketConnected   |
  |         Event to NATS]        |
```

### Message Flow

```
Client                          Gateway
  |                               |
  |--- [Binary JSON Packet] ------>|
  |    {                          |
  |      "type": "messages.new",  |
  |      "data": { ... }          |
  |    }                          |
  |                               |
  |      [Deserialize Packet]    |
  |      [Lookup Handler]         |
  |      [Call Handler]          |
  |      [Process Message]       |
  |                               |
```

### Disconnection Flow

```
Client                          Gateway
  |                               |
  |--- [Close Frame] ------------>|
  |                               |
  |      [Remove from             |
  |       ActiveConnections]      |
  |                               |
  |      [Publish                 |
  |       WebSocketDisconnected   |
  |       Event to NATS]          |
  |                               |
  |      [Check if account        |
  |       has other connections]  |
  |      [Update online status]   |
```

---

## Message Protocol

### WebSocketPacket Structure

**File:** `DysonNetwork.Shared/Models/WebSocketPacket.cs`

```csharp
public class WebSocketPacket
{
    // Required: Packet type identifier
    public string Type { get; set; } = null!;

    // Optional: Packet payload (any JSON-serializable object)
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Data { get; set; }

    // Optional: Endpoint for unknown packet types (forwarded to NATS)
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Endpoint { get; set; }

    // Optional: Error message for error packets
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorMessage { get; set; }
}
```

### Serialization

- **Format:** JSON
- **Encoding:** UTF-8
- **Transport:** WebSocket Binary frames
- **Serializer Options:** Custom `InfraObjectCoder.SerializerOptions` (System.Text.Json)

```csharp
// Deserialize from bytes
public static WebSocketPacket FromBytes(byte[] bytes)
{
    var json = Encoding.UTF8.GetString(bytes);
    return JsonSerializer.Deserialize<WebSocketPacket>(json, InfraObjectCoder.SerializerOptions);
}

// Serialize to bytes
public byte[] ToBytes()
{
    var json = JsonSerializer.Serialize(this, InfraObjectCoder.SerializerOptions);
    return Encoding.UTF8.GetBytes(json);
}
```

### Packet Types

**File:** `DysonNetwork.Shared/Models/WebSocket.cs`

```csharp
public abstract class WebSocketPacketType
{
    public const string Ping = "ping";
    public const string Pong = "pong";
    public const string Error = "error";
    public const string MessageNew = "messages.new";
    public const string MessageUpdate = "messages.update";
    public const string MessageDelete = "messages.delete";
    public const string MessageReactionAdded = "messages.reaction.added";
    public const string MessageReactionRemoved = "messages.reaction.removed";
    public const string CallParticipantsUpdate = "call.participants.update";
}
```

### Example Packets

**Ping Packet:**
```json
{
    "type": "ping"
}
```

**Pong Packet:**
```json
{
    "type": "pong"
}
```

**Error Packet:**
```json
{
    "type": "error",
    "errorMessage": "Unprocessable packet: unknown.type"
}
```

**Message Packet:**
```json
{
    "type": "messages.new",
    "data": {
        "messageId": "uuid",
        "content": "Hello world",
        "chatRoomId": "uuid"
    }
}
```

---

## Authentication

### Authentication Flow

The WebSocket gateway uses token-based authentication via the `DysonTokenAuthHandler`.

**File:** `DysonNetwork.Shared/Auth/AuthScheme.cs`

### Token Extraction (in order of priority)

1. **Query Parameter:** `?tk=<token>`
2. **Authorization Header:**
   - `Bearer <token>` - AuthKey or OIDC token
   - `AtField <token>` - AuthKey token
   - `AkField <token>` - API key
3. **Cookie:** `AuthToken`

### Token Types

```csharp
public enum TokenType
{
    AuthKey,   // Standard authentication key
    OidcKey,   // OpenID Connect token (3-part JWT)
    ApiKey     // API key
}
```

Token type is determined by:
- **Bearer:** If JWT has 3 parts → `OidcKey`, otherwise → `AuthKey`
- **AtField:** Always → `AuthKey`
- **AkField:** Always → `ApiKey`
- **Cookie:** If contains 2 dots → `OidcKey`, otherwise → `AuthKey`

### Token Validation

Tokens are validated via gRPC call to AuthService:

```csharp
private async Task<AuthSession> ValidateToken(string token, string? ipAddress)
{
    var resp = await auth.AuthenticateAsync(new AuthenticateRequest
    {
        Token = token,
        IpAddress = ipAddress
    });
    if (!resp.Valid) throw new InvalidOperationException(resp.Message);
    return resp.Session;
}
```

### Context Population

After successful authentication, the following is stored in `HttpContext.Items`:

```csharp
Context.Items["CurrentUser"] = session.Account;        // SnAccount
Context.Items["CurrentSession"] = session;              // SnAuthSession
Context.Items["CurrentTokenType"] = tokenInfo.Type;     // TokenType as string
```

### Claims

The following claims are added to the principal:

| Claim Type | Value Source |
|-----------|--------------|
| `user_id` | `session.Account.Id` |
| `session_id` | `session.Id` |
| `token_type` | `tokenInfo.Type` |
| `scope` | Each scope in `session.Scopes` |
| `is_superuser` | "1" if `session.Account.IsSuperuser` |

---

## Packet Handling

### Built-in Handlers

Handlers are automatically discovered and registered via DI. Each handler implements `IWebSocketPacketHandler` with a specific `PacketType`.

### Creating a Custom Handler

```csharp
public class MyCustomPacketHandler : IWebSocketPacketHandler
{
    public string PacketType => "custom.packet.type";

    public async Task HandleAsync(
        Account currentUser,
        string deviceId,
        WebSocketPacket packet,
        WebSocket socket,
        WebSocketService srv)
    {
        // Get typed data
        var data = packet.GetData<MyCustomData>();
        
        // Process the packet
        // ...
        
        // Optionally send a response
        await socket.SendAsync(
            new ArraySegment<byte>(new WebSocketPacket
            {
                Type = "custom.response",
                Data = new { success = true }
            }.ToBytes()),
            WebSocketMessageType.Binary,
            true,
            CancellationToken.None
        );
        
        // Or broadcast to user's all devices
        WebSocketService.SendPacketToAccount(
            Guid.Parse(currentUser.Id),
            new WebSocketPacket { Type = "notification", Data = new { ... } }
        );
    }
}
```

### Handler Registration

In your service configuration, simply register the handler:
```csharp
services.AddScoped<IWebSocketPacketHandler, MyCustomPacketHandler>();
```

The `WebSocketService` will automatically register it.

---

## Event System

### Connection Events

**File:** `DysonNetwork.Shared/Queue/WebSocketPacketEvent.cs`

Two event types are published to NATS JetStream:

#### WebSocketConnectedEvent

```csharp
public class WebSocketConnectedEvent : EventBase
{
    public static string Type => "websocket_connected";
    public override string EventType => Type;
    public override string StreamName => "websocket_events";

    public Guid AccountId { get; set; }
    public string DeviceId { get; set; }
    public Instant ConnectedAt { get; set; }  // Default: current time
    public bool IsOffline { get; set; }       // Default: false
}
```

#### WebSocketDisconnectedEvent

```csharp
public class WebSocketDisconnectedEvent : EventBase
{
    public static string Type => "websocket_disconnected";
    public override string EventType => Type;
    public override string StreamName => "websocket_events";

    public Guid AccountId { get; set; }
    public string DeviceId { get; set; }
    public Instant DisconnectedAt { get; set; }  // Default: current time
    public bool IsOffline { get; set; }          // True if no other devices connected
}
```

### Subscribing to Events

Other services can subscribe to these events from NATS JetStream to:
- Update online/offline status in database
- Send push notifications when user goes offline
- Log connection analytics

---

## gRPC Integration

### RingServiceGrpc

**File:** `Services/PusherServiceGrpc.cs`

Provides gRPC endpoints for external services to interact with WebSocket connections.

```protobuf
// Proto definition (in DysonNetwork.Shared.Proto)
service RingService {
    // Push WebSocket packet to a single user
    rpc PushWebSocketPacket(PushWebSocketPacketRequest) returns (google.protobuf.Empty);
    
    // Push WebSocket packet to multiple users
    rpc PushWebSocketPacketToUsers(DyPushWebSocketPacketToUsersRequest) returns (google.protobuf.Empty);
    
    // Push WebSocket packet to a specific device
    rpc PushWebSocketPacketToDevice(PushWebSocketPacketToDeviceRequest) returns (google.protobuf.Empty);
    
    // Push WebSocket packet to multiple devices
    rpc PushWebSocketPacketToDevices(PushWebSocketPacketToDevicesRequest) returns (google.protobuf.Empty);
    
    // Get WebSocket connection status
    rpc GetWebsocketConnectionStatus(GetWebsocketConnectionStatusRequest) returns (GetWebsocketConnectionStatusResponse);
    
    // Get batch connection status
    rpc GetWebsocketConnectionStatusBatch(GetWebsocketConnectionStatusBatchRequest) returns (GetWebsocketConnectionStatusBatchResponse);
    
    // Get all connected user IDs
    rpc GetAllConnectedUserIds(google.protobuf.Empty) returns (GetAllConnectedUserIdsResponse);
}
```

### Request/Response Types

```csharp
// PushWebSocketPacketRequest
public class PushWebSocketPacketRequest
{
    public string UserId { get; set; }    // Account ID (GUID as string)
    public Proto.WebSocketPacket Packet { get; set; }
}

// PushWebSocketPacketToDeviceRequest
public class PushWebSocketPacketToDeviceRequest
{
    public string DeviceId { get; set; }
    public Proto.WebSocketPacket Packet { get; set; }
}

// GetWebsocketConnectionStatusRequest
public class GetWebsocketConnectionStatusRequest
{
    // Oneof: UserId or DeviceId
    public string UserId { get; set; }
    public string DeviceId { get; set; }
}

// GetWebsocketConnectionStatusResponse
public class GetWebsocketConnectionStatusResponse
{
    public bool IsConnected { get; set; }
}
```

### Usage Examples

**Push notification to user:**
```csharp
// From another service
var channel = GrpcChannel.ForAddress("http://ring-service:5000");
var client = new DyRingService.DyRingServiceClient(channel);

await client.PushWebSocketPacketAsync(new PushWebSocketPacketRequest
{
    UserId = userId.ToString(),
    Packet = new Proto.WebSocketPacket
    {
        Type = "messages.new",
        Data = ByteString.CopyFromUtf8(JsonSerializer.Serialize(messageData))
    }
});
```

**Check if user is online:**
```csharp
var response = await client.GetWebsocketConnectionStatusAsync(
    new GetWebsocketConnectionStatusRequest { UserId = userId.ToString() }
);
Console.WriteLine($"User is {(response.IsConnected ? "online" : "offline")}");
```

---

## Message Queue Integration

### NATS Integration

Unknown packet types are forwarded to NATS for processing by other services.

### Packet Forwarding

When a packet with an unknown type but with an `Endpoint` is received:

```csharp
if (packet.Endpoint is not null)
{
    // Transform endpoint: "DysonNetwork.ServiceName.Method" -> "service_service.method"
    var endpoint = packet.Endpoint.Replace("DysonNetwork.", "").ToLower();
    
    await _nats.PublishAsync(
        WebSocketPacketEvent.SubjectPrefix + endpoint,  // "websocket_service.method"
        InfraObjectCoder.ConvertObjectToByteString(new WebSocketPacketEvent
        {
            AccountId = Guid.Parse(currentUser.Id),
            DeviceId = deviceId,
            PacketBytes = packet.ToBytes()
        }).ToByteArray()
    );
}
```

### WebSocketPacketEvent

```csharp
public class WebSocketPacketEvent : EventBase
{
    public static string Type => "websocket_msg";
    public const string SubjectPrefix = "websocket_";

    public Guid AccountId { get; set; }
    public string DeviceId { get; set; }
    public byte[] PacketBytes { get; set; }
}
```

### Subscribing to Packets

Other services can subscribe to `websocket_*` subjects to process packets:

```csharp
// Example subscriber (pseudo-code)
await nats.SubscribeAsync("websocket_messages.new", async (data) =>
{
    var packetEvent = WebSocketPacketEvent.Parser.ParseFrom(data);
    var packet = WebSocketPacket.FromBytes(packetEvent.PacketBytes);
    
    // Process the packet
    // ...
});
```

---

## Configuration

### appsettings.json

```json
{
  "AllowedHosts": "*",
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://localhost:5212"
      }
    }
  },
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=dysonring;Username=user;Password=pass"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

### WebSocket Configuration

**In WebSocketController.cs:**
```csharp
await HttpContext.WebSockets.AcceptWebSocketAsync(
    new WebSocketAcceptContext 
    { 
        KeepAliveInterval = TimeSpan.FromSeconds(60) 
    }
);
```

### Middleware Pipeline

```csharp
// In Program.cs / Startup
app.UseRequestLocalization();
app.ConfigureForwardedHeaders();  // For proxies
app.UseWebSockets();
app.UseAuthentication();
app.UseAuthorization();
app.RequireRateLimiting("fixed");
```

---

## Dependencies

### NuGet Packages

```xml
<PackageReference Include="Grpc.AspNetCore.Server" Version="2.76.0" />
<PackageReference Include="NATS.Client.Core" Version="*" />
```

### Internal Dependencies

- **DysonNetwork.Shared** - Shared models, auth, and utilities
- **DysonNetwork.Shared.Proto** - Protocol Buffers definitions

### External Services Required

1. **AuthService** - gRPC service for token authentication
2. **NATS Server** - Message queue and event streaming
3. **PostgreSQL** - Database (for application data)

---

## Key Implementation Details

### Connection Key

Connections are uniquely identified by the tuple `(AccountId, DeviceId)`:

```csharp
var connectionKey = (accountId, deviceId);
// Example: (Guid.Parse("..."), "device-uuid+watch")
```

### Duplicate Connection Handling

When a new connection attempt uses the same (AccountId, DeviceId):
1. The existing connection is disconnected with reason: "Just connected somewhere else..."
2. The new connection is added
3. Client receives `error.dupe` packet before close

### Device ID Generation

```csharp
// From session
var deviceId = currentSession.ClientId;  // e.g., "uuid-from-database"

// If deviceAlt provided (e.g., ?deviceAlt=watch)
if (deviceAlt is not null)
    deviceId = $"{deviceId}+{deviceAlt}";  // e.g., "uuid+watch"
```

### Offline Status

When a user disconnects, the `IsOffline` flag is set based on whether other devices are still connected:

```csharp
IsOffline = !WebSocketService.GetAccountIsConnected(accountId)
```

---

## Porting Checklist

When porting this gateway to another platform:

- [ ] Implement WebSocket server at `/ws` endpoint
- [ ] Implement token-based authentication (extract from query/header/cookie)
- [ ] Validate tokens via AuthService gRPC
- [ ] Manage connections in thread-safe storage (ConcurrentDictionary equivalent)
- [ ] Implement ping/pong heartbeat mechanism
- [ ] Implement packet serialization (JSON over binary frames)
- [ ] Create handler registry for known packet types
- [ ] Forward unknown packets to message queue
- [ ] Publish connection/disconnection events
- [ ] Implement gRPC endpoints for external push
- [ ] Configure 60-second keep-alive interval
- [ ] Handle duplicate connection rejection

---

## Security Considerations

1. **Always validate tokens** before accepting WebSocket connections
2. **Use secure WebSocket (WSS)** in production
3. **Implement rate limiting** to prevent connection floods
4. **Validate all incoming packet data** before processing
5. **Limit message size** (default 4KB buffer in this implementation)
6. **Log connection events** for auditing

---

## Performance Considerations

1. **Connection storage is in-memory** - not persisted across restarts
2. **Use ConcurrentDictionary** for thread-safe connection management
3. **Consider connection limits** based on available memory
4. **Use connection pooling** for database and NATS connections
5. **Monitor NATS queue** for message backlog

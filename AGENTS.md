# DysonNetwork Agent Guidelines

This document provides essential information for AI agents working on the DysonNetwork codebase.

## Project Structure

```
DysonNetwork/           # Main repository (this repo)
├── DysonNetwork.Padlock/     # Authentication & authorization service
├── DysonNetwork.Passport/    # User profiles & social features
├── DysonNetwork.Sphere/      # ActivityPub & federated content
├── DysonNetwork.Messager/    # Real-time messaging
├── DysonNetwork.Drive/       # File storage & E2EE
├── DysonNetwork.Wallet/      # Payments & subscriptions
├── DysonNetwork.Fitness/     # Health & fitness tracking
├── DysonNetwork.Ring/        # Real-time communication (calls)
├── DysonNetwork.Zone/        # Zones & communities
├── DysonNetwork.Develop/     # Developer portal & app management
├── DysonNetwork.Insight/     # AI features
└── DysonNetwork.Shared/      # Shared models, proto, utilities

DysonSpec/              # Protobuf definitions (separate repo)
├── proto/              # .proto files
└── gen/                # Generated code
```

## Protobuf Definitions (DysonSpec)

**Important:** Protocol Buffer definitions are maintained in a separate repository.

- **Location:** `../DysonSpec/` (sibling to this repo)
- **Proto files:** `../DysonSpec/proto/*.proto`
- **Generated C# code:** `DysonNetwork.Shared/Proto/` (auto-generated, do not edit manually)
  - When you finished editing the `.proto` file, ask the user to regenerate the code.

### Proto to C# Model Mapping

Models in `DysonNetwork.Shared/Models/` have:

- `ToProto()` method for C# → Proto conversion
- `FromProtoValue()` static method for Proto → C# conversion

Always update both methods when adding new fields.

## JSON Serialization

**All APIs use snake_case for JSON property names.**

```csharp
// Configured in Startup/ServiceCollectionExtensions.cs
options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;
```

### Example

```csharp
// C# model
public class UserProfile
{
    public string DisplayName { get; set; }
    public DateTime CreatedAt { get; set; }
}

// JSON output
{
    "display_name": "John Doe",
    "created_at": "2024-01-15T10:30:00Z"
}
```

### Exceptions

Some external integrations use `CamelCase` or `PropertyNamingPolicy = null`:

- ActivityPub payloads (federation compatibility)
- Third-party OAuth providers (Google, Discord, etc.)
- External payment webhooks

## API Gateway & URL Routing

### Production Gateway

In production, a gateway sits in front of all services. API routes are transformed:

```
Local Development:     /api/controller/action
                       ↓
Production Gateway:    /{service}/controller/action
```

### Service Name Mapping

| Service Project       | Route Prefix    |
| --------------------- | --------------- |
| DysonNetwork.Padlock  | `/padlock/...`  |
| DysonNetwork.Passport | `/passport/...` |
| DysonNetwork.Sphere   | `/sphere/...`   |
| DysonNetwork.Messager | `/messager/...` |
| DysonNetwork.Drive    | `/drive/...`    |
| DysonNetwork.Wallet   | `/wallet/...`   |
| DysonNetwork.Fitness  | `/fitness/...`  |
| DysonNetwork.Ring     | `/ring/...`     |
| DysonNetwork.Zone     | `/zone/...`     |
| DysonNetwork.Develop  | `/develop/...`  |
| DysonNetwork.Insight  | `/insight/...`  |

### Example

```
Local:    /api/auth/login          (Padlock)
Production: /padlock/auth/login

Local:    /api/users/me            (Passport)
Production: /passport/users/me
```

### Route Configuration

Controllers use `[Route("/api/...")]` attribute. The gateway strips `/api` and prepends the service name.

```csharp
[Route("/api/auth")]  // Becomes /padlock/auth in production
public class AuthController : ControllerBase { }
```

### Discovery Endpoint Exceptions

Some endpoints have fixed paths (not transformed):

```
/.well-known/openid-configuration
/.well-known/jwks
/.well-known/webfinger
```

## Database Conventions

### EF Core Naming

All `AppDatabase.cs` files use snake_case naming convention:

```csharp
.UseSnakeCaseNamingConvention()
```

Database tables and columns will be in snake_case:

- Table: `auth_sessions`
- Column: `created_at`, `account_id`

### Model Base Class

Models inherit from `ModelBase` which provides:

```csharp
public class ModelBase
{
    public Instant CreatedAt { get; set; }
    public Instant UpdatedAt { get; set; }
}
```

## gRPC Services

Services communicate via gRPC when possible:

- Client factories: `DysonNetwork.Shared/Registry/LazyGrpcClientFactory.cs`
- DI (prefer this way to add clients): `DysonNetwork.Shared/Registry/ServiceInjectionHelper.cs`
- Service definitions: `DysonNetwork.Shared/Proto/*Grpc.cs`
- Service implementations: `*Grpc.cs` files in each service

### Common Pattern

```csharp
// In the consuming service
public class MyService
{
    private readonly DyCustomAppService.DyCustomAppServiceClient _customApps;

    public MyService(LazyGrpcClientFactory<DyCustomAppService.DyCustomAppServiceClient> factory)
    {
        _customApps = factory.GetClient();
    }
}
```

## NodaTime

All date/time handling uses NodaTime:

```csharp
using NodaTime;

public class MyEntity
{
    public Instant CreatedAt { get; set; }
    public Instant? ExpiredAt { get; set; }
}
```

- `Instant` for timestamps
- `Duration` for time spans
- `ZonedDateTime` rarely used (prefer UTC)

## Cache Service

Redis caching via `ICacheService`:

```csharp
public interface ICacheService
{
    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null);
    Task<T?> GetAsync<T>(string key);
    Task<(bool Found, T? Value)> GetAsyncWithStatus<T>(string key);
    Task RemoveAsync(string key);
}
```

## Event Bus

NATS-based event bus for inter-service communication:

```csharp
// Publish
await eventBus.PublishAsync(new MyEvent { ... });

// Subscribe (in background service)
eventBus.Subscribe<MyEvent>("my-event", async (data, headers) => {
    // Handle event
    return (Success: true, ShouldAck: true);
});
```

## Testing

- No testing

## Code Style

- Nullable reference types enabled
- File-scoped namespaces preferred
- Implicit usings enabled
- No comments unless explicitly requested
- Follow existing patterns in the codebase

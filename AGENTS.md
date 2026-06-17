# DysonNetwork Agent Guidelines

This document provides essential information for AI agents working on the DysonNetwork codebase.

## Project Structure

```
DysonNetwork/           # Main repository (this repo)
├── DysonNetwork.Padlock/     # Authentication & authorization service
├── DysonNetwork.Passport/    # User profiles & social features
├── DysonNetwork.Sphere/      # ActivityPub & federated content
├── DysonNetwork.Messager/    # Real-time messaging
├── DysonNetwork.Drive/       # File storage & E2EE (migrated to DysonFS: ../DysonFS)
├── DysonNetwork.Wallet/      # Payments & subscriptions
├── DysonNetwork.Ring/        # Real-time communication (calls)
├── DysonNetwork.Zone/        # Zones & communities (discontinued)
├── DysonNetwork.Develop/     # Developer portal & app management
├── DysonNetwork.Insight/     # AI features
└── DysonNetwork.Shared/      # Git submodule → NeTo repo

NeTo/                   # Shared library (separate repo: ../NeTo)
├── Models/             # Shared domain models
├── Proto/              # Generated gRPC/proto code
├── Registry/           # Service clients & helpers
├── Cache/              # Redis caching abstractions
├── EventBus/           # NATS event bus
├── Auth/               # Authentication middleware
├── Data/               # Database utilities
└── ...                 # Other shared utilities

Spec/                   # Protobuf definitions (separate repo: ../Spec)
└── proto/              # .proto files
```

## Protobuf Definitions (Spec)

**Important:** Protocol Buffer definitions are maintained in a separate repository.

- **Location:** `../Spec/` (sibling to this repo)
- **Proto files:** `../Spec/proto/*.proto`
- **Buf config:** `../Spec/buf.yaml`
- **Generation config:** `NeTo/buf.gen.yaml`
- **Generated C# code:** `NeTo/Proto/` (auto-generated, do not edit manually)
  - Regenerate with: `buf generate` under `NeTo/`

### Proto to C# Model Mapping

Models in `NeTo/Models/` have:

- `ToProto()` method for C# → Proto conversion
- `FromProtoValue()` static method for Proto → C# conversion

Always update both methods when adding new fields.

## Shared Module (NeTo)

**DysonNetwork.Shared is a git submodule pointing to the NeTo repository.**

- **Repository:** `ssh://git@compute01.latxa-bushi.ts.net/SoSYS/NeTo.git`
- **Submodule URL:** `https://src.solsynth.dev/SoSYS/NeTo.git`
- **Local path:** `DysonNetwork.Shared/` (submodule)

### What Goes in NeTo

- Models used by **multiple services** (e.g., `SnAccount`, `SnPost`, `SnCloudFile`)
- Proto/gRPC generated code
- Shared utilities (Cache, EventBus, Registry, etc.)

### What Stays in Service Repos

- Models used by **only one service** (e.g., `SnLiveStream` in Sphere, `SnMiniApp` in Develop)
- Service-specific logic, controllers, jobs

### Updating the Submodule

```bash
# Pull latest NeTo changes
cd DysonNetwork
git submodule update --remote

# Or init + update on fresh clone
git submodule update --init --recursive
```

## Adding a New Shared Model / Proto

When you need to add a new shared model or gRPC service definition, follow this workflow:

### Step 1: Add Protobuf Definition to Spec

```bash
cd ../Spec

# Edit or create .proto file
vi proto/my_new_service.proto

# Commit and push
git add -A
git commit -m "➕ add MyNewService proto definition"
git push
```

### Step 2: Regenerate Code in NeTo

```bash
cd ../NeTo

# Regenerate C# code from proto
buf generate

# Commit and push
git add -A
git commit -m "🔄 regenerate proto: add MyNewService"
git push
```

### Step 3: Update Submodules in Consuming Repos

```bash
# In DysonNetwork
cd ../DysonNetwork
git submodule update --remote
git add DysonNetwork.Shared
git commit -m "⬆️ update NeTo submodule (add MyNewService)"
git push

# In WattEngine (if applicable)
cd ../WattEngine
git submodule update --remote
git add NeTo
git commit -m "⬆️ update NeTo submodule (add MyNewService)"
git push
```

### Step 4: Add C# Model (if needed)

If the new proto requires a corresponding C# model:

```bash
cd ../NeTo

# Add model file
vi Models/MyNewModel.cs

# Ensure it has:
# - ToProto() method
# - FromProtoValue() static method

# Commit and push
git add -A
git commit -m "➕ add MyNewModel with proto mapping"
git push

# Update submodules again (repeat Step 3)
```

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
| DysonNetwork.Ring     | `/ring/...`     |
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

- Client factories: `NeTo/Registry/LazyGrpcClientFactory.cs`
- DI (prefer this way to add clients): `NeTo/Registry/ServiceInjectionHelper.cs`
- Service definitions: `NeTo/Proto/*Grpc.cs`
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

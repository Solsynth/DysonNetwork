# Endpoint Capabilities

## Overview

`DysonNetwork.Shared.Capabilities` provides a declarative way for a service to
advertise the features exposed by its HTTP API. A capability is declared as
endpoint metadata, so it stays with the controller action or Minimal API route
that exposes it.

The shared implementation collects this metadata once when the application
starts and exposes the result through `ICapabilityRegistry`. It does not add an
HTTP route, a gRPC method, authorization behavior, or annotations to existing
endpoints.

## Declaring a feature

Apply `ApiFeatureAttribute` to a controller to describe every action in that
controller, or apply it to an action for a more specific feature. Attributes
from both levels are retained in endpoint metadata.

```csharp
using DysonNetwork.Shared.Capabilities;

[ApiController]
[Route("api/chat")]
[ApiFeature("chat", Revision = 16)]
public class ChatController : ControllerBase
{
    [HttpPost("voice")]
    [ApiFeature(
        "voice",
        Revision = 17)]
    public IActionResult JoinVoice()
    {
        return Ok();
    }
}
```

Multiple feature attributes are supported on a controller or action.

```csharp
[ApiFeature("chat")]
[ApiFeature("chat.reactions", Revision = 18, Experimental = true)]
```

For a Minimal API, attach the same attribute through endpoint metadata.

```csharp
app.MapPost("/api/chat/voice", JoinVoice)
    .WithMetadata(new ApiFeatureAttribute("voice")
    {
        Revision = 17
    });
```

Capability identifiers are strings. Use stable, lowercase, dot-separated names
such as `chat`, `chat.reactions`, or `voice`; the shared library deliberately
does not prescribe a global capability enum yet.

## Metadata fields

| Field | Meaning |
| --- | --- |
| `Capability` | Required capability identifier. |
| `Revision` | API revision that introduced the feature. `0` means unspecified. |
| `Experimental` | Whether clients should treat the feature as experimental. |

The registry also records the endpoint route pattern and display name alongside
each feature declaration.

## Registration and lifecycle

Register the collector with the service collection before building the app:

```csharp
builder.Services.AddEndpointCapabilities();
```

The collector runs as a hosted service after endpoint mapping is complete. It
reads the application's `EndpointDataSource`, uses
`GetOrderedMetadata<ApiFeatureAttribute>()`, and stores one immutable snapshot
in `CapabilityRegistry`.

```csharp
public sealed class CapabilitiesService(ICapabilityRegistry capabilityRegistry)
{
    public IReadOnlyList<string> GetCapabilities() => capabilityRegistry.Capabilities;

    public IReadOnlyList<ApiFeature> GetFeatures() => capabilityRegistry.Features;
}
```

`Capabilities` contains distinct capability identifiers. `Features` retains
the per-endpoint details, including revision, experimental state, and route.
Both collections are ordered deterministically by capability and route.

## Current scope

This facility is intentionally unconnected at present:

- No DysonNetwork service has registered the collector.
- No existing controller or Minimal API endpoint is annotated.
- No gRPC `GetCapabilities` contract or HTTP discovery endpoint exists.
- Features are descriptive metadata only; they do not grant, deny, or enforce
  access.

When a service is ready to advertise capabilities, register the collector,
annotate its relevant endpoints, and expose `ICapabilityRegistry` from that
service's chosen discovery API.

## Shared implementation

- `DysonNetwork.Shared/Capabilities/ApiFeatureAttribute.cs`
- `DysonNetwork.Shared/Capabilities/ApiFeature.cs`
- `DysonNetwork.Shared/Capabilities/CapabilityRegistry.cs`

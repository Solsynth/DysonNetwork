# Endpoint Capabilities

## Overview

`DysonNetwork.Shared.Capabilities` provides a declarative way for a service to
advertise the features exposed by its HTTP API. A capability is declared as
endpoint metadata, so it stays with the controller action or Minimal API route
that exposes it.

The shared implementation collects this metadata once when the application
starts and exposes the result through `ICapabilityRegistry`. Every service that
uses `AddServiceDefaults()` also exposes the `DyCapabilitiesService`
`GetCapabilities` gRPC method. It does not add annotations to existing
endpoints or enforce authorization behavior.

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

Capability identifiers remain strings on endpoint metadata, but only the names
defined by the shared `DyCapability` protobuf enum are advertised externally:
`voice`, `passkeys`, `stories`, `drive-resumable`, and `realm-v2`. Add an enum
value to `Spec/proto/capability.proto` before annotating a new public
capability.

## Metadata fields

| Field | Meaning |
| --- | --- |
| `Capability` | Required capability identifier. |
| `Revision` | API revision that introduced the feature. `0` means unspecified. |
| `Experimental` | Whether clients should treat the feature as experimental. |

The registry also records the endpoint route pattern and display name alongside
each feature declaration.

## Registration and lifecycle

`AddServiceDefaults()` registers the collector and the capability gRPC service
for every DysonNetwork service. No per-service registration or mapping is
required.

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

## gRPC response

`DyCapabilitiesService.GetCapabilities(Empty)` returns the static metadata for
the service. `api_revision` defaults to the highest annotated capability
revision and can be explicitly configured. `minimum_revision` is configured
separately when a service has a compatibility floor.

```json
{
  "Capabilities": {
    "ApiRevision": 17,
    "MinimumRevision": 16
  }
}
```

Each advertised item contains an enum capability, `enabled`, `revision`,
`experimental`, and optional `version`. Features are descriptive only; they do
not grant, deny, or enforce access.

## Shared implementation

- `DysonNetwork.Shared/Capabilities/ApiFeatureAttribute.cs`
- `DysonNetwork.Shared/Capabilities/ApiFeature.cs`
- `DysonNetwork.Shared/Capabilities/CapabilityRegistry.cs`
- `DysonNetwork.Shared/Capabilities/CapabilityGrpcService.cs`

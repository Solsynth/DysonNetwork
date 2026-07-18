# Blade Service Discovery

## Overview

`DysonNetwork.Shared.Registry` provides an opt-in client for Blade's leased
service-discovery API. Registering a service creates a hosted worker that:

1. registers the instance with Blade;
2. renews its lease every third of the configured lease duration;
3. re-registers when Blade reports that the lease no longer exists; and
4. deregisters the instance during graceful shutdown.

The same registration also provides `IBladeServiceDiscoveryClient` for
resolving other services. Resolutions are cached locally for one second by
default and return cloned protobuf instances so callers cannot mutate the
cache.

It also supplies an ASP.NET service-discovery endpoint provider. Existing
clients using logical names such as `https://_grpc.sphere` resolve Blade's
healthy `sphere` gRPC instances automatically; `ServiceInjectionHelper` does
not need to change.

## Registration

`AddServiceDefaults()` registers Blade discovery for every DysonNetwork
service. Configure each deployment with the shared `Blade:ServiceDiscovery`
section:

```json
{
  "Blade": {
    "ServiceDiscovery": {
      "Endpoint": "http://blade:7005",
      "RegistrationToken": "replace-with-the-shared-registration-secret",
      "InstanceId": "sphere-7f4d8b9c-pod-1",
      "HttpEndpoint": "http://sphere-7f4d8b9c-pod-1:8000",
      "GrpcEndpoint": "sphere-7f4d8b9c-pod-1:7005",
      "Weight": 1,
      "LeaseSeconds": 30
    }
  }
}
```

`InstanceId` must be unique for each running replica and stable for its
lifetime. A pod UID or allocation ID is appropriate. Do not use only the
service name because replicas would overwrite one another.

`Service` defaults to the application's DysonNetwork service name without the
`DysonNetwork.` prefix, in lowercase. `InstanceId` defaults to a process-unique
value. `HttpEndpoint` and `GrpcEndpoint` default to the service name with its
configured `HTTP_PORTS` and `GRPC_PORT`. Production deployments should override
the endpoint values when those DNS names are not reachable from Blade.

`HttpEndpoint` must be an absolute URL because Blade probes
`<http_endpoint>/health`. At least one of `HttpEndpoint` or `GrpcEndpoint` is
required. A gRPC-only instance is resolvable with `healthyOnly: false`, but it
cannot become healthy until Blade has gRPC health probing.

The registration token is required. The extension validates its configuration
when each service starts.

## Resolution

Inject `IBladeServiceDiscoveryClient` to resolve healthy instances in normal
request paths:

```csharp
public sealed class RingClient(IBladeServiceDiscoveryClient discovery)
{
    public async Task<string> GetGrpcEndpointAsync(CancellationToken cancellationToken)
    {
        var instances = await discovery.ResolveAsync("ring", cancellationToken: cancellationToken);
        return instances.First().GrpcEndpoint;
    }
}
```

`ResolveAsync` uses `healthyOnly: true` by default. Requesting an unknown
service or resolving when no matching healthy instance exists returns an empty
list; transport and Blade errors are propagated to the caller.

## ASP.NET service discovery

The Blade endpoint provider handles ASP.NET's `grpc` and `http` endpoint names.
For example, `https://_grpc.ring` is parsed as the `grpc` endpoint of the
`ring` service, so Blade resolves `ring` and exposes each healthy
`grpc_endpoint` to the standard ASP.NET HTTP client and gRPC load-balancing
pipeline. Likewise, `http://_http.ring` resolves the registered
`http_endpoint`. Resolutions are refreshed when the short local cache expires.

Blade's `grpc_endpoint` and `http_endpoint` may be absolute URIs or
`host:port` pairs. For the latter, the logical client's requested scheme is
applied, so the existing `https://_grpc.*` clients use HTTPS.

## Authentication and failure behavior

The registration token is sent as `authorization: Bearer <token>` only for
`Register`, `Renew`, and `Deregister`. `Resolve` is unauthenticated.

Transient registration or renewal failures are logged and retried with an
exponential backoff starting at `RetryDelay` (five seconds by default) and
capped at 30 seconds. Renewal timing follows Blade's granted lease expiry. If
a service crashes, Blade's Redis-backed lease expiration removes the stale
instance.

Use TLS and internal networking for the Blade gRPC endpoint in production.

## Gateway capability document

When discovery is enabled, Blade exposes `GET /meta`. It reads the services
currently registered in Blade, queries one healthy gRPC instance of each
service through `DyCapabilitiesService.GetCapabilities`, and serves the
aggregated result from memory. The cache is refreshed at startup and every five
minutes, so normal `/meta` requests make no downstream gRPC calls.

```json
{
  "apiRevision": 17,
  "minimumRevision": 16,
  "features": {
    "voice": true,
    "drive-resumable": true
  },
  "capabilities": {
    "voice": {
      "enabled": true,
      "revision": 17
    }
  },
  "services": {
    "ring": {
      "apiRevision": 17,
      "minimumRevision": 16,
      "state": "up"
    }
  }
}
```

Blade marks a registered service `degraded` when it has no healthy gRPC
instance or its capability RPC cannot be read. It remains in the document so
clients and operators can distinguish absence from a temporarily unavailable
service.

## Shared implementation

- `DysonNetwork.Shared/Registry/BladeServiceDiscoveryOptions.cs`
- `DysonNetwork.Shared/Registry/BladeServiceDiscoveryClient.cs`
- `DysonNetwork.Shared/Registry/BladeServiceRegistrationService.cs`
- `DysonNetwork.Shared/Registry/BladeServiceDiscoveryExtensions.cs`

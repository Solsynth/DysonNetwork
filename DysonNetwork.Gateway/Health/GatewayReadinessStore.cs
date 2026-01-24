using NodaTime;

namespace DysonNetwork.Gateway.Health;

public record ServiceHealthState(
    string ServiceName,
    bool IsHealthy,
    Instant LastChecked,
    string? Error
);

public record GatewayReadinessState(
    bool IsReady,
    IReadOnlyDictionary<string, ServiceHealthState> Services,
    Instant LastUpdated
);

public class GatewayReadinessStore
{
    private readonly Lock _lock = new();

    private readonly Dictionary<string, ServiceHealthState> _services = new();

    public GatewayReadinessState Current { get; private set; } = new(
        IsReady: false,
        Services: new Dictionary<string, ServiceHealthState>(),
        LastUpdated: SystemClock.Instance.GetCurrentInstant()
    );

    public IReadOnlyCollection<string> ServiceNames => _services.Keys;

    public GatewayReadinessStore()
    {
        InitializeServices(GatewayConstant.ServiceNames);
    }

    /// <summary>
    /// Reinitializes the store with new service names from configuration.
    /// This method should be called when configuration changes.
    /// </summary>
    /// <param name="serviceNames">The new service names to track</param>
    public void ReinitializeServices(string[] serviceNames)
    {
        lock (_lock)
        {
            // Preserve existing health states for services that still exist
            var existingStates = new Dictionary<string, ServiceHealthState>(_services);

            _services.Clear();

            foreach (var name in serviceNames)
            {
                // Use existing state if available, otherwise create new unhealthy state
                if (existingStates.TryGetValue(name, out var existingState))
                {
                    _services[name] = existingState;
                }
                else
                {
                    _services[name] = new ServiceHealthState(
                        name,
                        IsHealthy: false,
                        LastChecked: SystemClock.Instance.GetCurrentInstant(),
                        Error: "Not checked yet"
                    );
                }
            }

            RecalculateLocked();
        }
    }

    private void InitializeServices(IEnumerable<string> serviceNames)
    {
        lock (_lock)
        {
            _services.Clear();

            foreach (var name in serviceNames)
            {
                _services[name] = new ServiceHealthState(
                    name,
                    IsHealthy: false,
                    LastChecked: SystemClock.Instance.GetCurrentInstant(),
                    Error: "Not checked yet"
                );
            }

            RecalculateLocked();
        }
    }

    public void Update(ServiceHealthState state)
    {
        lock (_lock)
        {
            _services[state.ServiceName] = state;
            RecalculateLocked();
        }
    }

    private void RecalculateLocked()
    {
        var isReady = _services.Count > 0 && _services.Values.All(s => s.IsHealthy);

        Current = new GatewayReadinessState(
            IsReady: isReady,
            Services: new Dictionary<string, ServiceHealthState>(_services),
            LastUpdated: SystemClock.Instance.GetCurrentInstant()
        );
    }
}
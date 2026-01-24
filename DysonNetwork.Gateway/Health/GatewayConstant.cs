namespace DysonNetwork.Gateway.Health;

public abstract class GatewayConstant
{
    // Default service names used when no configuration is provided
    private static readonly string[] DefaultServiceNames =
    [
        "ring",
        "pass",
        "drive",
        "sphere",
        "develop",
        "insight",
        "zone",
        "messager"
    ];

    // Default core service names used when no configuration is provided
    private static readonly string[] DefaultCoreServiceNames =
    [
        "ring",
        "pass",
        "drive",
        "sphere"
    ];

    // Configuration-driven service names with fallback to defaults
    public static string[] ServiceNames { get; private set; } = DefaultServiceNames;

    // Configuration-driven core service names with fallback to defaults
    public static string[] CoreServiceNames { get; private set; } = DefaultCoreServiceNames;

    /// <summary>
    /// Initializes the service names from configuration options.
    /// This method should be called during application startup.
    /// </summary>
    /// <param name="options">The gateway endpoints options containing configuration</param>
    public static void InitializeFromConfiguration(DysonNetwork.Gateway.Configuration.GatewayEndpointsOptions options)
    {
        ServiceNames = options.GetServiceNames();
        CoreServiceNames = options.GetCoreServiceNames();
    }

    /// <summary>
    /// Resets the service names to their default values.
    /// Useful for testing or when configuration is not available.
    /// </summary>
    public static void ResetToDefaults()
    {
        ServiceNames = DefaultServiceNames;
        CoreServiceNames = DefaultCoreServiceNames;
    }
}

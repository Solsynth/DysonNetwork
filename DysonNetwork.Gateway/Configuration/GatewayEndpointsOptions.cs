namespace DysonNetwork.Gateway.Configuration;

public class GatewayEndpointsOptions
{
    public const string SectionName = "Endpoints";

    /// <summary>
    /// List of all services that the gateway should manage.
    /// If not specified, defaults to the built-in service list.
    /// </summary>
    public List<string>? ServiceNames { get; set; }

    /// <summary>
    /// List of core services that are essential for the application to function.
    /// If not specified, defaults to the built-in core service list.
    /// </summary>
    public List<string>? CoreServiceNames { get; set; }

    /// <summary>
    /// Default service names used when no configuration is provided.
    /// </summary>
    public static readonly string[] DefaultServiceNames =
    [
        "ring",
        "pass",
        "drive",
        "sphere",
        "develop",
        "insight",
        "zone",
        "messager",
        "wallet"
    ];

    /// <summary>
    /// Default core service names used when no configuration is provided.
    /// </summary>
    public static readonly string[] DefaultCoreServiceNames =
    [
        "ring",
        "pass",
        "drive",
        "sphere",
        "wallet"
    ];

    /// <summary>
    /// Gets the effective service names, using configuration if available, otherwise defaults.
    /// </summary>
    public string[] GetServiceNames() => ServiceNames?.ToArray() ?? DefaultServiceNames;

    /// <summary>
    /// Gets the effective core service names, using configuration if available, otherwise defaults.
    /// </summary>
    public string[] GetCoreServiceNames() => CoreServiceNames?.ToArray() ?? DefaultCoreServiceNames;
}
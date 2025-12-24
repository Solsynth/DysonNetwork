namespace DysonNetwork.Gateway.Health;

public abstract class GatewayConstant
{
    public static readonly string[] ServiceNames = ["ring", "pass", "drive", "sphere", "develop", "insight", "zone"];
    
    // Core services stands with w/o these services the functional of entire app will broke.
    public static readonly string[] CoreServiceNames = ["ring", "pass", "drive", "sphere"];
}
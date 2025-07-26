namespace DysonNetwork.Shared.Auth;

public static class PerkSubscriptionPrivilege
{
    public static int GetPrivilegeFromIdentifier(string identifier)
    {
        // Reference from the DysonNetwork.Pass
        return identifier switch
        {
            "solian.stellar.primary" => 1,
            "solian.stellar.nova" => 2,
            "solian.stellar.supernova" => 3,
            _ => 0
        };
    }
}
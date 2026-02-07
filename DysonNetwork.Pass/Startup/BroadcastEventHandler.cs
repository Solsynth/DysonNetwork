using DysonNetwork.Shared.Models;

namespace DysonNetwork.Pass.Startup;

public static class BroadcastEventHandler
{
    public static bool StatusesEqual(SnAccountStatus a, SnAccountStatus b)
    {
        return a.Attitude == b.Attitude &&
               a.IsOnline == b.IsOnline &&
               a.IsCustomized == b.IsCustomized &&
               a.Label == b.Label &&
               a.IsInvisible == b.IsInvisible &&
               a.IsNotDisturb == b.IsNotDisturb;
    }
}

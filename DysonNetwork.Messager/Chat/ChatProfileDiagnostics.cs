using DysonNetwork.Shared.Models;

namespace DysonNetwork.Messager.Chat;

/// <summary>
/// Diagnostics for chat responses whose account lookup did not resolve or
/// whose resolved account has no profile. Profiles are optional: bare
/// profiles and profiles without visual identity are valid and are returned.
/// </summary>
public static class ChatProfileDiagnostics
{
    public static void LogIncompleteProfile(ILogger logger, SnAccount? account, string route)
    {
        if (account is null)
        {
            logger.LogWarning(
                "ChatProfileDiagnostics: account lookup returned no account on route {Route}",
                route
            );
            return;
        }

        if (account.Profile is null)
        {
            logger.LogDebug(
                "ChatProfileDiagnostics: account {AccountId} ({Nick}) has no profile on route {Route}; " +
                "the account remains valid",
                account.Id,
                account.Nick,
                route
            );
        }
    }
}

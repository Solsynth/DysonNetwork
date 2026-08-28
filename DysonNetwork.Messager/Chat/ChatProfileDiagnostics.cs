using DysonNetwork.Shared.Models;

namespace DysonNetwork.Messager.Chat;

/// <summary>
/// Diagnostics for chat responses whose account lookup did not resolve or
/// whose resolved account has no profile. A missing account is expected
/// for members whose account was deleted (historic rows are retained for
/// message history), so it is logged at Debug, not Warning. Profiles are
/// optional: bare profiles and profiles without visual identity are valid
/// and are returned.
/// </summary>
public static class ChatProfileDiagnostics
{
    public static void LogIncompleteProfile(ILogger logger, SnAccount? account, string route)
    {
        if (account is null)
        {
            logger.LogDebug(
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

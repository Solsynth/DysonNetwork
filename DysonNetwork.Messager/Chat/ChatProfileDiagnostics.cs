using DysonNetwork.Shared.Models;

namespace DysonNetwork.Messager.Chat;

/// <summary>
/// Diagnostics for chat responses carrying accounts whose profile data is
/// incomplete (null profile, bare fallback shell, or missing visual identity).
/// Every route that attaches an account to a message/member payload logs
/// through here so a data-less profile can be traced back to the route and
/// account that produced it.
/// </summary>
public static class ChatProfileDiagnostics
{
    public static void LogIncompleteProfile(ILogger logger, SnAccount? account, string route)
    {
        if (account is null)
        {
            logger.LogWarning(
                "ChatProfileDiagnostics: attached account is NULL on route {Route}",
                route
            );
            return;
        }

        var profile = account.Profile;
        if (profile is null)
        {
            logger.LogWarning(
                "ChatProfileDiagnostics: account {AccountId} ({Nick}) has a NULL profile on route {Route}",
                account.Id,
                account.Nick,
                route
            );
            return;
        }

        if (profile.IsBare)
        {
            logger.LogWarning(
                "ChatProfileDiagnostics: account {AccountId} ({Nick}) has a BARE profile " +
                "(no name/bio/picture) on route {Route}",
                account.Id,
                account.Nick,
                route
            );
            return;
        }

        var noVisuals = profile.Picture is null && profile.Background is null && profile.UsernameColor is null;
        if (noVisuals)
        {
            logger.LogWarning(
                "ChatProfileDiagnostics: account {AccountId} ({Nick}) profile missing visual " +
                "identity (picture={HasPicture}, background={HasBackground}, usernameColor={HasUsernameColor}) " +
                "on route {Route}",
                account.Id,
                account.Nick,
                profile.Picture is not null,
                profile.Background is not null,
                profile.UsernameColor is not null,
                route
            );
        }
    }
}

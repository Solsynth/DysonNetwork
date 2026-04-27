using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;

namespace DysonNetwork.Shared.Auth;

public static class PermissionScopeGate
{
    private static readonly HashSet<string> PresetScopes = new(StringComparer.OrdinalIgnoreCase)
    {
        "openid",
        "profile",
        "email",
        "*"
    };

    public static bool ShouldEnforcePermissionScope(DyAuthSession? session)
    {
        return session is not null && session.Type == DySessionType.DyOauth;
    }

    public static bool ShouldEnforcePermissionScope(SnAuthSession? session)
    {
        return session is not null && session.Type == SessionType.OAuth;
    }

    public static bool HasFullScope(DyAuthSession? session)
    {
        return session is not null && session.Scopes.Contains("*");
    }

    public static bool HasFullScope(SnAuthSession? session)
    {
        return session is not null && session.Scopes.Contains("*");
    }

    public static bool IsPermissionEnabled(IEnumerable<string> scopes, string permissionKey)
    {
        return GetMatchedPermissionScope(scopes, permissionKey) is not null;
    }

    public static string? GetMatchedPermissionScope(IEnumerable<string> scopes, string permissionKey)
    {
        if (string.IsNullOrWhiteSpace(permissionKey))
            return null;

        foreach (var rawScope in scopes)
        {
            var scope = rawScope?.Trim();
            if (string.IsNullOrWhiteSpace(scope))
                continue;

            if (string.Equals(scope, "*", StringComparison.OrdinalIgnoreCase))
                return "*";

            if (PresetScopes.Contains(scope))
                continue;

            if (string.Equals(scope, permissionKey, StringComparison.OrdinalIgnoreCase))
                return scope;

            if (scope.EndsWith(".*", StringComparison.OrdinalIgnoreCase))
            {
                var prefix = scope[..^1];
                if (permissionKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return scope;
            }
        }

        return null;
    }
}

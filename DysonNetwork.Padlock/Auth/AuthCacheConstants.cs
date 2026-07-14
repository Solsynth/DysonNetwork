namespace DysonNetwork.Padlock.Auth;

public static class AuthCacheConstants
{
    public const string SessionPrefix = "auth:session:";
    public static string Session(string sessionId) => $"{SessionPrefix}{sessionId}";
    public const string SessionTokensGroupPrefix = "auth:session_tokens:";
    public static string SessionTokensGroup(string sessionId) => $"{SessionTokensGroupPrefix}{sessionId}";
}

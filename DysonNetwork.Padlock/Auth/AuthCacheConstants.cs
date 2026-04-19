namespace DysonNetwork.Padlock.Auth;

public static class AuthCacheConstants
{
    public const string SessionPrefix = "auth:session:";
    public static string Session(string sessionId) => $"{SessionPrefix}{sessionId}";
}

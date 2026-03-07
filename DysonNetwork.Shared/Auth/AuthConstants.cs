namespace DysonNetwork.Shared.Auth;

public static class AuthConstants
{
    public const string SchemeName = "DysonToken";
    public const string TokenQueryParamName = "tk";
    public const string CookieTokenName = "AuthToken";
    public const string RefreshCookieTokenName = "RefreshToken";
    public const string UserHeaderScheme = "Bearer";
    public const string BotHeaderScheme = "Bot";
}

public enum TokenType
{
    AuthKey,
    ApiKey,
    OidcKey,
    Unknown
}

public class TokenInfo
{
    public string Token { get; set; } = string.Empty;
    public TokenType Type { get; set; } = TokenType.Unknown;
}

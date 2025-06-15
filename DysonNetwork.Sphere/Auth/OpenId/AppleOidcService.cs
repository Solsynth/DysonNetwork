using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.IdentityModel.Tokens;

namespace DysonNetwork.Sphere.Auth.OpenId;

/// <summary>
/// Implementation of OpenID Connect service for Apple Sign In
/// </summary>
public class AppleOidcService(
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    AppDatabase db
)
    : OidcService(configuration, httpClientFactory, db)
{
    private readonly IConfiguration _configuration = configuration;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;

    public override string ProviderName => "apple";
    protected override string DiscoveryEndpoint => "https://appleid.apple.com/.well-known/openid-configuration";
    protected override string ConfigSectionName => "Apple";

    public override string GetAuthorizationUrl(string state, string nonce)
    {
        var config = GetProviderConfig();

        var queryParams = new Dictionary<string, string>
        {
            { "client_id", config.ClientId },
            { "redirect_uri", config.RedirectUri },
            { "response_type", "code id_token" },
            { "scope", "name email" },
            { "response_mode", "form_post" },
            { "state", state },
            { "nonce", nonce }
        };

        var queryString = string.Join("&", queryParams.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
        return $"https://appleid.apple.com/auth/authorize?{queryString}";
    }

    public override async Task<OidcUserInfo> ProcessCallbackAsync(OidcCallbackData callbackData)
    {
        // Verify and decode the id_token
        var userInfo = await ValidateTokenAsync(callbackData.IdToken);

        // If user data is provided in first login, parse it
        if (!string.IsNullOrEmpty(callbackData.RawData))
        {
            var userData = JsonSerializer.Deserialize<AppleUserData>(callbackData.RawData);
            if (userData?.Name != null)
            {
                userInfo.FirstName = userData.Name.FirstName ?? "";
                userInfo.LastName = userData.Name.LastName ?? "";
                userInfo.DisplayName = $"{userInfo.FirstName} {userInfo.LastName}".Trim();
            }
        }

        // Exchange authorization code for access token (optional, if you need the access token)
        if (string.IsNullOrEmpty(callbackData.Code)) return userInfo;
        var tokenResponse = await ExchangeCodeForTokensAsync(callbackData.Code);
        if (tokenResponse == null) return userInfo;
        userInfo.AccessToken = tokenResponse.AccessToken;
        userInfo.RefreshToken = tokenResponse.RefreshToken;

        return userInfo;
    }

    private async Task<OidcUserInfo> ValidateTokenAsync(string idToken)
    {
        // Get Apple's public keys
        var jwksJson = await GetAppleJwksAsync();
        var jwks = JsonSerializer.Deserialize<AppleJwks>(jwksJson) ?? new AppleJwks { Keys = new List<AppleKey>() };

        // Parse the JWT header to get the key ID
        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(idToken);
        var kid = jwtToken.Header.Kid;

        // Find the matching key
        var key = jwks.Keys.FirstOrDefault(k => k.Kid == kid);
        if (key == null)
        {
            throw new SecurityTokenValidationException("Unable to find matching key in Apple's JWKS");
        }

        // Create the validation parameters
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "https://appleid.apple.com",
            ValidateAudience = true,
            ValidAudience = GetProviderConfig().ClientId,
            ValidateLifetime = true,
            IssuerSigningKey = key.ToSecurityKey()
        };

        return ValidateAndExtractIdToken(idToken, validationParameters);
    }

    protected override Dictionary<string, string> BuildTokenRequestParameters(string code, ProviderConfiguration config,
        string? codeVerifier)
    {
        var parameters = new Dictionary<string, string>
        {
            { "client_id", config.ClientId },
            { "client_secret", GenerateClientSecret() },
            { "code", code },
            { "grant_type", "authorization_code" },
            { "redirect_uri", config.RedirectUri }
        };

        return parameters;
    }

    private async Task<string> GetAppleJwksAsync()
    {
        var client = _httpClientFactory.CreateClient();
        var response = await client.GetAsync("https://appleid.apple.com/auth/keys");
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// Generates a client secret for Apple Sign In using JWT
    /// </summary>
    private string GenerateClientSecret()
    {
        var now = DateTime.UtcNow;
        var teamId = _configuration["Oidc:Apple:TeamId"];
        var clientId = _configuration["Oidc:Apple:ClientId"];
        var keyId = _configuration["Oidc:Apple:KeyId"];
        var privateKeyPath = _configuration["Oidc:Apple:PrivateKeyPath"];

        // Read the private key
        var privateKey = File.ReadAllText(privateKeyPath!);

        // Create the JWT header
        var header = new Dictionary<string, object>
        {
            { "alg", "ES256" },
            { "kid", keyId }
        };

        // Create the JWT payload
        var payload = new Dictionary<string, object>
        {
            { "iss", teamId },
            { "iat", ToUnixTimeSeconds(now) },
            { "exp", ToUnixTimeSeconds(now.AddMinutes(5)) },
            { "aud", "https://appleid.apple.com" },
            { "sub", clientId }
        };

        // Convert header and payload to Base64Url
        var headerJson = JsonSerializer.Serialize(header);
        var payloadJson = JsonSerializer.Serialize(payload);
        var headerBase64 = Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));
        var payloadBase64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));

        // Create the signature
        var dataToSign = $"{headerBase64}.{payloadBase64}";
        var signature = SignWithECDsa(dataToSign, privateKey);

        // Combine all parts
        return $"{headerBase64}.{payloadBase64}.{signature}";
    }

    private long ToUnixTimeSeconds(DateTime dateTime)
    {
        return new DateTimeOffset(dateTime).ToUnixTimeSeconds();
    }

    private string SignWithECDsa(string dataToSign, string privateKey)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(privateKey);

        var bytes = Encoding.UTF8.GetBytes(dataToSign);
        var signature = ecdsa.SignData(bytes, HashAlgorithmName.SHA256);

        return Base64UrlEncode(signature);
    }

    private string Base64UrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}

public class AppleUserData
{
    [JsonPropertyName("name")] public AppleNameData? Name { get; set; }

    [JsonPropertyName("email")] public string? Email { get; set; }
}

public class AppleNameData
{
    [JsonPropertyName("firstName")] public string? FirstName { get; set; }

    [JsonPropertyName("lastName")] public string? LastName { get; set; }
}

public class AppleJwks
{
    [JsonPropertyName("keys")] public List<AppleKey> Keys { get; set; } = new List<AppleKey>();
}

public class AppleKey
{
    [JsonPropertyName("kty")] public string? Kty { get; set; }

    [JsonPropertyName("kid")] public string? Kid { get; set; }

    [JsonPropertyName("use")] public string? Use { get; set; }

    [JsonPropertyName("alg")] public string? Alg { get; set; }

    [JsonPropertyName("n")] public string? N { get; set; }

    [JsonPropertyName("e")] public string? E { get; set; }

    public SecurityKey ToSecurityKey()
    {
        if (Kty != "RSA" || string.IsNullOrEmpty(N) || string.IsNullOrEmpty(E))
        {
            throw new InvalidOperationException("Invalid key data");
        }

        var parameters = new RSAParameters
        {
            Modulus = Base64UrlDecode(N),
            Exponent = Base64UrlDecode(E)
        };

        var rsa = RSA.Create();
        rsa.ImportParameters(parameters);

        return new RsaSecurityKey(rsa);
    }

    private byte[] Base64UrlDecode(string input)
    {
        var output = input
            .Replace('-', '+')
            .Replace('_', '/');

        switch (output.Length % 4)
        {
            case 0: break;
            case 2: output += "=="; break;
            case 3: output += "="; break;
            default: throw new InvalidOperationException("Invalid base64url string");
        }

        return Convert.FromBase64String(output);
    }
}
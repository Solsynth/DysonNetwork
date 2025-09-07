using System.Text.Json;
using System.Text.Json.Serialization;

namespace DysonNetwork.Pass.Auth.OpenId;

/// <summary>
/// Represents the state parameter used in OpenID Connect flows.
/// Handles serialization and deserialization of the state parameter.
/// </summary>
public class OidcState
{
    /// <summary>
    /// The type of OIDC flow (login or connect).
    /// </summary>
    public OidcFlowType FlowType { get; set; }

    /// <summary>
    /// The account ID (for connect flow).
    /// </summary>
    public Guid? AccountId { get; set; }


    /// <summary>
    /// The OIDC provider name.
    /// </summary>
    public string? Provider { get; set; }


    /// <summary>
    /// The nonce for CSRF protection.
    /// </summary>
    public string? Nonce { get; set; }


    /// <summary>
    /// The device ID for the authentication request.
    /// </summary>
    public string? DeviceId { get; set; }


    /// <summary>
    /// The return URL after authentication (for login flow).
    /// </summary>
    public string? ReturnUrl { get; set; }


    /// <summary>
    /// Creates a new OidcState for a connection flow.
    /// </summary>
    public static OidcState ForConnection(Guid accountId, string provider, string nonce, string? deviceId = null)
    {
        return new OidcState
        {
            FlowType = OidcFlowType.Connect,
            AccountId = accountId,
            Provider = provider,
            Nonce = nonce,
            DeviceId = deviceId
        };
    }

    /// <summary>
    /// Creates a new OidcState for a login flow.
    /// </summary>
    public static OidcState ForLogin(string returnUrl = "/", string? deviceId = null)
    {
        return new OidcState
        {
            FlowType = OidcFlowType.Login,
            ReturnUrl = returnUrl,
            DeviceId = deviceId
        };
    }

    /// <summary>
    /// The version of the state format.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Serializes the state to a JSON string for use in OIDC flows.
    /// </summary>
    public string Serialize()
    {
        return JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
    }

    /// <summary>
    /// Attempts to parse a state string into an OidcState object.
    /// </summary>
    public static bool TryParse(string? stateString, out OidcState? state)
    {
        state = null;

        if (string.IsNullOrEmpty(stateString))
            return false;

        try
        {
            // First try to parse as JSON
            try
            {
                state = JsonSerializer.Deserialize<OidcState>(stateString);
                return state != null;
            }
            catch (JsonException)
            {
                // Not a JSON string, try legacy format for backward compatibility
                return TryParseLegacyFormat(stateString, out state);
            }
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParseLegacyFormat(string stateString, out OidcState? state)
    {
        state = null;
        var parts = stateString.Split('|');

        // Check for connection flow format: {accountId}|{provider}|{nonce}|{deviceId}|connect
        if (parts.Length >= 5 &&
            Guid.TryParse(parts[0], out var accountId) &&
            string.Equals(parts[^1], "connect", StringComparison.OrdinalIgnoreCase))
        {
            state = new OidcState
            {
                FlowType = OidcFlowType.Connect,
                AccountId = accountId,
                Provider = parts[1],
                Nonce = parts[2],
                DeviceId = parts.Length >= 4 && !string.IsNullOrEmpty(parts[3]) ? parts[3] : null
            };
            return true;
        }

        // Check for login flow format: {returnUrl}|{deviceId}|login
        if (parts.Length >= 2 &&
            parts.Length <= 3 &&
            (parts.Length < 3 || string.Equals(parts[^1], "login", StringComparison.OrdinalIgnoreCase)))
        {
            state = new OidcState
            {
                FlowType = OidcFlowType.Login,
                ReturnUrl = parts[0],
                DeviceId = parts.Length >= 2 && !string.IsNullOrEmpty(parts[1]) ? parts[1] : null
            };
            return true;
        }

        // Legacy format support (for backward compatibility)
        if (parts.Length == 1)
        {
            state = new OidcState
            {
                FlowType = OidcFlowType.Login,
                ReturnUrl = parts[0],
                DeviceId = null
            };
            return true;
        }


        return false;
    }
}

/// <summary>
/// Represents the type of OIDC flow.
/// </summary>
public enum OidcFlowType
{
    /// <summary>
    /// Login or registration flow.
    /// </summary>
    Login,


    /// <summary>
    /// Account connection flow.
    /// </summary>
    Connect
}
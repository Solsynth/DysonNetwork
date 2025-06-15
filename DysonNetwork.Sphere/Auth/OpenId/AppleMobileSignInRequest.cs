
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace DysonNetwork.Sphere.Auth.OpenId;

public class AppleMobileConnectRequest
{
    [Required]
    public required string IdentityToken { get; set; }
    [Required]
    public required string AuthorizationCode { get; set; }
}

public class AppleMobileSignInRequest : AppleMobileConnectRequest
{
    [Required]
    public required string DeviceId { get; set; }
}

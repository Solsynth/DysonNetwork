
using System.ComponentModel.DataAnnotations;

namespace DysonNetwork.Pass.Auth.OpenId;

public class AppleMobileConnectRequest
{
    [Required]
    public required string IdentityToken { get; set; }
    [Required]
    public required string AuthorizationCode { get; set; }
}

public class AppleMobileSignInRequest : AppleMobileConnectRequest
{
    [Required] [MaxLength(512)]
    public required string DeviceId { get; set; }
    [MaxLength(1024)] public string? DeviceName { get; set; }
}

using NodaTime;

namespace DysonNetwork.Padlock.Auth.OidcProvider.Models;

public class DeviceCodeInfo
{
    public string DeviceCode { get; set; } = string.Empty;
    public string UserCode { get; set; } = string.Empty;
    public Guid ClientId { get; set; }
    public Guid? AccountId { get; set; }
    public List<string> Scopes { get; set; } = [];
    public string? Nonce { get; set; }
    public DeviceCodeStatus Status { get; set; } = DeviceCodeStatus.Pending;
    public Instant CreatedAt { get; set; }
    public Instant ExpiresAt { get; set; }
    public Instant? ApprovedAt { get; set; }
    public Guid? ApprovedBySessionId { get; set; }
}

public enum DeviceCodeStatus
{
    Pending,
    Approved,
    Declined,
    Expired
}

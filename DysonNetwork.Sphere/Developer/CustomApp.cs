using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using NodaTime;

namespace DysonNetwork.Sphere.Developer;

public enum CustomAppStatus
{
    Developing,
    Staging,
    Production,
    Suspended
}

public class CustomApp : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(1024)] public string Slug { get; set; } = null!;
    [MaxLength(1024)] public string Name { get; set; } = null!;
    public CustomAppStatus Status { get; set; } = CustomAppStatus.Developing;
    public Instant? VerifiedAt { get; set; }
    [MaxLength(4096)] public string? VerifiedAs { get; set; }
    
    // OIDC/OAuth specific properties
    [MaxLength(4096)] public string? LogoUri { get; set; }
    [MaxLength(1024)] public string? ClientUri { get; set; }
    [MaxLength(4096)] public string[] RedirectUris { get; set; } = [];
    [MaxLength(4096)] public string[]? PostLogoutRedirectUris { get; set; }
    [MaxLength(256)] public string[]? AllowedScopes { get; set; } = ["openid", "profile", "email"];
    [MaxLength(256)] public string[] AllowedGrantTypes { get; set; } = ["authorization_code", "refresh_token"];
    public bool RequirePkce { get; set; } = true;
    public bool AllowOfflineAccess { get; set; } = false;
    
    [JsonIgnore] public ICollection<CustomAppSecret> Secrets { get; set; } = new List<CustomAppSecret>();

    public Guid PublisherId { get; set; }
    public Publisher.Publisher Developer { get; set; } = null!;
}

public class CustomAppSecret : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(1024)] public string Secret { get; set; } = null!;
    [MaxLength(4096)] public string? Description { get; set; } = null!;
    public Instant? ExpiredAt { get; set; }
    public bool IsOidc { get; set; } = false; // Indicates if this secret is for OIDC/OAuth
    
    public Guid AppId { get; set; }
    public CustomApp App { get; set; } = null!;
}
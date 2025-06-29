using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using DysonNetwork.Sphere.Account;
using DysonNetwork.Sphere.Storage;
using NodaTime;

namespace DysonNetwork.Sphere.Developer;

public enum CustomAppStatus
{
    Developing,
    Staging,
    Production,
    Suspended
}

public class CustomApp : ModelBase, IIdentifiedResource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(1024)] public string Slug { get; set; } = null!;
    [MaxLength(1024)] public string Name { get; set; } = null!;
    [MaxLength(4096)] public string? Description { get; set; }
    public CustomAppStatus Status { get; set; } = CustomAppStatus.Developing;

    [Column(TypeName = "jsonb")] public CloudFileReferenceObject? Picture { get; set; }
    [Column(TypeName = "jsonb")] public CloudFileReferenceObject? Background { get; set; }

    [Column(TypeName = "jsonb")] public VerificationMark? Verification { get; set; }
    [Column(TypeName = "jsonb")] public CustomAppOauthConfig? OauthConfig { get; set; }
    [Column(TypeName = "jsonb")] public CustomAppLinks? Links { get; set; }

    [JsonIgnore] public ICollection<CustomAppSecret> Secrets { get; set; } = new List<CustomAppSecret>();

    public Guid PublisherId { get; set; }
    public Publisher.Publisher Developer { get; set; } = null!;

    [NotMapped] public string ResourceIdentifier => "custom-app/" + Id;
}

public class CustomAppLinks : ModelBase
{
    [MaxLength(8192)] public string? HomePage { get; set; }
    [MaxLength(8192)] public string? PrivacyPolicy { get; set; }
    [MaxLength(8192)] public string? TermsOfService { get; set; }
}

public class CustomAppOauthConfig : ModelBase
{
    [MaxLength(1024)] public string? ClientUri { get; set; }
    [MaxLength(4096)] public string[] RedirectUris { get; set; } = [];
    [MaxLength(4096)] public string[]? PostLogoutRedirectUris { get; set; }
    [MaxLength(256)] public string[]? AllowedScopes { get; set; } = ["openid", "profile", "email"];
    [MaxLength(256)] public string[] AllowedGrantTypes { get; set; } = ["authorization_code", "refresh_token"];
    public bool RequirePkce { get; set; } = true;
    public bool AllowOfflineAccess { get; set; } = false;
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
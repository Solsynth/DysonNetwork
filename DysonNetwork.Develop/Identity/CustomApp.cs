using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Proto;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using NodaTime.Serialization.Protobuf;
using NodaTime;

using DysonNetwork.Shared.Proto;
using Google.Protobuf.WellKnownTypes;
using NodaTime.Serialization.Protobuf;
using VerificationMark = DysonNetwork.Shared.Data.VerificationMark;

namespace DysonNetwork.Develop.Identity;

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

    [Column(TypeName = "jsonb")] public DysonNetwork.Shared.Data.VerificationMark? Verification { get; set; }
    [Column(TypeName = "jsonb")] public CustomAppOauthConfig? OauthConfig { get; set; }
    [Column(TypeName = "jsonb")] public CustomAppLinks? Links { get; set; }

    [JsonIgnore] public ICollection<CustomAppSecret> Secrets { get; set; } = new List<CustomAppSecret>();

    public Guid DeveloperId { get; set; }
    public Developer Developer { get; set; } = null!;

    [NotMapped] public string ResourceIdentifier => "custom-app:" + Id;

    public Shared.Proto.CustomApp ToProto()
    {
        return new Shared.Proto.CustomApp
        {
            Id = Id.ToString(),
            Slug = Slug,
            Name = Name,
            Description = Description ?? string.Empty,
            Status = Status switch
            {
                CustomAppStatus.Developing => Shared.Proto.CustomAppStatus.Developing,
                CustomAppStatus.Staging => Shared.Proto.CustomAppStatus.Staging,
                CustomAppStatus.Production => Shared.Proto.CustomAppStatus.Production,
                CustomAppStatus.Suspended => Shared.Proto.CustomAppStatus.Suspended,
                _ => Shared.Proto.CustomAppStatus.Unspecified
            },
            Picture = Picture is null ? ByteString.Empty : ByteString.CopyFromUtf8(System.Text.Json.JsonSerializer.Serialize(Picture)),
            Background = Background is null ? ByteString.Empty : ByteString.CopyFromUtf8(System.Text.Json.JsonSerializer.Serialize(Background)),
            Verification = Verification is null ? ByteString.Empty : ByteString.CopyFromUtf8(System.Text.Json.JsonSerializer.Serialize(Verification)),
            Links = Links is null ? ByteString.Empty : ByteString.CopyFromUtf8(System.Text.Json.JsonSerializer.Serialize(Links)),
            OauthConfig = OauthConfig is null ? null : new DysonNetwork.Shared.Proto.CustomAppOauthConfig
            {
                ClientUri = OauthConfig.ClientUri ?? string.Empty,
                RedirectUris = { OauthConfig.RedirectUris ?? Array.Empty<string>() },
                PostLogoutRedirectUris = { OauthConfig.PostLogoutRedirectUris ?? Array.Empty<string>() },
                AllowedScopes = { OauthConfig.AllowedScopes ?? Array.Empty<string>() },
                AllowedGrantTypes = { OauthConfig.AllowedGrantTypes ?? Array.Empty<string>() },
                RequirePkce = OauthConfig.RequirePkce,
                AllowOfflineAccess = OauthConfig.AllowOfflineAccess
            },
            DeveloperId = DeveloperId.ToString(),
            CreatedAt = CreatedAt.ToTimestamp(),
            UpdatedAt = UpdatedAt.ToTimestamp()
        };
    }

    public CustomApp FromProtoValue(Shared.Proto.CustomApp p)
    {
        Id = Guid.Parse(p.Id);
        Slug = p.Slug;
        Name = p.Name;
        Description = string.IsNullOrEmpty(p.Description) ? null : p.Description;
        Status = p.Status switch
        {
            Shared.Proto.CustomAppStatus.Developing => CustomAppStatus.Developing,
            Shared.Proto.CustomAppStatus.Staging => CustomAppStatus.Staging,
            Shared.Proto.CustomAppStatus.Production => CustomAppStatus.Production,
            Shared.Proto.CustomAppStatus.Suspended => CustomAppStatus.Suspended,
            _ => CustomAppStatus.Developing
        };
        DeveloperId = string.IsNullOrEmpty(p.DeveloperId) ? Guid.Empty : Guid.Parse(p.DeveloperId);
        CreatedAt = p.CreatedAt.ToInstant();
        UpdatedAt = p.UpdatedAt.ToInstant();
        if (p.Picture.Length > 0) Picture = System.Text.Json.JsonSerializer.Deserialize<CloudFileReferenceObject>(p.Picture.ToStringUtf8());
        if (p.Background.Length > 0) Background = System.Text.Json.JsonSerializer.Deserialize<CloudFileReferenceObject>(p.Background.ToStringUtf8());
        if (p.Verification.Length > 0) Verification = System.Text.Json.JsonSerializer.Deserialize<DysonNetwork.Shared.Data.VerificationMark>(p.Verification.ToStringUtf8());
        if (p.Links.Length > 0) Links = System.Text.Json.JsonSerializer.Deserialize<CustomAppLinks>(p.Links.ToStringUtf8());
        return this;
    }
}

public class CustomAppLinks
{
    [MaxLength(8192)] public string? HomePage { get; set; }
    [MaxLength(8192)] public string? PrivacyPolicy { get; set; }
    [MaxLength(8192)] public string? TermsOfService { get; set; }
}

public class CustomAppOauthConfig
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
    
    
    public static CustomAppSecret FromProtoValue(DysonNetwork.Shared.Proto.CustomAppSecret p)
    {
        return new CustomAppSecret
        {
            Id = Guid.Parse(p.Id),
            Secret = p.Secret,
            Description = p.Description,
            ExpiredAt = p.ExpiredAt?.ToInstant(),
            IsOidc = p.IsOidc,
            AppId = Guid.Parse(p.AppId),
        };
    }

    public DysonNetwork.Shared.Proto.CustomAppSecret ToProto()
    {
        return new DysonNetwork.Shared.Proto.CustomAppSecret
        {
            Id = Id.ToString(),
            Secret = Secret,
            Description = Description,
            ExpiredAt = ExpiredAt?.ToTimestamp(),
            IsOidc = IsOidc,
            AppId = Id.ToString(),
        };
    }
}
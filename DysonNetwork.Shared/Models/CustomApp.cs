using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Google.Protobuf.WellKnownTypes;
using NodaTime.Serialization.Protobuf;
using NodaTime;

namespace DysonNetwork.Shared.Models;

public enum CustomAppStatus
{
    Developing,
    Staging,
    Production,
    Suspended
}

public class SnCustomApp : ModelBase, IIdentifiedResource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(1024)] public string Slug { get; set; } = null!;
    [MaxLength(1024)] public string Name { get; set; } = null!;
    [MaxLength(4096)] public string? Description { get; set; }
    public CustomAppStatus Status { get; set; } = CustomAppStatus.Developing;

    [Column(TypeName = "jsonb")] public SnCloudFileReferenceObject? Picture { get; set; }
    [Column(TypeName = "jsonb")] public SnCloudFileReferenceObject? Background { get; set; }

    [Column(TypeName = "jsonb")] public SnVerificationMark? Verification { get; set; }
    [Column(TypeName = "jsonb")] public SnCustomAppOauthConfig? OauthConfig { get; set; }
    [Column(TypeName = "jsonb")] public SnCustomAppLinks? Links { get; set; }

    [JsonIgnore] public List<SnCustomAppSecret> Secrets { get; set; } = new List<SnCustomAppSecret>();

    public Guid ProjectId { get; set; }
    public SnDevProject Project { get; set; } = null!;

    [NotMapped]
    public SnDeveloper Developer => Project.Developer;

    [NotMapped] public string ResourceIdentifier => "developer.app:" + Id;

    public Proto.CustomApp ToProto()
    {
        return new Proto.CustomApp
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
            Picture = Picture?.ToProtoValue(),
            Background = Background?.ToProtoValue(),
            Verification = Verification?.ToProtoValue(),
            Links = Links is null ? null : new Proto.CustomAppLinks
            {
                HomePage = Links.HomePage ?? string.Empty,
                PrivacyPolicy = Links.PrivacyPolicy ?? string.Empty,
                TermsOfService = Links.TermsOfService ?? string.Empty
            },
            OauthConfig = OauthConfig is null ? null : new Proto.CustomAppOauthConfig
            {
                ClientUri = OauthConfig.ClientUri ?? string.Empty,
                RedirectUris = { OauthConfig.RedirectUris ?? [] },
                PostLogoutRedirectUris = { OauthConfig.PostLogoutRedirectUris ?? [] },
                AllowedScopes = { OauthConfig.AllowedScopes ?? [] },
                AllowedGrantTypes = { OauthConfig.AllowedGrantTypes ?? [] },
                RequirePkce = OauthConfig.RequirePkce,
                AllowOfflineAccess = OauthConfig.AllowOfflineAccess
            },
            ProjectId = ProjectId.ToString(),
            CreatedAt = CreatedAt.ToTimestamp(),
            UpdatedAt = UpdatedAt.ToTimestamp()
        };
    }

    public static SnCustomApp FromProtoValue(Proto.CustomApp p)
    {
        var obj = new SnCustomApp
        {
            Id = Guid.Parse(p.Id),
            Slug = p.Slug,
            Name = p.Name,
            Description = string.IsNullOrEmpty(p.Description) ? null : p.Description,
            Status = p.Status switch
            {
                Shared.Proto.CustomAppStatus.Developing => CustomAppStatus.Developing,
                Shared.Proto.CustomAppStatus.Staging => CustomAppStatus.Staging,
                Shared.Proto.CustomAppStatus.Production => CustomAppStatus.Production,
                Shared.Proto.CustomAppStatus.Suspended => CustomAppStatus.Suspended,
                _ => CustomAppStatus.Developing
            },
            ProjectId = string.IsNullOrEmpty(p.ProjectId) ? Guid.Empty : Guid.Parse(p.ProjectId),
            CreatedAt = p.CreatedAt.ToInstant(),
            UpdatedAt = p.UpdatedAt.ToInstant(),
        };

        if (p.Picture is not null) obj.Picture = SnCloudFileReferenceObject.FromProtoValue(p.Picture);
        if (p.Background is not null) obj.Background = SnCloudFileReferenceObject.FromProtoValue(p.Background);
        if (p.Verification is not null) obj.Verification = SnVerificationMark.FromProtoValue(p.Verification);
        if (p.Links is not null)
        {
            obj.Links = new SnCustomAppLinks
            {
                HomePage = string.IsNullOrEmpty(p.Links.HomePage) ? null : p.Links.HomePage,
                PrivacyPolicy = string.IsNullOrEmpty(p.Links.PrivacyPolicy) ? null : p.Links.PrivacyPolicy,
                TermsOfService = string.IsNullOrEmpty(p.Links.TermsOfService) ? null : p.Links.TermsOfService
            };
        }

        return obj;
    }
}

public class SnCustomAppLinks
{
    [MaxLength(8192)] public string? HomePage { get; set; }
    [MaxLength(8192)] public string? PrivacyPolicy { get; set; }
    [MaxLength(8192)] public string? TermsOfService { get; set; }
}

public class SnCustomAppOauthConfig
{
    [MaxLength(1024)] public string? ClientUri { get; set; }
    [MaxLength(4096)] public string[] RedirectUris { get; set; } = [];
    [MaxLength(4096)] public string[]? PostLogoutRedirectUris { get; set; }
    [MaxLength(256)] public string[]? AllowedScopes { get; set; } = ["openid", "profile", "email"];
    [MaxLength(256)] public string[] AllowedGrantTypes { get; set; } = ["authorization_code", "refresh_token"];
    public bool RequirePkce { get; set; } = true;
    public bool AllowOfflineAccess { get; set; } = false;
}

public class SnCustomAppSecret : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(1024)] public string Secret { get; set; } = null!;
    [MaxLength(4096)] public string? Description { get; set; } = null!;
    public Instant? ExpiredAt { get; set; }
    public bool IsOidc { get; set; } = false; // Indicates if this secret is for OIDC/OAuth

    public Guid AppId { get; set; }
    public SnCustomApp App { get; set; } = null!;


    public static SnCustomAppSecret FromProtoValue(Proto.CustomAppSecret p)
    {
        return new SnCustomAppSecret
        {
            Id = Guid.Parse(p.Id),
            Secret = p.Secret,
            Description = p.Description,
            ExpiredAt = p.ExpiredAt?.ToInstant(),
            IsOidc = p.IsOidc,
            AppId = Guid.Parse(p.AppId),
        };
    }

    public Proto.CustomAppSecret ToProto()
    {
        return new Proto.CustomAppSecret
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

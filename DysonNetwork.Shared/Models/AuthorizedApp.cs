using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using NodaTime;

namespace DysonNetwork.Shared.Models;

public enum AuthorizedAppType
{
    Oidc,
    AppConnect
}

public class SnAuthorizedApp : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public AuthorizedAppType Type { get; set; } = AuthorizedAppType.Oidc;

    public Guid AccountId { get; set; }
    [JsonIgnore] public SnAccount Account { get; set; } = null!;

    // References custom app id in Develop service.
    public Guid AppId { get; set; }

    [MaxLength(1024)] public string? AppSlug { get; set; }
    [MaxLength(1024)] public string? AppName { get; set; }

    public Instant LastAuthorizedAt { get; set; }
    public Instant? LastUsedAt { get; set; }
}


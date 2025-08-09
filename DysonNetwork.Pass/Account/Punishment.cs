using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DysonNetwork.Shared.Data;
using NodaTime;

namespace DysonNetwork.Pass.Account;

public enum PunishmentType
{
    // TODO: impl the permission modification
    PermissionModification,
    BlockLogin,
    DisableAccount,
    Strike
}

public class Punishment : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(8192)] public string Reason { get; set; } = string.Empty;
    public Instant? ExpiredAt { get; set; }
    
    public PunishmentType Type { get; set; }
    [Column(TypeName = "jsonb")] public List<string>? BlockedPermissions { get; set; }

    public Guid AccountId { get; set; }
    public Account Account { get; set; } = null!;
}
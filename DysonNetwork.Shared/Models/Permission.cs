using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Proto;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Shared.Models;

public enum PermissionNodeActorType
{
    Account,
    Group
}

/// The permission node model provides the infrastructure of permission control in Dyson Network.
/// It based on the ABAC permission model.
/// 
/// The value can be any type, boolean and number for most cases and stored in jsonb.
/// 
/// The area represents the region this permission affects. For example, the pub:&lt;publisherId&gt;
/// indicates it's a permission node for the publishers managing.
///
/// And the actor shows who owns the permission, in most cases, the user:&lt;userId&gt;
/// and when the permission node has a GroupId, the actor will be set to the group, but it won't work on checking
/// expect the member of that permission group inherent the permission from the group.
[Index(nameof(Key), nameof(Actor))]
public class SnPermissionNode : ModelBase, IDisposable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public PermissionNodeActorType Type { get; set; } = PermissionNodeActorType.Account;
    [MaxLength(1024)] public string Actor { get; set; } = null!;
    [MaxLength(1024)] public string Key { get; set; } = null!;
    [Column(TypeName = "jsonb")] public JsonDocument Value { get; set; } = null!;
    public Instant? ExpiredAt { get; set; } = null;
    public Instant? AffectedAt { get; set; } = null;

    public Guid? GroupId { get; set; } = null;
    [JsonIgnore] public SnPermissionGroup? Group { get; set; } = null;

    public DyPermissionNode ToProtoValue()
    {
        return new DyPermissionNode
        {
            Id = Id.ToString(),
            Actor = Actor,
            Type = Type switch
            {
                PermissionNodeActorType.Account => DyPermissionNodeActorType.DyAccount,
                PermissionNodeActorType.Group => DyPermissionNodeActorType.DyGroup,
                _ => throw new ArgumentOutOfRangeException()
            },
            Key = Key,
            Value = Google.Protobuf.WellKnownTypes.Value.Parser.ParseJson(Value.RootElement.GetRawText()),
            ExpiredAt = ExpiredAt?.ToTimestamp(),
            AffectedAt = AffectedAt?.ToTimestamp(),
            GroupId = GroupId?.ToString() ?? string.Empty
        };
    }

    public static PermissionNodeActorType ConvertProtoActorType(DyPermissionNodeActorType? val)
    {
        return val switch
        {
            DyPermissionNodeActorType.DyAccount => PermissionNodeActorType.Account,
            DyPermissionNodeActorType.DyGroup => PermissionNodeActorType.Group,
            _ => PermissionNodeActorType.Account
        };
    }

    public void Dispose()
    {
        Value.Dispose();
        GC.SuppressFinalize(this);
    }
}

public class SnPermissionGroup : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(1024)] public string Key { get; set; } = null!;

    public List<SnPermissionNode> Nodes { get; set; } = [];
    [JsonIgnore] public List<SnPermissionGroupMember> Members { get; set; } = [];
}

public class SnPermissionGroupMember : ModelBase
{
    public Guid GroupId { get; set; }
    public SnPermissionGroup Group { get; set; } = null!;
    [MaxLength(1024)] public string Actor { get; set; } = null!;

    public Instant? ExpiredAt { get; set; }
    public Instant? AffectedAt { get; set; }
}
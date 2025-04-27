using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using NodaTime;

namespace DysonNetwork.Sphere.Permission;

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
[Index(nameof(Key), nameof(Area), nameof(Actor))]
public class PermissionNode : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(1024)] public string Actor { get; set; } = null!;
    [MaxLength(1024)] public string Area { get; set; } = null!;
    [MaxLength(1024)] public string Key { get; set; } = null!;
    [Column(TypeName = "jsonb")] public object Value { get; set; } = null!;
    public Instant? ExpiredAt { get; set; } = null;
    public Instant? AffectedAt { get; set; } = null;
    
    public Guid? GroupId { get; set; } = null;
    [JsonIgnore] public PermissionNode? Group { get; set; } = null;
}

public class PermissionGroup : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(1024)] public string Key { get; set; } = null!;

    public ICollection<PermissionNode> Nodes { get; set; } = new List<PermissionNode>();
    [JsonIgnore] public ICollection<PermissionGroupMember> Members { get; set; } = new List<PermissionGroupMember>();
}

public class PermissionGroupMember : ModelBase
{
    public Guid GroupId { get; set; }
    public long AccountId { get; set; }
    public PermissionGroup Group { get; set; } = null!;
    public Account.Account Account { get; set; } = null!;
    
    public Instant? ExpiredAt { get; set; } = null;
    public Instant? AffectedAt { get; set; } = null;
}
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Point = NetTopologySuite.Geometries.Point;

namespace DysonNetwork.Sphere.Account;

public abstract class ActionLogType
{
    public const string NewLogin = "login";
    public const string ChallengeAttempt = "challenges.attempt";
    public const string ChallengeSuccess = "challenges.success";
    public const string ChallengeFailure = "challenges.failure";
    public const string PostCreate = "posts.create";
    public const string PostUpdate = "posts.update";
    public const string PostDelete = "posts.delete";
    public const string PostReact = "posts.react";
    public const string MessageCreate = "messages.create";
    public const string MessageUpdate = "messages.update";
    public const string MessageDelete = "messages.delete";
    public const string MessageReact = "messages.react";
    public const string PublisherCreate = "publishers.create";
    public const string PublisherUpdate = "publishers.update";
    public const string PublisherDelete = "publishers.delete";
    public const string PublisherMemberInvite = "publishers.members.invite";
    public const string PublisherMemberJoin = "publishers.members.join";
    public const string PublisherMemberLeave = "publishers.members.leave";
    public const string PublisherMemberKick = "publishers.members.kick";
    public const string RealmCreate = "realms.create";
    public const string RealmUpdate = "realms.update";
    public const string RealmDelete = "realms.delete";
    public const string RealmInvite = "realms.invite";
    public const string RealmJoin = "realms.join";
    public const string RealmLeave = "realms.leave";
    public const string RealmKick = "realms.kick";
    public const string RealmAdjustRole = "realms.role.edit";
    public const string ChatroomCreate = "chatrooms.create";
    public const string ChatroomUpdate = "chatrooms.update";
    public const string ChatroomDelete = "chatrooms.delete";
    public const string ChatroomInvite = "chatrooms.invite";
    public const string ChatroomJoin = "chatrooms.join";
    public const string ChatroomLeave = "chatrooms.leave";
    public const string ChatroomKick = "chatrooms.kick";
    public const string ChatroomAdjustRole = "chatrooms.role.edit";
}

public class ActionLog : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(4096)] public string Action { get; set; } = null!;
    [Column(TypeName = "jsonb")] public Dictionary<string, object> Meta { get; set; } = new();
    [MaxLength(512)] public string? UserAgent { get; set; }
    [MaxLength(128)] public string? IpAddress { get; set; }
    public Point? Location { get; set; }

    public Guid AccountId { get; set; }
    public Account Account { get; set; } = null!;
    public Guid? SessionId { get; set; }
}
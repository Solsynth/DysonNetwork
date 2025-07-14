using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Proto;
using NodaTime.Serialization.Protobuf;
using Point = NetTopologySuite.Geometries.Point;

namespace DysonNetwork.Pass.Account;

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

    public Shared.Proto.ActionLog ToProtoValue()
    {
        var protoLog = new Shared.Proto.ActionLog
        {
            Id = Id.ToString(),
            Action = Action,
            UserAgent = UserAgent ?? string.Empty,
            IpAddress = IpAddress ?? string.Empty,
            Location = Location?.ToString() ?? string.Empty,
            AccountId = AccountId.ToString(),
            CreatedAt = CreatedAt.ToTimestamp()
        };

        // Convert Meta dictionary to Struct
        protoLog.Meta.Add(GrpcTypeHelper.ConvertToValueMap(Meta));

        if (SessionId.HasValue)
            protoLog.SessionId = SessionId.Value.ToString();

        return protoLog;
    }
}
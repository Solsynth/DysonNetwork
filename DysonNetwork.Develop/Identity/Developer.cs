using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using DysonNetwork.Develop.Project;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Data;
using VerificationMark = DysonNetwork.Shared.Data.VerificationMark;

namespace DysonNetwork.Develop.Identity;

public class Developer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PublisherId { get; set; }
    
    [JsonIgnore] public List<DevProject> Projects { get; set; } = [];
    
    [NotMapped] public PublisherInfo? Publisher { get; set; }
}

public class PublisherInfo
{
    public Guid Id { get; set; }
    public PublisherType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Nick { get; set; } = string.Empty;
    public string? Bio { get; set; }

    public CloudFileReferenceObject? Picture { get; set; }
    public CloudFileReferenceObject? Background { get; set; }

    public VerificationMark? Verification { get; set; }
    public Guid? AccountId { get; set; }
    public Guid? RealmId { get; set; }

    public static PublisherInfo FromProto(Publisher proto)
    {
        var info = new PublisherInfo
        {
            Id = Guid.Parse(proto.Id),
            Type = proto.Type == PublisherType.PubIndividual
                ? PublisherType.PubIndividual
                : PublisherType.PubOrganizational,
            Name = proto.Name,
            Nick = proto.Nick,
            Bio = string.IsNullOrEmpty(proto.Bio) ? null : proto.Bio,
            Verification = proto.VerificationMark is not null
                ? VerificationMark.FromProtoValue(proto.VerificationMark)
                : null,
            AccountId = string.IsNullOrEmpty(proto.AccountId) ? null : Guid.Parse(proto.AccountId),
            RealmId = string.IsNullOrEmpty(proto.RealmId) ? null : Guid.Parse(proto.RealmId)
        };

        if (proto.Picture != null)
        {
            info.Picture = new CloudFileReferenceObject
            {
                Id = proto.Picture.Id,
                Name = proto.Picture.Name,
                MimeType = proto.Picture.MimeType,
                Hash = proto.Picture.Hash,
                Size = proto.Picture.Size
            };
        }

        if (proto.Background != null)
        {
            info.Background = new CloudFileReferenceObject
            {
                Id = proto.Background.Id,
                Name = proto.Background.Name,
                MimeType = proto.Background.MimeType,
                Hash = proto.Background.Hash,
                Size = (long)proto.Background.Size
            };
        }

        return info;
    }
}
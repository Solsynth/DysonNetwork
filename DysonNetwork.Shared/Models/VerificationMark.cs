using System.ComponentModel.DataAnnotations;

namespace DysonNetwork.Shared.Models;

/// <summary>
/// The verification info of a resource
/// stands, for it is really an individual or organization or a company in the real world.
/// Besides, it can also be use for mark parody or fake.
/// </summary>
public class SnVerificationMark
{
    public VerificationMarkType Type { get; set; }
    [MaxLength(1024)] public string? Title { get; set; }
    [MaxLength(8192)] public string? Description { get; set; }
    [MaxLength(1024)] public string? VerifiedBy { get; set; }

    public Proto.VerificationMark ToProtoValue()
    {
        var proto = new Proto.VerificationMark
        {
            Type = Type switch
            {
                VerificationMarkType.Official => Proto.VerificationMarkType.Official,
                VerificationMarkType.Individual => Proto.VerificationMarkType.Individual,
                VerificationMarkType.Organization => Proto.VerificationMarkType.Organization,
                VerificationMarkType.Government => Proto.VerificationMarkType.Government,
                VerificationMarkType.Creator => Proto.VerificationMarkType.Creator,
                VerificationMarkType.Developer => Proto.VerificationMarkType.Developer,
                VerificationMarkType.Parody => Proto.VerificationMarkType.Parody,
                _ => Proto.VerificationMarkType.Unspecified
            },
            Title = Title ?? string.Empty,
            Description = Description ?? string.Empty,
            VerifiedBy = VerifiedBy ?? string.Empty
        };

        return proto;
    }
    
    
    public static SnVerificationMark FromProtoValue(Proto.VerificationMark proto)
    {
        return new SnVerificationMark
        {
            Type = proto.Type switch
            {
                Proto.VerificationMarkType.Official => VerificationMarkType.Official,
                Proto.VerificationMarkType.Individual => VerificationMarkType.Individual,
                Proto.VerificationMarkType.Organization => VerificationMarkType.Organization,
                Proto.VerificationMarkType.Government => VerificationMarkType.Government,
                Proto.VerificationMarkType.Creator => VerificationMarkType.Creator,
                Proto.VerificationMarkType.Developer => VerificationMarkType.Developer,
                Proto.VerificationMarkType.Parody => VerificationMarkType.Parody,
                _ => VerificationMarkType.Individual
            },
            Title = proto.Title,
            Description = proto.Description,
            VerifiedBy = proto.VerifiedBy
        };
    }
}

public enum VerificationMarkType
{
    Official,
    Individual,
    Organization,
    Government,
    Creator,
    Developer,
    Parody
}
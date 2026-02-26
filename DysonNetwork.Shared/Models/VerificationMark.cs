using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Proto;

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

    public DyVerificationMark ToProtoValue()
    {
        var proto = new DyVerificationMark
        {
            Type = Type switch
            {
                VerificationMarkType.Official => DyVerificationMarkType.DyOfficial,
                VerificationMarkType.Individual => DyVerificationMarkType.DyIndividual,
                VerificationMarkType.Organization => DyVerificationMarkType.DyOrganization,
                VerificationMarkType.Government => DyVerificationMarkType.DyGovernment,
                VerificationMarkType.Creator => DyVerificationMarkType.DyCreator,
                VerificationMarkType.Developer => DyVerificationMarkType.DyDeveloper,
                VerificationMarkType.Parody => DyVerificationMarkType.DyParody,
                _ => DyVerificationMarkType.DyIndividual
            },
            Title = Title ?? string.Empty,
            Description = Description ?? string.Empty,
            VerifiedBy = VerifiedBy ?? string.Empty
        };

        return proto;
    }
    
    
    public static SnVerificationMark FromProtoValue(DyVerificationMark proto)
    {
        return new SnVerificationMark
        {
            Type = proto.Type switch
            {
                DyVerificationMarkType.DyOfficial => VerificationMarkType.Official,
                DyVerificationMarkType.DyIndividual => VerificationMarkType.Individual,
                DyVerificationMarkType.DyOrganization => VerificationMarkType.Organization,
                DyVerificationMarkType.DyGovernment => VerificationMarkType.Government,
                DyVerificationMarkType.DyCreator => VerificationMarkType.Creator,
                DyVerificationMarkType.DyDeveloper => VerificationMarkType.Developer,
                DyVerificationMarkType.DyParody => VerificationMarkType.Parody,
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
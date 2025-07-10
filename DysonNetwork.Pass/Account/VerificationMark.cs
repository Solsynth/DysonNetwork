using System.ComponentModel.DataAnnotations;

namespace DysonNetwork.Pass.Account;

/// <summary>
/// The verification info of a resource
/// stands, for it is really an individual or organization or a company in the real world.
/// Besides, it can also be use for mark parody or fake.
/// </summary>
public class VerificationMark
{
    public VerificationMarkType Type { get; set; }
    [MaxLength(1024)] public string? Title { get; set; }
    [MaxLength(8192)] public string? Description { get; set; }
    [MaxLength(1024)] public string? VerifiedBy { get; set; }
}

public enum VerificationMarkType
{
    Official,
    Individual,
    Organization,
    Government,
    Creator
}
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Proto;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Shared.Models;

public enum MagicSpellType
{
    AccountActivation,
    AccountDeactivation,
    AccountRemoval,
    AuthPasswordReset,
    ContactVerification
}

[Index(nameof(Spell), nameof(DeletedAt), IsUnique = true)]
public class SnMagicSpell : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [JsonIgnore] [MaxLength(1024)] public string Spell { get; set; } = null!;
    public MagicSpellType Type { get; set; }
    public Instant? ExpiresAt { get; set; }
    public Instant? AffectedAt { get; set; }
    [Column(TypeName = "jsonb")] public Dictionary<string, object> Meta { get; set; } = new();

    public Guid? AccountId { get; set; }
    [NotMapped] public SnAccount? Account { get; set; }

    public DyMagicSpell ToProtoValue()
    {
        var proto = new DyMagicSpell
        {
            Id = Id.ToString(),
            Type = Type switch
            {
                MagicSpellType.AccountActivation => DyMagicSpellType.DyMagicSpellAccountActivation,
                MagicSpellType.AccountDeactivation => DyMagicSpellType.DyMagicSpellAccountDeactivation,
                MagicSpellType.AccountRemoval => DyMagicSpellType.DyMagicSpellAccountRemoval,
                MagicSpellType.AuthPasswordReset => DyMagicSpellType.DyMagicSpellAuthPasswordReset,
                MagicSpellType.ContactVerification => DyMagicSpellType.DyMagicSpellContactVerification,
                _ => DyMagicSpellType.Unspecified
            },
            ExpiresAt = ExpiresAt?.ToTimestamp(),
            AffectedAt = AffectedAt?.ToTimestamp(),
            AccountId = AccountId?.ToString(),
            SpellWord = Spell,
            CreatedAt = CreatedAt.ToDateTimeUtc() != default ? CreatedAt.ToTimestamp() : null,
            UpdatedAt = UpdatedAt.ToDateTimeUtc() != default ? UpdatedAt.ToTimestamp() : null
        };

        proto.Meta.Add(InfraObjectCoder.ConvertToValueMap(Meta));
        return proto;
    }

    public static SnMagicSpell FromProtoValue(DyMagicSpell proto)
    {
        return new SnMagicSpell
        {
            Id = Guid.Parse(proto.Id),
            Type = proto.Type switch
            {
                DyMagicSpellType.DyMagicSpellAccountActivation => MagicSpellType.AccountActivation,
                DyMagicSpellType.DyMagicSpellAccountDeactivation => MagicSpellType.AccountDeactivation,
                DyMagicSpellType.DyMagicSpellAccountRemoval => MagicSpellType.AccountRemoval,
                DyMagicSpellType.DyMagicSpellAuthPasswordReset => MagicSpellType.AuthPasswordReset,
                DyMagicSpellType.DyMagicSpellContactVerification => MagicSpellType.ContactVerification,
                _ => throw new ArgumentOutOfRangeException(nameof(proto.Type), proto.Type, "Unsupported magic spell type.")
            },
            ExpiresAt = proto.ExpiresAt?.ToInstant(),
            AffectedAt = proto.AffectedAt?.ToInstant(),
            Meta = InfraObjectCoder.ConvertFromValueMap(proto.Meta)
                .Where(x => x.Value is not null)
                .ToDictionary(x => x.Key, x => x.Value!),
            AccountId = proto.AccountId is not null ? Guid.Parse(proto.AccountId) : null,
            Spell = proto.SpellWord ?? string.Empty,
            CreatedAt = proto.CreatedAt?.ToInstant() ?? default,
            UpdatedAt = proto.UpdatedAt?.ToInstant() ?? default
        };
    }
}

public enum AffiliationSpellType
{
    RegistrationInvite
}

/// <summary>
/// Different from the magic spell, this is for the regeneration invite and other marketing usage.
/// </summary>
[Index(nameof(Spell), nameof(DeletedAt), IsUnique = true)]
public class SnAffiliationSpell : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(1024)] public string Spell { get; set; } = null!;
    public AffiliationSpellType Type { get; set; }
    public Instant? ExpiresAt { get; set; }
    public Instant? AffectedAt { get; set; }
    [Column(TypeName = "jsonb")] public Dictionary<string, object> Meta { get; set; } = new();

    public List<SnAffiliationResult> Results = [];
    
    public Guid? AccountId { get; set; }
    [NotMapped] public SnAccount? Account { get; set; }
}

/// <summary>
/// The record for who used the affiliation spells
/// </summary>
public class SnAffiliationResult : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(8192)] public string ResourceIdentifier { get; set; } = null!;
    
    public Guid SpellId { get; set; }
    [JsonIgnore] public SnAffiliationSpell Spell { get; set; } = null!;
}

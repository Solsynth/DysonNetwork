using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Shared.Models;

public enum MagicSpellType
{
    AccountActivation,
    AccountDeactivation,
    AccountRemoval,
    AuthPasswordReset,
    ContactVerification
}

[Index(nameof(Spell), IsUnique = true)]
public class SnMagicSpell : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [JsonIgnore] [MaxLength(1024)] public string Spell { get; set; } = null!;
    public MagicSpellType Type { get; set; }
    public Instant? ExpiresAt { get; set; }
    public Instant? AffectedAt { get; set; }
    [Column(TypeName = "jsonb")] public Dictionary<string, object> Meta { get; set; } = new();

    public Guid? AccountId { get; set; }
    public SnAccount? Account { get; set; }
}

public enum AffiliationSpellType
{
    RegistrationInvite
}

/// <summary>
/// Different from the magic spell, this is for the regeneration invite and other marketing usage.
/// </summary>
[Index(nameof(Spell), IsUnique = true)]
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
    public SnAccount? Account { get; set; }
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
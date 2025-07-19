using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Data;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Pass.Account;

public enum MagicSpellType
{
    AccountActivation,
    AccountDeactivation,
    AccountRemoval,
    AuthPasswordReset,
    ContactVerification,
}

[Index(nameof(Spell), IsUnique = true)]
public class MagicSpell : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [JsonIgnore] [MaxLength(1024)] public string Spell { get; set; } = null!;
    public MagicSpellType Type { get; set; }
    public Instant? ExpiresAt { get; set; }
    public Instant? AffectedAt { get; set; }
    [Column(TypeName = "jsonb")] public Dictionary<string, object> Meta { get; set; } = new();

    public Guid? AccountId { get; set; }
    public Account? Account { get; set; }
}
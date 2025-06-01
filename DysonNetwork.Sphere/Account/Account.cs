using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using DysonNetwork.Sphere.Permission;
using DysonNetwork.Sphere.Storage;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Account;

[Index(nameof(Name), IsUnique = true)]
public class Account : ModelBase
{
    public Guid Id { get; set; }
    [MaxLength(256)] public string Name { get; set; } = string.Empty;
    [MaxLength(256)] public string Nick { get; set; } = string.Empty;
    [MaxLength(32)] public string Language { get; set; } = string.Empty;
    public Instant? ActivatedAt { get; set; }
    public bool IsSuperuser { get; set; } = false;

    public Profile Profile { get; set; } = null!;
    public ICollection<AccountContact> Contacts { get; set; } = new List<AccountContact>();
    public ICollection<Badge> Badges { get; set; } = new List<Badge>();
    
    [JsonIgnore] public ICollection<AccountAuthFactor> AuthFactors { get; set; } = new List<AccountAuthFactor>();
    [JsonIgnore] public ICollection<Auth.Session> Sessions { get; set; } = new List<Auth.Session>();
    [JsonIgnore] public ICollection<Auth.Challenge> Challenges { get; set; } = new List<Auth.Challenge>();

    [JsonIgnore] public ICollection<Relationship> OutgoingRelationships { get; set; } = new List<Relationship>();
    [JsonIgnore] public ICollection<Relationship> IncomingRelationships { get; set; } = new List<Relationship>();
}

public abstract class Leveling
{
    public static readonly List<int> ExperiencePerLevel = [
        0,      // Level 0
        100,    // Level 1
        250,    // Level 2
        500,    // Level 3
        1000,   // Level 4
        2000,   // Level 5
        4000,   // Level 6
        8000,   // Level 7
        16000,  // Level 8
        32000,  // Level 9
        64000,  // Level 10
        128000, // Level 11
        256000, // Level 12
        512000, // Level 13
        1024000 // Level 14
    ];
}

public class Profile : ModelBase
{
    public Guid Id { get; set; }
    [MaxLength(256)] public string? FirstName { get; set; }
    [MaxLength(256)] public string? MiddleName { get; set; }
    [MaxLength(256)] public string? LastName { get; set; }
    [MaxLength(4096)] public string? Bio { get; set; }
    [MaxLength(1024)] public string? Gender { get; set; }
    [MaxLength(1024)] public string? Pronouns { get; set; }
    public Instant? Birthday { get; set; }
    public Instant? LastSeenAt { get; set; }
    
    public int Experience { get; set; } = 0;
    [NotMapped] public int Level => Leveling.ExperiencePerLevel.Count(xp => Experience >= xp) - 1;
    [NotMapped] public double LevelingProgress => Level >= Leveling.ExperiencePerLevel.Count - 1 ? 100 : 
        (Experience - Leveling.ExperiencePerLevel[Level]) * 100.0 / 
        (Leveling.ExperiencePerLevel[Level + 1] - Leveling.ExperiencePerLevel[Level]);

    // Outdated fields, for backward compability
    [MaxLength(32)] public string? PictureId { get; set; }
    [MaxLength(32)] public string? BackgroundId { get; set; }
    
    [Column(TypeName = "jsonb")] public CloudFileReferenceObject? Picture { get; set; }
    [Column(TypeName = "jsonb")] public CloudFileReferenceObject? Background { get; set; }

    public Guid AccountId { get; set; }
    [JsonIgnore] public Account Account { get; set; } = null!;
}

public class AccountContact : ModelBase
{
    public Guid Id { get; set; }
    public AccountContactType Type { get; set; }
    public Instant? VerifiedAt { get; set; }
    [MaxLength(1024)] public string Content { get; set; } = string.Empty;

    public Guid AccountId { get; set; }
    [JsonIgnore] public Account Account { get; set; } = null!;
}

public enum AccountContactType
{
    Email,
    PhoneNumber,
    Address
}

public class AccountAuthFactor : ModelBase
{
    public Guid Id { get; set; }
    public AccountAuthFactorType Type { get; set; }
    [MaxLength(8196)] public string? Secret { get; set; } = null;

    public Guid AccountId { get; set; }
    [JsonIgnore] public Account Account { get; set; } = null!;

    public AccountAuthFactor HashSecret(int cost = 12)
    {
        if (Secret == null) return this;
        Secret = BCrypt.Net.BCrypt.HashPassword(Secret, workFactor: cost);
        return this;
    }

    public bool VerifyPassword(string password)
    {
        if (Secret == null)
            throw new InvalidOperationException("Auth factor with no secret cannot be verified with password.");
        return BCrypt.Net.BCrypt.Verify(password, Secret);
    }
}

public enum AccountAuthFactorType
{
    Password,
    EmailCode,
    InAppCode,
    TimedCode
}
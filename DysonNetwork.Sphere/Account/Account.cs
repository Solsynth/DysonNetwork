using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using DysonNetwork.Sphere.Permission;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Account;

[Index(nameof(Name), IsUnique = true)]
public class Account : ModelBase
{
    public long Id { get; set; }
    [MaxLength(256)] public string Name { get; set; } = string.Empty;
    [MaxLength(256)] public string Nick { get; set; } = string.Empty;
    [MaxLength(32)] public string Language { get; set; } = string.Empty;
    public Instant? ActivatedAt { get; set; }
    public bool IsSuperuser { get; set; } = false;

    public Profile Profile { get; set; } = null!;
    public ICollection<AccountContact> Contacts { get; set; } = new List<AccountContact>();

    [JsonIgnore] public ICollection<AccountAuthFactor> AuthFactors { get; set; } = new List<AccountAuthFactor>();
    [JsonIgnore] public ICollection<Auth.Session> Sessions { get; set; } = new List<Auth.Session>();
    [JsonIgnore] public ICollection<Auth.Challenge> Challenges { get; set; } = new List<Auth.Challenge>();

    [JsonIgnore] public ICollection<Relationship> OutgoingRelationships { get; set; } = new List<Relationship>();
    [JsonIgnore] public ICollection<Relationship> IncomingRelationships { get; set; } = new List<Relationship>();
}

public class Profile : ModelBase
{
    public long Id { get; set; }
    [MaxLength(256)] public string? FirstName { get; set; }
    [MaxLength(256)] public string? MiddleName { get; set; }
    [MaxLength(256)] public string? LastName { get; set; }
    [MaxLength(4096)] public string? Bio { get; set; }

    public Storage.CloudFile? Picture { get; set; }
    public Storage.CloudFile? Background { get; set; }

    [JsonIgnore] public Account Account { get; set; } = null!;
}

public class AccountContact : ModelBase
{
    public long Id { get; set; }
    public AccountContactType Type { get; set; }
    public Instant? VerifiedAt { get; set; }
    [MaxLength(1024)] public string Content { get; set; } = string.Empty;

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
    public long Id { get; set; }
    public AccountAuthFactorType Type { get; set; }
    public string? Secret { get; set; } = null;

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
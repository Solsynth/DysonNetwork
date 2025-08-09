using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using DysonNetwork.Pass.Wallet;
using DysonNetwork.Shared.Data;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Serialization.Protobuf;
using OtpNet;
using VerificationMark = DysonNetwork.Shared.Data.VerificationMark;

namespace DysonNetwork.Pass.Account;

[Index(nameof(Name), IsUnique = true)]
public class Account : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(256)] public string Name { get; set; } = string.Empty;
    [MaxLength(256)] public string Nick { get; set; } = string.Empty;
    [MaxLength(32)] public string Language { get; set; } = string.Empty;
    public Instant? ActivatedAt { get; set; }
    public bool IsSuperuser { get; set; } = false;

    public AccountProfile Profile { get; set; } = null!;
    public ICollection<AccountContact> Contacts { get; set; } = new List<AccountContact>();
    public ICollection<AccountBadge> Badges { get; set; } = new List<AccountBadge>();

    [JsonIgnore] public ICollection<AccountAuthFactor> AuthFactors { get; set; } = new List<AccountAuthFactor>();
    [JsonIgnore] public ICollection<AccountConnection> Connections { get; set; } = new List<AccountConnection>();
    [JsonIgnore] public ICollection<Auth.AuthSession> Sessions { get; set; } = new List<Auth.AuthSession>();
    [JsonIgnore] public ICollection<Auth.AuthChallenge> Challenges { get; set; } = new List<Auth.AuthChallenge>();

    [JsonIgnore] public ICollection<Relationship> OutgoingRelationships { get; set; } = new List<Relationship>();
    [JsonIgnore] public ICollection<Relationship> IncomingRelationships { get; set; } = new List<Relationship>();

    [NotMapped] public SubscriptionReferenceObject? PerkSubscription { get; set; }

    public Shared.Proto.Account ToProtoValue()
    {
        var proto = new Shared.Proto.Account
        {
            Id = Id.ToString(),
            Name = Name,
            Nick = Nick,
            Language = Language,
            ActivatedAt = ActivatedAt?.ToTimestamp(),
            IsSuperuser = IsSuperuser,
            Profile = Profile.ToProtoValue(),
            PerkSubscription = PerkSubscription?.ToProtoValue(),
            CreatedAt = CreatedAt.ToTimestamp(),
            UpdatedAt = UpdatedAt.ToTimestamp()
        };

        // Add contacts
        foreach (var contact in Contacts)
            proto.Contacts.Add(contact.ToProtoValue());

        // Add badges
        foreach (var badge in Badges)
            proto.Badges.Add(badge.ToProtoValue());

        return proto;
    }


    public static Account FromProtoValue(Shared.Proto.Account proto)
    {
        var account = new Account
        {
            Id = Guid.Parse(proto.Id),
            Name = proto.Name,
            Nick = proto.Nick,
            Language = proto.Language,
            ActivatedAt = proto.ActivatedAt?.ToInstant(),
            IsSuperuser = proto.IsSuperuser,
            PerkSubscription = proto.PerkSubscription is not null
                ? SubscriptionReferenceObject.FromProtoValue(proto.PerkSubscription)
                : null,
            CreatedAt = proto.CreatedAt.ToInstant(),
            UpdatedAt = proto.UpdatedAt.ToInstant(),
        };

        account.Profile = AccountProfile.FromProtoValue(proto.Profile);

        foreach (var contactProto in proto.Contacts)
            account.Contacts.Add(AccountContact.FromProtoValue(contactProto));

        foreach (var badgeProto in proto.Badges)
            account.Badges.Add(AccountBadge.FromProtoValue(badgeProto));

        return account;
    }
}

public abstract class Leveling
{
    public static readonly List<int> ExperiencePerLevel =
    [
        0, // Level 0
        100, // Level 1
        250, // Level 2
        500, // Level 3
        1000, // Level 4
        2000, // Level 5
        4000, // Level 6
        8000, // Level 7
        16000, // Level 8
        32000, // Level 9
        64000, // Level 10
        128000, // Level 11
        256000, // Level 12
        512000, // Level 13
        1024000 // Level 14
    ];
}

public class AccountProfile : ModelBase, IIdentifiedResource
{
    public Guid Id { get; set; }
    [MaxLength(256)] public string? FirstName { get; set; }
    [MaxLength(256)] public string? MiddleName { get; set; }
    [MaxLength(256)] public string? LastName { get; set; }
    [MaxLength(4096)] public string? Bio { get; set; }
    [MaxLength(1024)] public string? Gender { get; set; }
    [MaxLength(1024)] public string? Pronouns { get; set; }
    [MaxLength(1024)] public string? TimeZone { get; set; }
    [MaxLength(1024)] public string? Location { get; set; }
    [Column(TypeName = "jsonb")] public List<ProfileLink>? Links { get; set; }
    public Instant? Birthday { get; set; }
    public Instant? LastSeenAt { get; set; }

    [Column(TypeName = "jsonb")] public VerificationMark? Verification { get; set; }
    [Column(TypeName = "jsonb")] public BadgeReferenceObject? ActiveBadge { get; set; }

    public int Experience { get; set; } = 0;
    [NotMapped] public int Level => Leveling.ExperiencePerLevel.Count(xp => Experience >= xp) - 1;

    [NotMapped]
    public double LevelingProgress => Level >= Leveling.ExperiencePerLevel.Count - 1
        ? 100
        : (Experience - Leveling.ExperiencePerLevel[Level]) * 100.0 /
          (Leveling.ExperiencePerLevel[Level + 1] - Leveling.ExperiencePerLevel[Level]);

    [Column(TypeName = "jsonb")] public CloudFileReferenceObject? Picture { get; set; }
    [Column(TypeName = "jsonb")] public CloudFileReferenceObject? Background { get; set; }

    public Guid AccountId { get; set; }
    [JsonIgnore] public Account Account { get; set; } = null!;

    public Shared.Proto.AccountProfile ToProtoValue()
    {
        var proto = new Shared.Proto.AccountProfile
        {
            Id = Id.ToString(),
            FirstName = FirstName ?? string.Empty,
            MiddleName = MiddleName ?? string.Empty,
            LastName = LastName ?? string.Empty,
            Bio = Bio ?? string.Empty,
            Gender = Gender ?? string.Empty,
            Pronouns = Pronouns ?? string.Empty,
            TimeZone = TimeZone ?? string.Empty,
            Location = Location ?? string.Empty,
            Birthday = Birthday?.ToTimestamp(),
            LastSeenAt = LastSeenAt?.ToTimestamp(),
            Experience = Experience,
            Level = Level,
            LevelingProgress = LevelingProgress,
            Picture = Picture?.ToProtoValue(),
            Background = Background?.ToProtoValue(),
            AccountId = AccountId.ToString(),
            Verification = Verification?.ToProtoValue(),
            ActiveBadge = ActiveBadge?.ToProtoValue(),
            CreatedAt = CreatedAt.ToTimestamp(),
            UpdatedAt = UpdatedAt.ToTimestamp()
        };

        return proto;
    }

    public static AccountProfile FromProtoValue(Shared.Proto.AccountProfile proto)
    {
        var profile = new AccountProfile
        {
            Id = Guid.Parse(proto.Id),
            FirstName = proto.FirstName,
            LastName = proto.LastName,
            MiddleName = proto.MiddleName,
            Bio = proto.Bio,
            Gender = proto.Gender,
            Pronouns = proto.Pronouns,
            TimeZone = proto.TimeZone,
            Location = proto.Location,
            Birthday = proto.Birthday?.ToInstant(),
            LastSeenAt = proto.LastSeenAt?.ToInstant(),
            Verification = proto.Verification is null ? null : VerificationMark.FromProtoValue(proto.Verification),
            ActiveBadge = proto.ActiveBadge is null ? null : BadgeReferenceObject.FromProtoValue(proto.ActiveBadge),
            Experience = proto.Experience,
            Picture = proto.Picture is null ? null : CloudFileReferenceObject.FromProtoValue(proto.Picture),
            Background = proto.Background is null ? null : CloudFileReferenceObject.FromProtoValue(proto.Background),
            AccountId = Guid.Parse(proto.AccountId),
            CreatedAt = proto.CreatedAt.ToInstant(),
            UpdatedAt = proto.UpdatedAt.ToInstant()
        };

        return profile;
    }

    public string ResourceIdentifier => $"account:profile:{Id}";
}

public class ProfileLink
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}

public class AccountContact : ModelBase
{
    public Guid Id { get; set; }
    public AccountContactType Type { get; set; }
    public Instant? VerifiedAt { get; set; }
    public bool IsPrimary { get; set; } = false;
    public bool IsPublic { get; set; } = false;
    [MaxLength(1024)] public string Content { get; set; } = string.Empty;

    public Guid AccountId { get; set; }
    [JsonIgnore] public Account Account { get; set; } = null!;

    public Shared.Proto.AccountContact ToProtoValue()
    {
        var proto = new Shared.Proto.AccountContact
        {
            Id = Id.ToString(),
            Type = Type switch
            {
                AccountContactType.Email => Shared.Proto.AccountContactType.Email,
                AccountContactType.PhoneNumber => Shared.Proto.AccountContactType.PhoneNumber,
                AccountContactType.Address => Shared.Proto.AccountContactType.Address,
                _ => Shared.Proto.AccountContactType.Unspecified
            },
            Content = Content,
            IsPrimary = IsPrimary,
            VerifiedAt = VerifiedAt?.ToTimestamp(),
            AccountId = AccountId.ToString(),
            CreatedAt = CreatedAt.ToTimestamp(),
            UpdatedAt = UpdatedAt.ToTimestamp()
        };

        return proto;
    }

    public static AccountContact FromProtoValue(Shared.Proto.AccountContact proto)
    {
        var contact = new AccountContact
        {
            Id = Guid.Parse(proto.Id),
            AccountId = Guid.Parse(proto.AccountId),
            Type = proto.Type switch
            {
                Shared.Proto.AccountContactType.Email => AccountContactType.Email,
                Shared.Proto.AccountContactType.PhoneNumber => AccountContactType.PhoneNumber,
                Shared.Proto.AccountContactType.Address => AccountContactType.Address,
                _ => AccountContactType.Email
            },
            Content = proto.Content,
            IsPrimary = proto.IsPrimary,
            VerifiedAt = proto.VerifiedAt?.ToInstant(),
            CreatedAt = proto.CreatedAt.ToInstant(),
            UpdatedAt = proto.UpdatedAt.ToInstant()
        };

        return contact;
    }
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
    [JsonIgnore] [MaxLength(8196)] public string? Secret { get; set; }

    [JsonIgnore]
    [Column(TypeName = "jsonb")]
    public Dictionary<string, object>? Config { get; set; } = new();

    /// <summary>
    /// The trustworthy stands for how safe is this auth factor.
    /// Basically, it affects how many steps it can complete in authentication.
    /// Besides, users may need to use some high-trustworthy level auth factors when confirming some dangerous operations.
    /// </summary>
    public int Trustworthy { get; set; } = 1;

    public Instant? EnabledAt { get; set; }
    public Instant? ExpiredAt { get; set; }

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
        switch (Type)
        {
            case AccountAuthFactorType.Password:
            case AccountAuthFactorType.PinCode:
                return BCrypt.Net.BCrypt.Verify(password, Secret);
            case AccountAuthFactorType.TimedCode:
                var otp = new Totp(Base32Encoding.ToBytes(Secret));
                return otp.VerifyTotp(DateTime.UtcNow, password, out _, new VerificationWindow(previous: 5, future: 5));
            case AccountAuthFactorType.EmailCode:
            case AccountAuthFactorType.InAppCode:
            default:
                throw new InvalidOperationException("Unsupported verification type, use CheckDeliveredCode instead.");
        }
    }

    /// <summary>
    /// This dictionary will be returned to the client and should only be set when it just created.
    /// Useful for passing the client some data to finishing setup and recovery code.
    /// </summary>
    [NotMapped]
    public Dictionary<string, object>? CreatedResponse { get; set; }
}

public enum AccountAuthFactorType
{
    Password,
    EmailCode,
    InAppCode,
    TimedCode,
    PinCode,
}

public class AccountConnection : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(4096)] public string Provider { get; set; } = null!;
    [MaxLength(8192)] public string ProvidedIdentifier { get; set; } = null!;
    [Column(TypeName = "jsonb")] public Dictionary<string, object>? Meta { get; set; } = new();

    [JsonIgnore] [MaxLength(4096)] public string? AccessToken { get; set; }
    [JsonIgnore] [MaxLength(4096)] public string? RefreshToken { get; set; }
    public Instant? LastUsedAt { get; set; }

    public Guid AccountId { get; set; }
    public Account Account { get; set; } = null!;
}
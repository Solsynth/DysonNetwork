using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Serialization.Protobuf;
using OtpNet;

namespace DysonNetwork.Shared.Models;

[Index(nameof(Name), IsUnique = true)]
public class SnAccount : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(256)] public string Name { get; set; } = string.Empty;
    [MaxLength(256)] public string Nick { get; set; } = string.Empty;
    [MaxLength(32)] public string Language { get; set; } = string.Empty;
    [MaxLength(32)] public string Region { get; set; } = string.Empty;
    public Instant? ActivatedAt { get; set; }
    public bool IsSuperuser { get; set; } = false;

    // The ID is the BotAccount ID in the DysonNetwork.Develop
    public Guid? AutomatedId { get; set; }

    public SnAccountProfile Profile { get; set; } = null!;
    public ICollection<SnAccountContact> Contacts { get; set; } = [];
    public ICollection<SnAccountBadge> Badges { get; set; } = [];

    [JsonIgnore] public ICollection<SnAccountAuthFactor> AuthFactors { get; set; } = [];
    [JsonIgnore] public ICollection<SnAccountConnection> Connections { get; set; } = [];
    [JsonIgnore] public ICollection<SnAuthSession> Sessions { get; set; } = [];
    [JsonIgnore] public ICollection<SnAuthChallenge> Challenges { get; set; } = [];

    [JsonIgnore] public ICollection<SnAccountRelationship> OutgoingRelationships { get; set; } = [];
    [JsonIgnore] public ICollection<SnAccountRelationship> IncomingRelationships { get; set; } = [];

    [NotMapped] public SnSubscriptionReferenceObject? PerkSubscription { get; set; }

    public Proto.Account ToProtoValue()
    {
        var proto = new Proto.Account
        {
            Id = Id.ToString(),
            Name = Name,
            Nick = Nick,
            Language = Language,
            Region = Region,
            ActivatedAt = ActivatedAt?.ToTimestamp(),
            IsSuperuser = IsSuperuser,
            Profile = Profile.ToProtoValue(),
            PerkSubscription = PerkSubscription?.ToProtoValue(),
            CreatedAt = CreatedAt.ToTimestamp(),
            UpdatedAt = UpdatedAt.ToTimestamp(),
            AutomatedId = AutomatedId?.ToString()
        };

        // Add contacts
        foreach (var contact in Contacts)
            proto.Contacts.Add(contact.ToProtoValue());

        // Add badges
        foreach (var badge in Badges)
            proto.Badges.Add(badge.ToProtoValue());

        return proto;
    }


    public static SnAccount FromProtoValue(Proto.Account proto)
    {
        var account = new SnAccount
        {
            Id = Guid.Parse(proto.Id),
            Name = proto.Name,
            Nick = proto.Nick,
            Language = proto.Language,
            Region = proto.Region,
            ActivatedAt = proto.ActivatedAt?.ToInstant(),
            IsSuperuser = proto.IsSuperuser,
            PerkSubscription = proto.PerkSubscription is not null
                ? SnSubscriptionReferenceObject.FromProtoValue(proto.PerkSubscription)
                : null,
            CreatedAt = proto.CreatedAt.ToInstant(),
            UpdatedAt = proto.UpdatedAt.ToInstant(),
            AutomatedId = proto.AutomatedId is not null ? Guid.Parse(proto.AutomatedId) : null,
            Profile = SnAccountProfile.FromProtoValue(proto.Profile)
        };

        foreach (var contactProto in proto.Contacts)
            account.Contacts.Add(SnAccountContact.FromProtoValue(contactProto));

        foreach (var badgeProto in proto.Badges)
            account.Badges.Add(SnAccountBadge.FromProtoValue(badgeProto));

        return account;
    }
}

public abstract class Leveling
{
    private const int MaxLevel = 120;
    private const double BaseExp = 100.0;
    private const double K = 3.52; // tweak this for balance

    // Single level XP requirement (from L → L+1)
    public static double ExpForLevel(int level)
    {
        if (level < 1) return 0;
        return BaseExp + K * Math.Pow(level - 1, 2);
    }

    // Total cumulative XP required to reach level L
    public static double TotalExpForLevel(int level)
    {
        if (level < 1) return 0;
        return BaseExp * level + K * ((level - 1) * level * (2 * level - 1)) / 6.0;
    }

    // Get level from experience
    public static int GetLevelFromExp(int xp)
    {
        if (xp < 0) return 0;

        int level = 0;
        while (level < MaxLevel && TotalExpForLevel(level + 1) <= xp)
        {
            level++;
        }
        return level;
    }

    // Progress to next level (0.0 ~ 1.0)
    public static double GetProgressToNextLevel(int xp)
    {
        int currentLevel = GetLevelFromExp(xp);
        if (currentLevel >= MaxLevel) return 1.0;

        double prevTotal = TotalExpForLevel(currentLevel);
        double nextTotal = TotalExpForLevel(currentLevel + 1);

        return (xp - prevTotal) / (nextTotal - prevTotal);
    }
}

public class SnAccountProfile : ModelBase, IIdentifiedResource
{
    public Guid Id { get; set; } = Guid.NewGuid();
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

    [Column(TypeName = "jsonb")] public SnVerificationMark? Verification { get; set; }
    [Column(TypeName = "jsonb")] public SnAccountBadgeRef? ActiveBadge { get; set; }

    public int Experience { get; set; }

    [NotMapped]
    public int Level => Leveling.GetLevelFromExp(Experience);
    [NotMapped]
    public double LevelingProgress => Leveling.GetProgressToNextLevel(Experience);

    public double SocialCredits { get; set; } = 100;

    [NotMapped]
    public int SocialCreditsLevel => SocialCredits switch
    {
        < 100 => -1,
        > 100 and < 200 => 0,
        < 200 => 1,
        _ => 2
    };

    [Column(TypeName = "jsonb")] public SnCloudFileReferenceObject? Picture { get; set; }
    [Column(TypeName = "jsonb")] public SnCloudFileReferenceObject? Background { get; set; }

    public Guid AccountId { get; set; }
    [JsonIgnore] public SnAccount Account { get; set; } = null!;

    public Proto.AccountProfile ToProtoValue()
    {
        var proto = new Proto.AccountProfile
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
            SocialCredits = SocialCredits,
            SocialCreditsLevel = SocialCreditsLevel,
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

    public static SnAccountProfile FromProtoValue(Proto.AccountProfile proto)
    {
        var profile = new SnAccountProfile
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
            Verification = proto.Verification is null ? null : SnVerificationMark.FromProtoValue(proto.Verification),
            ActiveBadge = proto.ActiveBadge is null ? null : SnAccountBadgeRef.FromProtoValue(proto.ActiveBadge),
            Experience = proto.Experience,
            SocialCredits = proto.SocialCredits,
            Picture = proto.Picture is null ? null : SnCloudFileReferenceObject.FromProtoValue(proto.Picture),
            Background = proto.Background is null ? null : SnCloudFileReferenceObject.FromProtoValue(proto.Background),
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

public class SnAccountContact : ModelBase
{
    public Guid Id { get; set; }
    public AccountContactType Type { get; set; }
    public Instant? VerifiedAt { get; set; }
    public bool IsPrimary { get; set; } = false;
    public bool IsPublic { get; set; } = false;
    [MaxLength(1024)] public string Content { get; set; } = string.Empty;

    public Guid AccountId { get; set; }
    [JsonIgnore] public SnAccount Account { get; set; } = null!;

    public Proto.AccountContact ToProtoValue()
    {
        var proto = new Proto.AccountContact
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

    public static SnAccountContact FromProtoValue(Proto.AccountContact proto)
    {
        var contact = new SnAccountContact
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

public class SnAccountAuthFactor : ModelBase
{
    public Guid Id { get; set; }
    public AccountAuthFactorType Type { get; set; }
    [JsonIgnore][MaxLength(8196)] public string? Secret { get; set; }

    [JsonIgnore]
    [Column(TypeName = "jsonb")]
    public Dictionary<string, object>? Config { get; set; } = [];

    /// <summary>
    /// The trustworthy stands for how safe is this auth factor.
    /// Basically, it affects how many steps it can complete in authentication.
    /// Besides, users may need to use some high-trustworthy level auth factors when confirming some dangerous operations.
    /// </summary>
    public int Trustworthy { get; set; } = 1;

    public Instant? EnabledAt { get; set; }
    public Instant? ExpiredAt { get; set; }

    public Guid AccountId { get; set; }
    [JsonIgnore] public SnAccount Account { get; set; } = null!;

    public SnAccountAuthFactor HashSecret(int cost = 12)
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

public class SnAccountConnection : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(4096)] public string Provider { get; set; } = null!;
    [MaxLength(8192)] public string ProvidedIdentifier { get; set; } = null!;
    [Column(TypeName = "jsonb")] public Dictionary<string, object>? Meta { get; set; } = [];

    [JsonIgnore][MaxLength(4096)] public string? AccessToken { get; set; }
    [JsonIgnore][MaxLength(4096)] public string? RefreshToken { get; set; }
    public Instant? LastUsedAt { get; set; }

    public Guid AccountId { get; set; }
    public SnAccount Account { get; set; } = null!;
}

using DysonNetwork.Shared.Proto;
using NodaTime;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Shared.Data;

public class AccountReference : ModelBase
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Nick { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public Instant? ActivatedAt { get; set; }
    public bool IsSuperuser { get; set; }
    public Guid? AutomatedId { get; set; }
    public AccountProfileReference Profile { get; set; } = null!;
    public List<AccountContactReference> Contacts { get; set; } = new();
    public List<AccountBadgeReference> Badges { get; set; } = new();
    public SubscriptionReference? PerkSubscription { get; set; }

    public Proto.Account ToProtoValue()
    {
        var proto = new Proto.Account
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

        foreach (var contact in Contacts)
            proto.Contacts.Add(contact.ToProtoValue());

        foreach (var badge in Badges)
            proto.Badges.Add(badge.ToProtoValue());

        return proto;
    }

    public static AccountReference FromProtoValue(Proto.Account proto)
    {
        var account = new AccountReference
        {
            Id = Guid.Parse(proto.Id),
            Name = proto.Name,
            Nick = proto.Nick,
            Language = proto.Language,
            ActivatedAt = proto.ActivatedAt?.ToInstant(),
            IsSuperuser = proto.IsSuperuser,
            AutomatedId = string.IsNullOrEmpty(proto.AutomatedId) ? null : Guid.Parse(proto.AutomatedId),
            PerkSubscription = proto.PerkSubscription != null
                ? SubscriptionReference.FromProtoValue(proto.PerkSubscription)
                : null,
            CreatedAt = proto.CreatedAt.ToInstant(),
            UpdatedAt = proto.UpdatedAt.ToInstant()
        };

        account.Profile = AccountProfileReference.FromProtoValue(proto.Profile);

        foreach (var contactProto in proto.Contacts)
            account.Contacts.Add(AccountContactReference.FromProtoValue(contactProto));

        foreach (var badgeProto in proto.Badges)
            account.Badges.Add(AccountBadgeReference.FromProtoValue(badgeProto));

        return account;
    }
}

public class AccountProfileReference : ModelBase
{
    public Guid Id { get; set; }
    public string? FirstName { get; set; }
    public string? MiddleName { get; set; }
    public string? LastName { get; set; }
    public string? Bio { get; set; }
    public string? Gender { get; set; }
    public string? Pronouns { get; set; }
    public string? TimeZone { get; set; }
    public string? Location { get; set; }
    public List<ProfileLinkReference>? Links { get; set; }
    public Instant? Birthday { get; set; }
    public Instant? LastSeenAt { get; set; }
    public VerificationMark? Verification { get; set; }
    public int Experience { get; set; }
    public int Level => Leveling.ExperiencePerLevel.Count(xp => Experience >= xp) - 1;
    public double SocialCredits { get; set; } = 100;

    public int SocialCreditsLevel => SocialCredits switch
    {
        < 100 => -1,
        > 100 and < 200 => 0,
        < 200 => 1,
        _ => 2
    };

    public double LevelingProgress => Level >= Leveling.ExperiencePerLevel.Count - 1
        ? 100
        : (Experience - Leveling.ExperiencePerLevel[Level]) * 100.0 /
          (Leveling.ExperiencePerLevel[Level + 1] - Leveling.ExperiencePerLevel[Level]);

    public CloudFileReferenceObject? Picture { get; set; }
    public CloudFileReferenceObject? Background { get; set; }
    public Guid AccountId { get; set; }

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
            CreatedAt = CreatedAt.ToTimestamp(),
            UpdatedAt = UpdatedAt.ToTimestamp()
        };

        return proto;
    }

    public static AccountProfileReference FromProtoValue(Proto.AccountProfile proto)
    {
        return new AccountProfileReference
        {
            Id = Guid.Parse(proto.Id),
            FirstName = string.IsNullOrEmpty(proto.FirstName) ? null : proto.FirstName,
            MiddleName = string.IsNullOrEmpty(proto.MiddleName) ? null : proto.MiddleName,
            LastName = string.IsNullOrEmpty(proto.LastName) ? null : proto.LastName,
            Bio = string.IsNullOrEmpty(proto.Bio) ? null : proto.Bio,
            Gender = string.IsNullOrEmpty(proto.Gender) ? null : proto.Gender,
            Pronouns = string.IsNullOrEmpty(proto.Pronouns) ? null : proto.Pronouns,
            TimeZone = string.IsNullOrEmpty(proto.TimeZone) ? null : proto.TimeZone,
            Location = string.IsNullOrEmpty(proto.Location) ? null : proto.Location,
            Birthday = proto.Birthday?.ToInstant(),
            LastSeenAt = proto.LastSeenAt?.ToInstant(),
            Experience = proto.Experience,
            SocialCredits = proto.SocialCredits,
            Picture = proto.Picture != null ? CloudFileReferenceObject.FromProtoValue(proto.Picture) : null,
            Background = proto.Background != null ? CloudFileReferenceObject.FromProtoValue(proto.Background) : null,
            AccountId = Guid.Parse(proto.AccountId),
            Verification = proto.Verification != null ? VerificationMark.FromProtoValue(proto.Verification) : null,
            CreatedAt = proto.CreatedAt.ToInstant(),
            UpdatedAt = proto.UpdatedAt.ToInstant()
        };
    }
}

public class AccountContactReference : ModelBase
{
    public Guid Id { get; set; }
    public AccountContactReferenceType Type { get; set; }
    public Instant? VerifiedAt { get; set; }
    public bool IsPrimary { get; set; } = false;
    public bool IsPublic { get; set; } = false;
    public string Content { get; set; } = string.Empty; 
    
    public Guid AccountId { get; set; }
    
    public Shared.Proto.AccountContact ToProtoValue()
    {
        var proto = new Shared.Proto.AccountContact
        {
            Id = Id.ToString(),
            Type = Type switch
            {
                AccountContactReferenceType.Email => Shared.Proto.AccountContactType.Email,
                AccountContactReferenceType.PhoneNumber => Shared.Proto.AccountContactType.PhoneNumber,
                AccountContactReferenceType.Address => Shared.Proto.AccountContactType.Address,
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

    public static AccountContactReference FromProtoValue(Shared.Proto.AccountContact proto)
    {
        var contact = new AccountContactReference
        {
            Id = Guid.Parse(proto.Id),
            AccountId = Guid.Parse(proto.AccountId),
            Type = proto.Type switch
            {
                Shared.Proto.AccountContactType.Email => AccountContactReferenceType.Email,
                Shared.Proto.AccountContactType.PhoneNumber => AccountContactReferenceType.PhoneNumber,
                Shared.Proto.AccountContactType.Address => AccountContactReferenceType.Address,
                _ => AccountContactReferenceType.Email
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

public enum AccountContactReferenceType
{
    Email,
    PhoneNumber,
    Address
}

public class AccountBadgeReference : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Type { get; set; } = null!;
    public string? Label { get; set; }
    public string? Caption { get; set; }
    public Dictionary<string, object?> Meta { get; set; } = new();
    public Instant? ActivatedAt { get; set; }
    public Instant? ExpiredAt { get; set; }

    public Guid AccountId { get; set; }

    public AccountBadge ToProtoValue()
    {
        var proto = new AccountBadge
        {
            Id = Id.ToString(),
            Type = Type,
            Label = Label ?? string.Empty,
            Caption = Caption ?? string.Empty,
            ActivatedAt = ActivatedAt?.ToTimestamp(),
            ExpiredAt = ExpiredAt?.ToTimestamp(),
            AccountId = AccountId.ToString(),
            CreatedAt = CreatedAt.ToTimestamp(),
            UpdatedAt = UpdatedAt.ToTimestamp()
        };
        proto.Meta.Add(GrpcTypeHelper.ConvertToValueMap(Meta));

        return proto;
    }

    public static AccountBadgeReference FromProtoValue(AccountBadge proto)
    {
        var badge = new AccountBadgeReference
        {
            Id = Guid.Parse(proto.Id),
            AccountId = Guid.Parse(proto.AccountId),
            Type = proto.Type,
            Label = proto.Label,
            Caption = proto.Caption,
            ActivatedAt = proto.ActivatedAt?.ToInstant(),
            ExpiredAt = proto.ExpiredAt?.ToInstant(),
            CreatedAt = proto.CreatedAt.ToInstant(),
            UpdatedAt = proto.UpdatedAt.ToInstant()
        };

        return badge;
    }
}

public class ProfileLinkReference
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}

public static class Leveling
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
        1024000
    ];
}

public class ApiKeyReference : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Label { get; set; } = null!;

    public Guid AccountId { get; set; }
    public Guid SessionId { get; set; }

    public string? Key { get; set; }

    public ApiKey ToProtoValue()
    {
        return new ApiKey
        {
            Id = Id.ToString(),
            Label = Label,
            AccountId = AccountId.ToString(),
            SessionId = SessionId.ToString(),
            Key = Key
        };
    }

    public static ApiKeyReference FromProtoValue(ApiKey proto)
    {
        return new ApiKeyReference
        {
            Id = Guid.Parse(proto.Id),
            AccountId = Guid.Parse(proto.AccountId),
            SessionId = Guid.Parse(proto.SessionId),
            Label = proto.Label,
            Key = proto.Key
        };
    }
}
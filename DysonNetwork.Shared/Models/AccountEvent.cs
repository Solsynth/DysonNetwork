using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Proto;
using NodaTime;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Shared.Models;

public enum StatusAttitude
{
    Positive,
    Negative,
    Neutral,
}

public enum StatusType
{
    Default,
    Busy,
    DoNotDisturb,
    Invisible,
}

public class SnAccountStatus : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public StatusAttitude Attitude { get; set; }

    [NotMapped]
    public bool IsOnline { get; set; }

    [NotMapped]
    public bool IsIdle { get; set; }

    [NotMapped]
    public Instant? IdleSince { get; set; }

    [NotMapped]
    public bool IsCustomized { get; set; } = true;
    public StatusType Type { get; set; } = StatusType.Default;

    [MaxLength(1024)]
    public string? Label { get; set; }

    [MaxLength(128)]
    public string? Symbol { get; set; }

    [Column(TypeName = "jsonb")]
    public SnCloudFileReferenceObject? Icon { get; set; }

    [Column(TypeName = "jsonb")]
    public SnCloudFileReferenceObject? Background { get; set; }

    [Column(TypeName = "jsonb")]
    public Dictionary<string, object>? Meta { get; set; }
    public Instant? ClearedAt { get; set; }

    [MaxLength(4096)]
    public string? AppIdentifier { get; set; }

    /// <summary>
    /// Indicates this status is created based on running process or rich presence
    /// </summary>
    public bool IsAutomated { get; set; }

    public Guid AccountId { get; set; }
    [NotMapped] public SnAccount Account { get; set; } = null!;

    public DyAccountStatus ToProtoValue()
    {
        var proto = new DyAccountStatus
        {
            Id = Id.ToString(),
            Attitude = Attitude switch
            {
                StatusAttitude.Positive => DyStatusAttitude.DyPositive,
                StatusAttitude.Negative => DyStatusAttitude.DyNegative,
                StatusAttitude.Neutral => DyStatusAttitude.DyNeutral,
                _ => DyStatusAttitude.Unspecified,
            },
            IsOnline = IsOnline,
            IsIdle = IsIdle,
            IsCustomized = IsCustomized,
            IsInvisible = Type == StatusType.Invisible,
            IsNotDisturb = Type == StatusType.DoNotDisturb,
            Label = Label ?? string.Empty,
            Type = (DyAccountStatusType)Type,
            Symbol = Symbol ?? string.Empty,
            Meta = InfraObjectCoder.ConvertObjectToByteString(Meta),
            ClearedAt = ClearedAt?.ToTimestamp(),
            AccountId = AccountId.ToString(),
            AppIdentifier = AppIdentifier ?? string.Empty,
            IsAutomated = IsAutomated,
            CreatedAt = CreatedAt.ToTimestamp(),
            UpdatedAt = UpdatedAt.ToTimestamp(),
            DeletedAt = DeletedAt?.ToTimestamp(),
            IdleSince = IdleSince?.ToTimestamp(),
        };

        return proto;
    }

    public static SnAccountStatus FromProtoValue(DyAccountStatus proto)
    {
        var status = new SnAccountStatus
        {
            Id = Guid.Parse(proto.Id),
            Attitude = proto.Attitude switch
            {
                DyStatusAttitude.DyPositive => StatusAttitude.Positive,
                DyStatusAttitude.DyNegative => StatusAttitude.Negative,
                _ => StatusAttitude.Neutral,
            },
            IsOnline = proto.IsOnline,
            IsIdle = proto.IsIdle,
            IdleSince = proto.IdleSince?.ToInstant(),
            IsCustomized = proto.IsCustomized,
            Type = proto.Type != DyAccountStatusType.Default
                ? Enum.IsDefined(typeof(StatusType), (int)proto.Type) ? (StatusType)proto.Type : StatusType.Default
                : proto.IsInvisible
                    ? StatusType.Invisible
                    : proto.IsNotDisturb
                        ? StatusType.DoNotDisturb
                        : StatusType.Default,
            Label = proto.Label,
            Symbol = string.IsNullOrWhiteSpace(proto.Symbol) ? null : proto.Symbol,
            Meta = InfraObjectCoder.ConvertByteStringToObject<Dictionary<string, object>>(proto.Meta),
            ClearedAt = proto.ClearedAt?.ToInstant(),
            AccountId = Guid.Parse(proto.AccountId),
            AppIdentifier = string.IsNullOrWhiteSpace(proto.AppIdentifier) ? null : proto.AppIdentifier,
            IsAutomated = proto.IsAutomated,
            CreatedAt = proto.CreatedAt?.ToInstant() ?? default,
            UpdatedAt = proto.UpdatedAt?.ToInstant() ?? default,
            DeletedAt = proto.DeletedAt?.ToInstant(),
        };

        return status;
    }
}

public enum CheckInResultLevel
{
    Worst,
    Worse,
    Normal,
    Better,
    Best,
    Special,
}

public class SnCheckInResult : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public CheckInResultLevel Level { get; set; }
    public decimal? RewardPoints { get; set; }
    public int? RewardExperience { get; set; }

    [Column(TypeName = "jsonb")]
    public List<CheckInFortuneTip> Tips { get; set; } = new List<CheckInFortuneTip>();

    [Column(TypeName = "jsonb")]
    public CheckInFortuneReport? FortuneReport { get; set; }

    public Guid AccountId { get; set; }
    [NotMapped] public SnAccount Account { get; set; } = null!;

    public Instant? BackdatedFrom { get; set; }
}

public class CheckInFortuneTip
{
    public bool IsPositive { get; set; }
    public string Title { get; set; } = null!;
    public string Content { get; set; } = null!;
}

public class CheckInFortuneReport
{
    public int Version { get; set; }
    public string Poem { get; set; } = null!;
    public string Summary { get; set; } = null!;
    public string SummaryDetail { get; set; } = null!;
    public string Wish { get; set; } = null!;
    public string Love { get; set; } = null!;
    public string Study { get; set; } = null!;
    public string Career { get; set; } = null!;
    public string Health { get; set; } = null!;
    public string LostItem { get; set; } = null!;
    public string LuckyColor { get; set; } = null!;
    public string LuckyDirection { get; set; } = null!;
    public string LuckyTime { get; set; } = null!;
    public string LuckyItem { get; set; } = null!;
    public string LuckyAction { get; set; } = null!;
    public string AvoidAction { get; set; } = null!;
    public string Ritual { get; set; } = null!;
}

/// <summary>
/// This method should not be mapped. Used to generate the daily event calendar.
/// </summary>
public class DailyEventResponse
{
    public Instant Date { get; set; }
    public SnCheckInResult? CheckInResult { get; set; }
    public List<SnAccountStatus> Statuses { get; set; } = new List<SnAccountStatus>();
    public List<UserCalendarEventDto> UserEvents { get; set; } = new List<UserCalendarEventDto>();
    public List<NotableDay> NotableDays { get; set; } = new List<NotableDay>();
}

/// <summary>
/// Notable day/holiday for calendar display
/// </summary>
public class NotableDay
{
    public Instant Date { get; set; }
    public string? LocalName { get; set; }
    public string? GlobalName { get; set; }
    public string? LocalizableKey { get; set; }
    public string? CountryCode { get; set; }
    public NotableHolidayType[] Holidays { get; set; } = [];
}

public enum NotableDayTag
{
    Holiday,
    Event,
    Anniversary,
    Memorial,
    Festival,
}

/// <summary>
/// Local notable day stored in database, supports multi-day periods
/// </summary>
public class SnNotableDay : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(256)]
    public string Name { get; set; } = null!;

    [MaxLength(4096)]
    public string? Description { get; set; }

    [MaxLength(256)]
    public string? LocalName { get; set; }

    [MaxLength(256)]
    public string? LocalizableKey { get; set; }

    public Instant StartDate { get; set; }
    public Instant EndDate { get; set; }
    public bool IsAllDay { get; set; } = true;

    [MaxLength(8)]
    public string Region { get; set; } = "CN";

    [Column(TypeName = "jsonb")]
    public List<NotableDayTag> Tags { get; set; } = [];

    [Column(TypeName = "jsonb")]
    public Dictionary<string, object>? Meta { get; set; }

    public bool IsRecurring { get; set; }

    /// <summary>
    /// For recurring events: lunar calendar month/day (e.g., "01-01" for Spring Festival)
    /// or solar calendar pattern (e.g., "05-01" for Labour Day)
    /// </summary>
    [MaxLength(16)]
    public string? RecurrencePattern { get; set; }

    /// <summary>
    /// Whether this is a holiday period (multiple days off) or a single day event
    /// </summary>
    public bool IsPeriod { get; set; }

    /// <summary>
    /// For period holidays: the official holiday days within the period
    /// Stored as JSON array of date strings
    /// </summary>
    [Column(TypeName = "jsonb")]
    public List<string>? HolidayDays { get; set; }

    public int? DisplayOrder { get; set; }

    public NotableDay ToNotableDay()
    {
        return new NotableDay
        {
            Date = StartDate,
            LocalName = LocalName ?? Name,
            GlobalName = Name,
            LocalizableKey = LocalizableKey,
            CountryCode = Region,
            Holidays = Tags.Contains(NotableDayTag.Holiday)
                ? [NotableHolidayType.Public]
                : [],
        };
    }
}

/// <summary>
/// Represents a calendar event with merged data from multiple sources
/// </summary>
public class MergedCalendarEvent
{
    public Guid? Id { get; set; }
    public CalendarEventType Type { get; set; }
    public string Title { get; set; } = null!;
    public string? Description { get; set; }
    public string? Location { get; set; }
    public Instant StartTime { get; set; }
    public Instant EndTime { get; set; }
    public bool IsAllDay { get; set; }
    public Dictionary<string, object>? Meta { get; set; }
}

public enum CalendarEventType
{
    UserEvent,
    CheckIn,
    Status,
    NotableDay,
}

/// <summary>
/// Type of notable holiday
/// </summary>
public enum NotableHolidayType
{
    Public,
    Bank,
    School,
    Authorities,
    Optional,
    Observance,
}

/// <summary>
/// Extended daily response that includes merged events
/// </summary>
public class MergedDailyEventResponse
{
    public Instant Date { get; set; }
    public SnCheckInResult? CheckInResult { get; set; }
    public List<SnAccountStatus> Statuses { get; set; } = new List<SnAccountStatus>();
    public List<UserCalendarEventDto> UserEvents { get; set; } = new List<UserCalendarEventDto>();
    public List<NotableDay> NotableDays { get; set; } = new List<NotableDay>();
    public List<MergedCalendarEvent> MergedEvents { get; set; } = new List<MergedCalendarEvent>();
}

public enum PresenceType
{
    Unknown,
    Gaming,
    Music,
    Workout,
}

public class SnPresenceActivity : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public PresenceType Type { get; set; } = PresenceType.Unknown;

    [MaxLength(256)]
    public string? Provider { get; set; }

    [MaxLength(1024)]
    public string? ReferenceId { get; set; }

    [MaxLength(4096)]
    public string? ManualId { get; set; }

    [MaxLength(4096)]
    public string? Title { get; set; }

    [MaxLength(4096)]
    public string? Subtitle { get; set; }

    [MaxLength(4096)]
    public string? Caption { get; set; }

    [MaxLength(4096)]
    public string? LargeImage { get; set; }

    [MaxLength(4096)]
    public string? SmallImage { get; set; }

    [MaxLength(4096)]
    public string? TitleUrl { get; set; }

    [MaxLength(4096)]
    public string? SubtitleUrl { get; set; }

    [Column(TypeName = "jsonb")]
    public string[] QueryableTerms { get; set; } = [];

    [Column(TypeName = "jsonb")]
    public Dictionary<string, object>? Meta { get; set; }

    public int LeaseMinutes { get; set; } = 5; // Lease period in minutes (1-60)
    public Instant LeaseExpiresAt { get; set; }

    public Guid AccountId { get; set; }
    [NotMapped] public SnAccount Account { get; set; } = null!;

    public DyPresenceActivity ToProtoValue()
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var isActive = LeaseExpiresAt > now && DeletedAt == null;

        var proto = new DyPresenceActivity
        {
            Id = Id.ToString(),
            Type = (DyPresenceType)Type,
            LeaseMinutes = LeaseMinutes,
            LeaseExpiresAt = LeaseExpiresAt.ToTimestamp(),
            AccountId = AccountId.ToString(),
            CreatedAt = CreatedAt.ToTimestamp(),
            UpdatedAt = UpdatedAt.ToTimestamp(),
            IsActive = isActive,
        };

        if (!string.IsNullOrWhiteSpace(Provider))
            proto.Meta ??= new Google.Protobuf.WellKnownTypes.Struct();
        if (!string.IsNullOrWhiteSpace(ReferenceId))
            proto.Meta ??= new Google.Protobuf.WellKnownTypes.Struct();
        if (QueryableTerms.Length > 0)
            proto.Meta ??= new Google.Protobuf.WellKnownTypes.Struct();

        if (!string.IsNullOrWhiteSpace(ManualId))
            proto.ManualId = ManualId;
        if (!string.IsNullOrWhiteSpace(Title))
            proto.Title = Title;
        if (!string.IsNullOrWhiteSpace(Subtitle))
            proto.Subtitle = Subtitle;
        if (!string.IsNullOrWhiteSpace(Caption))
            proto.Caption = Caption;
        if (!string.IsNullOrWhiteSpace(LargeImage))
            proto.LargeImage = LargeImage;
        if (!string.IsNullOrWhiteSpace(SmallImage))
            proto.SmallImage = SmallImage;
        if (!string.IsNullOrWhiteSpace(TitleUrl))
            proto.TitleUrl = TitleUrl;
        if (!string.IsNullOrWhiteSpace(SubtitleUrl))
            proto.SubtitleUrl = SubtitleUrl;

        if (Meta != null)
        {
            proto.Meta ??= new Google.Protobuf.WellKnownTypes.Struct();
            proto.Meta.Fields.Add(InfraObjectCoder.ConvertToValueMap(Meta));
        }

        if (proto.Meta != null)
        {
            if (!string.IsNullOrWhiteSpace(Provider))
                proto.Meta.Fields["provider"] = Google.Protobuf.WellKnownTypes.Value.ForString(Provider);
            if (!string.IsNullOrWhiteSpace(ReferenceId))
                proto.Meta.Fields["reference_id"] = Google.Protobuf.WellKnownTypes.Value.ForString(ReferenceId);
            if (QueryableTerms.Length > 0)
                proto.Meta.Fields["queryable_terms"] = new Google.Protobuf.WellKnownTypes.Value
                {
                    ListValue = new Google.Protobuf.WellKnownTypes.ListValue
                    {
                        Values =
                        {
                            QueryableTerms.Select(Google.Protobuf.WellKnownTypes.Value.ForString)
                        }
                    }
                };
        }

        if (DeletedAt.HasValue)
            proto.DeletedAt = DeletedAt.Value.ToTimestamp();

        if (!isActive)
            proto.EndedAt = (DeletedAt ?? LeaseExpiresAt).ToTimestamp();

        return proto;
    }

    public static SnPresenceActivity FromProtoValue(DyPresenceActivity proto)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var activity = new SnPresenceActivity
        {
            Id = Guid.TryParse(proto.Id, out var id) ? id : Guid.NewGuid(),
            Type = (PresenceType)proto.Type,
            ManualId = proto.HasManualId ? proto.ManualId : null,
            Title = proto.HasTitle ? proto.Title : null,
            Subtitle = proto.HasSubtitle ? proto.Subtitle : null,
            Caption = proto.HasCaption ? proto.Caption : null,
            LargeImage = proto.HasLargeImage ? proto.LargeImage : null,
            SmallImage = proto.HasSmallImage ? proto.SmallImage : null,
            TitleUrl = proto.HasTitleUrl ? proto.TitleUrl : null,
            SubtitleUrl = proto.HasSubtitleUrl ? proto.SubtitleUrl : null,
            LeaseMinutes = proto.LeaseMinutes,
            LeaseExpiresAt = proto.LeaseExpiresAt?.ToInstant() ?? now,
            AccountId = Guid.Parse(proto.AccountId),
            CreatedAt = proto.CreatedAt?.ToInstant() ?? now,
            UpdatedAt = proto.UpdatedAt?.ToInstant() ?? now,
        };

        if (proto.Meta != null)
            activity.Meta = InfraObjectCoder.ConvertFromValueMap(proto.Meta.Fields)
                .Where(kvp => kvp.Value != null)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value!);

        if (activity.Meta != null)
        {
            if (activity.Meta.TryGetValue("provider", out var provider) && provider is string providerValue)
                activity.Provider = providerValue;

            if (activity.Meta.TryGetValue("reference_id", out var referenceId) && referenceId is string referenceIdValue)
                activity.ReferenceId = referenceIdValue;

            if (activity.Meta.TryGetValue("queryable_terms", out var queryableTerms))
            {
                activity.QueryableTerms = queryableTerms is IEnumerable<object> items
                    ? items.OfType<string>()
                        .Where(term => !string.IsNullOrWhiteSpace(term))
                        .ToArray()
                    : [];
            }
        }

        if (proto.DeletedAt != null)
            activity.DeletedAt = proto.DeletedAt.ToInstant();

        return activity;
    }
}

public enum TimelineEventType
{
    StatusChange,
    Activity,
}

public class AccountTimelineItem
{
    public Guid Id { get; set; }
    public Instant CreatedAt { get; set; }
    public TimelineEventType EventType { get; set; }
    public SnAccountStatus? Status { get; set; }
    public SnPresenceActivity? Activity { get; set; }
}

public enum EventVisibility
{
    Private = 0,
    Friends = 100,
    Public = 200,
}

public enum RecurrenceFrequency
{
    None,
    Daily,
    Weekly,
    Monthly,
    Yearly,
}

public class RecurrencePattern
{
    public RecurrenceFrequency Frequency { get; set; } = RecurrenceFrequency.None;
    public int Interval { get; set; } = 1;
    public Instant? EndDate { get; set; }
    public int? Occurrences { get; set; }
    public List<IsoDayOfWeek>? DaysOfWeek { get; set; }
    public int? DayOfMonth { get; set; }
    public int? MonthOfYear { get; set; }
}

public class SnUserCalendarEvent : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(256)]
    public string Title { get; set; } = null!;

    [MaxLength(4096)]
    public string? Description { get; set; }

    [MaxLength(512)]
    public string? Location { get; set; }

    public Instant StartTime { get; set; }
    public Instant EndTime { get; set; }
    public bool IsAllDay { get; set; }

    public EventVisibility Visibility { get; set; } = EventVisibility.Private;

    [Column(TypeName = "jsonb")]
    public RecurrencePattern? Recurrence { get; set; }

    [Column(TypeName = "jsonb")]
    public Dictionary<string, object>? Meta { get; set; }

    public Guid AccountId { get; set; }
    [NotMapped] public SnAccount Account { get; set; } = null!;
}

public class UserCalendarEventDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = null!;
    public string? Description { get; set; }
    public string? Location { get; set; }
    public Instant StartTime { get; set; }
    public Instant EndTime { get; set; }
    public bool IsAllDay { get; set; }
    public EventVisibility Visibility { get; set; }
    public RecurrencePattern? Recurrence { get; set; }
    public Dictionary<string, object>? Meta { get; set; }
    public Guid AccountId { get; set; }
    public Instant CreatedAt { get; set; }
    public Instant UpdatedAt { get; set; }
}

public class CreateCalendarEventRequest
{
    public string Title { get; set; } = null!;
    public string? Description { get; set; }
    public string? Location { get; set; }
    public Instant StartTime { get; set; }
    public Instant EndTime { get; set; }
    public bool IsAllDay { get; set; }
    public EventVisibility Visibility { get; set; } = EventVisibility.Private;
    public RecurrencePattern? Recurrence { get; set; }
    public Dictionary<string, object>? Meta { get; set; }
}

public class UpdateCalendarEventRequest
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Location { get; set; }
    public Instant? StartTime { get; set; }
    public Instant? EndTime { get; set; }
    public bool? IsAllDay { get; set; }
    public EventVisibility? Visibility { get; set; }
    public RecurrencePattern? Recurrence { get; set; }
    public Dictionary<string, object>? Meta { get; set; }
}

/// <summary>
/// Countdown item for upcoming events
/// </summary>
public class EventCountdownItem
{
    public Guid? EventId { get; set; }
    public CalendarEventType EventType { get; set; }
    public string Title { get; set; } = null!;
    public string? Description { get; set; }
    public string? Location { get; set; }
    public Instant StartTime { get; set; }
    public Instant EndTime { get; set; }
    public bool IsAllDay { get; set; }
    public int DaysRemaining { get; set; }
    public int HoursRemaining { get; set; }
    public bool IsOngoing { get; set; }
    public Dictionary<string, object>? Meta { get; set; }
    public Guid? AccountId { get; set; }
}

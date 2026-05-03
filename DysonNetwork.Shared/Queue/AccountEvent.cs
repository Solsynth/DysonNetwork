using System;
using DysonNetwork.Shared.EventBus;
using DysonNetwork.Shared.Models;
using NodaTime;

namespace DysonNetwork.Shared.Queue;

public class AccountDeletedEvent : EventBase
{
    public static string Type => "account.deleted";
    public override string EventType => Type;
    public override string StreamName => "account_events";

    public Guid AccountId { get; set; } = Guid.NewGuid();
    public Instant DeletedAt { get; set; } = SystemClock.Instance.GetCurrentInstant();
}

public class AccountCreatedEvent : EventBase
{
    public static string Type => "accounts.created";
    public override string EventType => Type;
    public override string StreamName => "account_events";

    public Guid AccountId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Nick { get; set; } = string.Empty;
    public string Language { get; set; } = "en-US";
    public string Region { get; set; } = "en";
    public string? PrimaryEmail { get; set; }
    public Instant? PrimaryEmailVerifiedAt { get; set; }
    public Instant? ActivatedAt { get; set; }
    public bool IsSuperuser { get; set; }
    public Instant CreatedAt { get; set; } = SystemClock.Instance.GetCurrentInstant();
}

public class AccountActivatedEvent : EventBase
{
    public static string Type => "accounts.activated";
    public override string EventType => Type;
    public override string StreamName => "account_events";

    public Guid AccountId { get; set; }
    public Instant ActivatedAt { get; set; } = SystemClock.Instance.GetCurrentInstant();
}

public class AccountContactVerifiedEvent : EventBase
{
    public static string Type => "accounts.contacts.verified";
    public override string EventType => Type;
    public override string StreamName => "account_events";

    public Guid AccountId { get; set; }
    public Guid ContactId { get; set; }
    public Guid SpellId { get; set; }
    public Instant VerifiedAt { get; set; } = SystemClock.Instance.GetCurrentInstant();
}

public class AccountPresenceActivitiesUpdatedEvent : EventBase
{
    public static string Type => "account.presence.activities.updated";
    public override string EventType => Type;
    public override string StreamName => "account_events";

    public Guid AccountId { get; set; }
    public List<SnPresenceActivity> Activities { get; set; } = [];
}

public class AccountRemovalConfirmedEvent : EventBase
{
    public static string Type => "accounts.removal.confirmed";
    public override string EventType => Type;
    public override string StreamName => "account_events";

    public Guid AccountId { get; set; }
    public Guid SpellId { get; set; }
    public Instant ConfirmedAt { get; set; } = SystemClock.Instance.GetCurrentInstant();
}

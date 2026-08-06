using DysonNetwork.Shared.EventBus;
using DysonNetwork.Shared.Models;
using NodaTime;

namespace DysonNetwork.Passport.Account;

/// <summary>
/// Published on <c>accounts.profile_updated</c> whenever a Passport-owned
/// feature mutates a denormalized <c>account_profiles</c> field that moved to
/// Stargate (last-seen touches, XP deltas, social-credit recomputes, active
/// badge and verification changes). Stargate consumes the event and applies
/// the patch to its own profile row.
/// </summary>
public class ProfileFieldUpdatedEvent : EventBase
{
    public static string Type => "accounts.profile_updated";
    public override string EventType => Type;
    public override string StreamName => "account_events";

    public Guid AccountId { get; set; }
    public Instant? LastSeenAt { get; set; }
    public int? Experience { get; set; }
    public int? ExperienceDelta { get; set; }
    public double? SocialCredits { get; set; }
    public SnAccountBadgeRef? ActiveBadge { get; set; }
    public SnVerificationMark? Verification { get; set; }
}

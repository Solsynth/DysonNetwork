using DysonNetwork.Shared.Models;
using NodaTime;

namespace DysonNetwork.Padlock.Auth;

public record QrLoginChallenge(
    Guid Id,
    Guid AuthChallengeId,
    Guid AccountId,
    string DeviceId,
    string? DeviceName,
    ClientPlatform Platform,
    QrLoginStatus Status,
    Instant CreatedAt,
    Instant ExpiresAt,
    Instant? ApprovedAt,
    Guid? ApprovedBySessionId,
    string? ApprovedDeviceId
);

public enum QrLoginStatus
{
    Pending,
    Scanned,
    Approved,
    Declined,
    Expired
}

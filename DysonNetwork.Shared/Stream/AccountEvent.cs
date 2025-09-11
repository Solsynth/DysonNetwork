using NodaTime;

namespace DysonNetwork.Shared.Stream;

public class AccountDeletedEvent
{
    public static string Type => "account_deleted";
    
    public Guid AccountId { get; set; } = Guid.NewGuid();
    public Instant DeletedAt { get; set; } = SystemClock.Instance.GetCurrentInstant();
}
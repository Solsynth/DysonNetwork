using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Models;
using NodaTime;

namespace DysonNetwork.Drive.Billing;

/// <summary>
/// The quota record stands for the extra quota that a user has.
/// For normal users, the quota is 1GiB.
/// For stellar program t1 users, the quota is 5GiB
/// For stellar program t2 users, the quota is 10GiB
/// For stellar program t3 users, the quota is 15GiB
///
/// If users want to increase the quota, they need to pay for it.
/// Each 1NSD they paid for one GiB.
///
/// But the quota record unit is MiB, the minimal billable unit.
/// </summary>
public class QuotaRecord : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    
    public long Quota { get; set; }
    
    public Instant? ExpiredAt { get; set; }
}
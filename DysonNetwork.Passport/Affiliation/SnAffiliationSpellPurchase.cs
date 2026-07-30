using DysonNetwork.Shared.Models;
using NodaTime;

namespace DysonNetwork.Passport.Affiliation;

public class SnAffiliationSpellPurchase : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public Guid OrderId { get; set; }
    public decimal Amount { get; set; }
    public Guid? SpellId { get; set; }
    public Instant? FulfilledAt { get; set; }
}

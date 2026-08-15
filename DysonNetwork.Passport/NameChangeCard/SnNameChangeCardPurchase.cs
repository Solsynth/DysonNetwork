using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Models;
using NodaTime;

namespace DysonNetwork.Passport.NameChangeCard;

public class SnNameChangeCardPurchase : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public Guid OrderId { get; set; }
    public decimal Amount { get; set; }
    public Instant? FulfilledAt { get; set; }
    public Instant? ConsumedAt { get; set; }
    [MaxLength(32)] public string? TargetType { get; set; }
    public Guid? TargetId { get; set; }
    [MaxLength(256)] public string? OldName { get; set; }
    [MaxLength(256)] public string? NewName { get; set; }
}

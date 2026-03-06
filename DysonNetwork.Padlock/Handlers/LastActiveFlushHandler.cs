using DysonNetwork.Shared.Models;
using NodaTime;

namespace DysonNetwork.Padlock.Handlers;

public class LastActiveInfo
{
    public required SnAccount Account { get; set; }
    public required SnAuthSession Session { get; set; }
    public required Instant SeenAt { get; set; }
}

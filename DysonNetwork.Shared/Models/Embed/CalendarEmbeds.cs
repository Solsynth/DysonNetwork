namespace DysonNetwork.Shared.Models.Embed;

public class NotableDayEmbed : EmbeddableBase
{
    public override string Type => "notable_day";
    public required Guid Id { get; set; }
}

public class CalendarEventEmbed : EmbeddableBase
{
    public override string Type => "calendar_event";
    public required Guid Id { get; set; }
}

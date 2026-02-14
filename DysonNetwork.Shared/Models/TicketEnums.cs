namespace DysonNetwork.Shared.Models;

public enum TicketType
{
    Support,
    BugReport,
    FeatureRequest,
    Billing,
    Other
}

public enum TicketStatus
{
    Open,
    InProgress,
    Resolved,
    Closed
}

public enum TicketPriority
{
    Low,
    Medium,
    High,
    Critical
}

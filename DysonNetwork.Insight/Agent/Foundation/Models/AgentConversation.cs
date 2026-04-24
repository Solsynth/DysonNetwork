namespace DysonNetwork.Insight.Agent.Foundation.Models;

public class AgentConversation
{
    public List<AgentMessage> Messages { get; init; } = new();
    public List<AgentToolDefinition>? Tools { get; init; }

    public AgentConversation() { }

    public AgentConversation(IEnumerable<AgentMessage> messages)
    {
        Messages = messages.ToList();
    }

    public AgentConversation AddSystemMessage(string content)
    {
        Messages.Add(AgentMessage.System(content));
        return this;
    }

    public AgentConversation AddUserMessage(string content)
    {
        Messages.Add(AgentMessage.User(content));
        return this;
    }

    public AgentConversation AddAssistantMessage(string content)
    {
        Messages.Add(AgentMessage.Assistant(content));
        return this;
    }

    public AgentConversation AddToolResult(string toolCallId, string result, bool isError = false)
    {
        Messages.Add(AgentMessage.FromToolResult(toolCallId, result, isError));
        return this;
    }
}

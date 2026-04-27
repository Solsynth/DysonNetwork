namespace DysonNetwork.Insight.Agent.Foundation;

using System.Text;
using System.Text.Json;
using DysonNetwork.Insight.Agent.Foundation.Models;
using DysonNetwork.Shared.Models;

public class ConversationBuilder
{
    private readonly List<AgentMessage> _messages = new();
    private List<AgentToolDefinition>? _tools;

    public ConversationBuilder WithTools(List<AgentToolDefinition>? tools)
    {
        _tools = tools;
        return this;
    }

    public ConversationBuilder AddSystemMessage(string content)
    {
        _messages.Add(AgentMessage.System(content));
        return this;
    }

    public ConversationBuilder AddUserMessage(string content)
    {
        _messages.Add(AgentMessage.User(content));
        return this;
    }

    public ConversationBuilder AddUserMessageWithImages(string text, List<SnCloudFileReferenceObject> imageFiles)
    {
        var parts = new List<AgentMessageContentPart>
        {
            new() { Type = AgentContentPartType.Text, Text = text }
        };

        foreach (var file in imageFiles.Where(f => f.MimeType?.StartsWith("image/") == true))
        {
            if (!string.IsNullOrEmpty(file.Url))
            {
                parts.Add(new AgentMessageContentPart
                {
                    Type = AgentContentPartType.ImageUrl,
                    ImageUrl = file.Url
                });
            }
        }

        _messages.Add(new AgentMessage
        {
            Role = AgentMessageRole.User,
            ContentParts = parts
        });
        return this;
    }

    public ConversationBuilder AddUserMessageWithFiles(string text, List<SnCloudFileReferenceObject> files)
    {
        var parts = new List<AgentMessageContentPart>
        {
            new() { Type = AgentContentPartType.Text, Text = text }
        };

        foreach (var file in files)
        {
            if (!string.IsNullOrWhiteSpace(file.Url))
            {
                parts.Add(new AgentMessageContentPart
                {
                    Type = AgentContentPartType.FileUrl,
                    FileUrl = file.Url,
                    FileMediaType = file.MimeType,
                    FileName = file.Name
                });
            }
        }

        _messages.Add(new AgentMessage
        {
            Role = AgentMessageRole.User,
            ContentParts = parts
        });

        return this;
    }

    public ConversationBuilder AddUserMessageWithInlineFiles(
        string text,
        List<(string fileName, string? mediaType, byte[] data)> files)
    {
        var parts = new List<AgentMessageContentPart>
        {
            new() { Type = AgentContentPartType.Text, Text = text }
        };

        foreach (var file in files.Where(f => f.data.Length > 0))
        {
            parts.Add(new AgentMessageContentPart
            {
                Type = AgentContentPartType.FileData,
                FileData = file.data,
                FileName = file.fileName,
                FileMediaType = file.mediaType
            });
        }

        _messages.Add(new AgentMessage
        {
            Role = AgentMessageRole.User,
            ContentParts = parts
        });

        return this;
    }

    public ConversationBuilder AddAssistantMessage(string content, List<AgentToolCall>? toolCalls = null)
    {
        _messages.Add(new AgentMessage
        {
            Role = AgentMessageRole.Assistant,
            Content = content,
            ToolCalls = toolCalls
        });
        return this;
    }

    public ConversationBuilder AddToolResult(string toolCallId, string result, bool isError = false)
    {
        _messages.Add(AgentMessage.FromToolResult(toolCallId, result, isError));
        return this;
    }

    public ConversationBuilder AddThoughts(IEnumerable<SnThinkingThought> thoughts)
    {
        foreach (var thought in thoughts)
        {
            var message = ConvertThoughtToMessage(thought);
            if (message != null)
            {
                _messages.Add(message);
            }
        }
        return this;
    }

    public AgentConversation Build()
    {
        return new AgentConversation(_messages) { Tools = _tools };
    }

    public static AgentMessage? ConvertThoughtToMessage(SnThinkingThought thought)
    {
        var role = thought.Role switch
        {
            ThinkingThoughtRole.User => AgentMessageRole.User,
            ThinkingThoughtRole.Assistant => AgentMessageRole.Assistant,
            ThinkingThoughtRole.System => AgentMessageRole.System,
            _ => AgentMessageRole.User
        };

        var parts = thought.Parts?.ToList() ?? new List<SnThinkingMessagePart>();
        var textParts = parts.Where(p => p.Type == ThinkingMessagePartType.Text).ToList();
        var toolCalls = parts.Where(p => p.Type == ThinkingMessagePartType.FunctionCall).ToList();
        var toolResults = parts.Where(p => p.Type == ThinkingMessagePartType.FunctionResult).ToList();
        var reasoningParts = parts.Where(p => p.Type == ThinkingMessagePartType.Reasoning).ToList();

        if (role == AgentMessageRole.Assistant && toolResults.Count > 0)
        {
            var toolResult = toolResults.First();
            return AgentMessage.FromToolResult(
                toolResult.FunctionResult?.CallId ?? "",
                toolResult.FunctionResult?.Result?.ToString() ?? "",
                toolResult.FunctionResult?.IsError ?? false
            );
        }

        var content = string.Join("\n", textParts.Select(p => p.Text ?? ""));

        var reasoningContent = string.Join("\n", reasoningParts.Select(p => p.Reasoning ?? ""));

        var message = new AgentMessage
        {
            Role = role,
            Content = content,
            ReasoningContent = string.IsNullOrWhiteSpace(reasoningContent) ? null : reasoningContent
        };

        if (toolCalls.Count > 0 && role == AgentMessageRole.Assistant)
        {
            message.ToolCalls = toolCalls.Select(tc => new AgentToolCall
            {
                Id = tc.FunctionCall?.Id ?? "",
                Name = string.IsNullOrEmpty(tc.FunctionCall?.PluginName)
                    ? tc.FunctionCall?.Name ?? ""
                    : $"{tc.FunctionCall.PluginName}-{tc.FunctionCall.Name}",
                Arguments = tc.FunctionCall?.Arguments ?? ""
            }).ToList();
        }

        return message;
    }

    public static List<AgentMessage> ConvertThoughtsToMessages(IEnumerable<SnThinkingThought> thoughts)
    {
        var messages = new List<AgentMessage>();
        foreach (var thought in thoughts)
        {
            var message = ConvertThoughtToMessage(thought);
            if (message != null)
            {
                messages.Add(message);
            }
        }
        return messages;
    }
}

namespace DysonNetwork.Insight.Agent.Foundation;

using System.Text.Json;
using DysonNetwork.Insight.Agent.Foundation.Models;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;

public class FoundationChatHistoryBuilder
{
    private readonly ILogger<FoundationChatHistoryBuilder> _logger;

    public FoundationChatHistoryBuilder(ILogger<FoundationChatHistoryBuilder> logger)
    {
        _logger = logger;
    }

    public AgentConversation BuildFromMessages(
        IEnumerable<SnThinkingThought> thoughts,
        string systemPrompt,
        List<AgentToolDefinition>? tools = null)
    {
        var conversation = new AgentConversation
        {
            Tools = tools
        };

        conversation.AddSystemMessage(systemPrompt);

        foreach (var thought in thoughts)
        {
            foreach (var message in ConvertThoughtToMessages(thought))
            {
                conversation.Messages.Add(message);
            }
        }

        return conversation;
    }

    public AgentConversation BuildSnChanConversation(
        IEnumerable<SnThinkingThought> thoughts,
        string systemPrompt,
        List<SnCloudFileReferenceObject>? attachedFiles = null,
        List<AgentToolDefinition>? tools = null)
    {
        var conversation = new AgentConversation
        {
            Tools = tools
        };

        conversation.AddSystemMessage(systemPrompt);

        foreach (var thought in thoughts)
        {
            foreach (var message in ConvertThoughtToMessages(thought, attachedFiles))
            {
                conversation.Messages.Add(message);
            }
        }

        return conversation;
    }

    private AgentMessage? ConvertThoughtToMessage(
        SnThinkingThought thought,
        List<SnCloudFileReferenceObject>? attachedFiles = null)
    {
        return ConvertThoughtToMessages(thought, attachedFiles).FirstOrDefault();
    }

    private List<AgentMessage> ConvertThoughtToMessages(
        SnThinkingThought thought,
        List<SnCloudFileReferenceObject>? attachedFiles = null)
    {
        var role = thought.Role switch
        {
            ThinkingThoughtRole.User => AgentMessageRole.User,
            ThinkingThoughtRole.Assistant => AgentMessageRole.Assistant,
            ThinkingThoughtRole.System => AgentMessageRole.System,
            _ => AgentMessageRole.User
        };

        var messages = new List<AgentMessage>();
        var parts = thought.Parts?.ToList() ?? new List<SnThinkingMessagePart>();
        var textParts = parts.Where(p => p.Type == ThinkingMessagePartType.Text).ToList();
        var toolCalls = parts.Where(p => p.Type == ThinkingMessagePartType.FunctionCall).ToList();
        var toolResults = parts.Where(p => p.Type == ThinkingMessagePartType.FunctionResult).ToList();
        var reasoningParts = parts.Where(p => p.Type == ThinkingMessagePartType.Reasoning).ToList();

        var content = string.Join("\n", textParts.Select(p => p.Text ?? ""));
        var hasImages = attachedFiles?.Any(f => f.MimeType?.StartsWith("image/") == true) == true ||
                        parts.Any(p => p.Files?.Any() == true);
        var reasoningContent = string.Join("\n", reasoningParts.Select(p => p.Reasoning ?? ""));

        var message = new AgentMessage
        {
            Role = role,
            Content = content,
            ReasoningContent = string.IsNullOrWhiteSpace(reasoningContent) ? null : reasoningContent
        };

        if (hasImages && role == AgentMessageRole.User)
        {
            var contentParts = new List<AgentMessageContentPart>();
            
            if (!string.IsNullOrEmpty(content))
            {
                contentParts.Add(new AgentMessageContentPart { Type = AgentContentPartType.Text, Text = content });
            }

            var imageFiles = attachedFiles?.Where(f => f.MimeType?.StartsWith("image/") == true) ?? Enumerable.Empty<SnCloudFileReferenceObject>();
            foreach (var imageFile in imageFiles)
            {
                if (!string.IsNullOrEmpty(imageFile.Url))
                {
                    contentParts.Add(new AgentMessageContentPart
                    {
                        Type = AgentContentPartType.ImageUrl,
                        ImageUrl = imageFile.Url
                    });
                }
            }

            foreach (var part in parts.Where(p => p.Files?.Any() == true))
            {
                foreach (var file in part.Files!.Where(f => f.MimeType?.StartsWith("image/") == true))
                {
                    if (!string.IsNullOrEmpty(file.Url))
                    {
                        contentParts.Add(new AgentMessageContentPart
                        {
                            Type = AgentContentPartType.ImageUrl,
                            ImageUrl = file.Url
                        });
                    }
                }
            }

            message.ContentParts = contentParts;
            message.Content = null;
        }

        if (toolCalls.Count > 0 && role == AgentMessageRole.Assistant)
        {
            message.ToolCalls = toolCalls.Select(tc => new AgentToolCall
            {
                Id = tc.FunctionCall?.Id ?? "",
                Name = $"{tc.FunctionCall?.PluginName}.{tc.FunctionCall?.Name}",
                Arguments = tc.FunctionCall?.Arguments ?? ""
            }).ToList();
        }

        var hasAssistantEnvelope = role != AgentMessageRole.Assistant ||
                                   !string.IsNullOrWhiteSpace(message.Content) ||
                                   !string.IsNullOrWhiteSpace(message.ReasoningContent) ||
                                   (message.ToolCalls?.Count > 0);

        if (hasAssistantEnvelope)
        {
            messages.Add(message);
        }

        if (role == AgentMessageRole.Assistant)
        {
            foreach (var toolResult in toolResults)
            {
                var resultText = toolResult.FunctionResult?.Result as string
                                 ?? (toolResult.FunctionResult?.Result != null
                                     ? JsonSerializer.Serialize(toolResult.FunctionResult.Result)
                                     : "");
                messages.Add(AgentMessage.FromToolResult(
                    toolResult.FunctionResult?.CallId ?? "",
                    resultText,
                    toolResult.FunctionResult?.IsError ?? false));
            }
        }

        return messages;
    }

    public static List<AgentToolDefinition> ExtractToolDefinitionsFromPlugins(IEnumerable<object> plugins)
    {
        var tools = new List<AgentToolDefinition>();

        foreach (var plugin in plugins)
        {
            var pluginType = plugin.GetType();
            var methods = pluginType.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            foreach (var method in methods)
            {
                var attrs = method.GetCustomAttributes(false);
                var kernelFunctionAttr = attrs.FirstOrDefault(a => a.GetType().Name == "KernelFunctionAttribute");

                if (kernelFunctionAttr == null) continue;

                var name = method.Name;
                var descriptionAttr = attrs.FirstOrDefault(a => a.GetType().Name == "DescriptionAttribute");
                var description = descriptionAttr?.GetType().GetProperty("Description")?.GetValue(descriptionAttr)?.ToString() ?? "";

                var parameters = BuildParametersSchema(method);

                tools.Add(new AgentToolDefinition
                {
                    Name = name,
                    Description = description,
                    ParametersJsonSchema = parameters
                });
            }
        }

        return tools;
    }

    private static string? BuildParametersSchema(System.Reflection.MethodInfo method)
    {
        var parameters = method.GetParameters();
        if (parameters.Length == 0) return null;

        var props = new Dictionary<string, object>();
        var required = new List<string>();

        foreach (var param in parameters)
        {
            var propDef = new Dictionary<string, object>
            {
                ["type"] = MapTypeToSchema(param.ParameterType)
            };

            var attrs = param.GetCustomAttributes(false);
            var descAttr = attrs.FirstOrDefault(a => a.GetType().Name == "DescriptionAttribute");
            if (descAttr != null)
            {
                var desc = descAttr.GetType().GetProperty("Description")?.GetValue(descAttr)?.ToString();
                if (!string.IsNullOrEmpty(desc))
                    propDef["description"] = desc;
            }

            props[param.Name ?? $"arg{Array.IndexOf(parameters, param)}"] = propDef;

            if (!param.HasDefaultValue && !param.IsOptional)
            {
                required.Add(param.Name ?? $"arg{Array.IndexOf(parameters, param)}");
            }
        }

        var schema = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = props
        };

        if (required.Count > 0)
            schema["required"] = required;

        return JsonSerializer.Serialize(schema);
    }

    private static string MapTypeToSchema(Type type)
    {
        if (type == typeof(string)) return "string";
        if (type == typeof(int) || type == typeof(long) || type == typeof(short)) return "integer";
        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal)) return "number";
        if (type == typeof(bool)) return "boolean";
        if (type == typeof(Guid)) return "string";
        if (type == typeof(DateTime) || type == typeof(DateTimeOffset)) return "string";
        if (type.IsArray || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))) return "array";
        return "object";
    }

    public AgentConversation ConvertFromChatHistory(object chatHistory, List<AgentToolDefinition>? tools = null)
    {
        var conversation = new AgentConversation { Tools = tools };
        
        var historyType = chatHistory.GetType();
        var messagesProp = historyType.GetProperty("Messages");
        if (messagesProp == null) return conversation;
        
        var messages = messagesProp.GetValue(chatHistory) as System.Collections.IEnumerable;
        if (messages == null) return conversation;
        
        foreach (var message in messages)
        {
            var msgType = message.GetType();
            var roleProp = msgType.GetProperty("Role");
            var contentProp = msgType.GetProperty("Content");
            var itemsProp = msgType.GetProperty("Items");
            
            var role = roleProp?.GetValue(message);
            var roleString = role?.ToString() ?? "user";
            
            var agentRole = roleString.ToLower() switch
            {
                "system" => AgentMessageRole.System,
                "user" => AgentMessageRole.User,
                "assistant" => AgentMessageRole.Assistant,
                "tool" => AgentMessageRole.Tool,
                _ => AgentMessageRole.User
            };
            
            string? content = null;
            List<AgentToolCall>? toolCalls = null;
            string? toolCallId = null;
            string? toolResultContent = null;
            
            var items = itemsProp?.GetValue(message) as System.Collections.IEnumerable;
            if (items != null)
            {
                var textParts = new List<string>();
                toolCalls = new List<AgentToolCall>();
                
                foreach (var item in items)
                {
                    var itemType = item.GetType().Name;
                    
                    if (itemType.Contains("TextContent"))
                    {
                        var textProp = item.GetType().GetProperty("Text");
                        var text = textProp?.GetValue(item)?.ToString();
                        if (!string.IsNullOrEmpty(text))
                            textParts.Add(text);
                    }
                    else if (itemType.Contains("FunctionCallContent"))
                    {
                        var idProp = item.GetType().GetProperty("Id");
                        var functionNameProp = item.GetType().GetProperty("FunctionName");
                        var pluginNameProp = item.GetType().GetProperty("PluginName");
                        var argsProp = item.GetType().GetProperty("Arguments");
                        
                        var id = idProp?.GetValue(item)?.ToString() ?? "";
                        var functionName = functionNameProp?.GetValue(item)?.ToString() ?? "";
                        var pluginName = pluginNameProp?.GetValue(item)?.ToString() ?? "";
                        var args = argsProp?.GetValue(item);
                        var argsString = args?.ToString() ?? "";
                        
                        var toolName = !string.IsNullOrEmpty(pluginName) 
                            ? $"{pluginName}.{functionName}" 
                            : functionName;
                        
                        toolCalls.Add(new AgentToolCall
                        {
                            Id = id,
                            Name = toolName,
                            Arguments = argsString
                        });
                    }
                    else if (itemType.Contains("FunctionResultContent"))
                    {
                        var callIdProp = item.GetType().GetProperty("CallId");
                        var resultProp = item.GetType().GetProperty("Result");
                        
                        toolCallId = callIdProp?.GetValue(item)?.ToString();
                        toolResultContent = resultProp?.GetValue(item)?.ToString();
                    }
                    else if (itemType.Contains("ImageContent"))
                    {
                        // Handle image content if needed
                    }
                }
                
                if (textParts.Count > 0)
                    content = string.Join("\n", textParts);
                    
                if (toolCalls.Count == 0)
                    toolCalls = null;
            }
            else
            {
                content = contentProp?.GetValue(message)?.ToString();
            }
            
            if (agentRole == AgentMessageRole.Tool && !string.IsNullOrEmpty(toolCallId))
            {
                conversation.Messages.Add(AgentMessage.FromToolResult(
                    toolCallId,
                    toolResultContent ?? "",
                    false
                ));
            }
            else
            {
                conversation.Messages.Add(new AgentMessage
                {
                    Role = agentRole,
                    Content = content,
                    ToolCalls = toolCalls
                });
            }
        }
        
        return conversation;
    }
}

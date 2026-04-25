namespace DysonNetwork.Insight.Agent.Foundation;

using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using DysonNetwork.Insight.Agent.Foundation.Models;

public static class AgentToolReflectionHelper
{
    public static void RegisterPluginTools(
        this IAgentToolRegistry registry,
        object pluginInstance,
        string? pluginName = null)
    {
        var pluginType = pluginInstance.GetType();
        var methods = pluginType.GetMethods(BindingFlags.Public | BindingFlags.Instance);

        foreach (var method in methods)
        {
            var agentToolAttr = method.GetCustomAttribute<AgentToolAttribute>();

            if (agentToolAttr == null) continue;

            var functionName = agentToolAttr.Name ?? method.Name;

            var toolName = !string.IsNullOrEmpty(pluginName)
                ? $"{pluginName}-{functionName}"
                : functionName;

            var description = agentToolAttr.Description ?? "";

            var parameters = BuildParametersSchema(method);

            var invoker = CreateMethodInvoker(pluginInstance, method);

            registry.Register(new AgentToolDefinition
            {
                Name = toolName,
                Description = description,
                ParametersJsonSchema = parameters
            }, invoker);
        }
    }

    public static List<AgentToolDefinition> ExtractToolDefinitionsFromType(Type pluginType)
    {
        var tools = new List<AgentToolDefinition>();
        var methods = pluginType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);

        foreach (var method in methods)
        {
            var agentToolAttr = method.GetCustomAttribute<AgentToolAttribute>();

            if (agentToolAttr == null) continue;

            var name = agentToolAttr.Name ?? method.Name;
            var description = agentToolAttr.Description ?? "";

            var parameters = BuildParametersSchema(method);

            tools.Add(new AgentToolDefinition
            {
                Name = name,
                Description = description,
                ParametersJsonSchema = parameters
            });
        }

        return tools;
    }

    public static Func<string, Task<string>> CreateMethodInvoker(object instance, MethodInfo method)
    {
        return async (argsJson) =>
        {
            try
            {
                var parameters = method.GetParameters();
                var bindableParameters = parameters
                    .Where(param => param.ParameterType != typeof(CancellationToken))
                    .ToArray();
                object?[]? args = null;

                if (parameters.Length > 0)
                {
                    args = new object?[parameters.Length];
                    var normalizedArgsJson = string.IsNullOrWhiteSpace(argsJson) ? "{}" : argsJson;
                    using var doc = JsonDocument.Parse(normalizedArgsJson);
                    var root = doc.RootElement;

                    for (int i = 0; i < parameters.Length; i++)
                    {
                        var param = parameters[i];
                        if (param.ParameterType == typeof(CancellationToken))
                        {
                            args[i] = CancellationToken.None;
                            continue;
                        }

                        if (root.TryGetProperty(param.Name ?? $"arg{i}", out var elem))
                        {
                            args[i] = JsonSerializer.Deserialize(elem.GetRawText(), param.ParameterType);
                        }
                        else if (param.HasDefaultValue)
                        {
                            args[i] = param.DefaultValue;
                        }
                        else
                        {
                            args[i] = param.ParameterType.IsValueType ? Activator.CreateInstance(param.ParameterType) : null;
                        }
                    }
                }

                var result = method.Invoke(instance, args);

                if (result is Task<string> stringTask)
                {
                    return await stringTask;
                }
                else if (result is Task task)
                {
                    await task;
                    return "Task completed";
                }
                else if (result != null)
                {
                    return JsonSerializer.Serialize(result);
                }

                return "null";
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        };
    }

    private static string? BuildParametersSchema(MethodInfo method)
    {
        var parameters = method.GetParameters()
            .Where(param => param.ParameterType != typeof(CancellationToken))
            .ToArray();
        if (parameters.Length == 0) return null;

        var props = new Dictionary<string, object>();
        var required = new List<string>();

        foreach (var param in parameters)
        {
            var propDef = new Dictionary<string, object>
            {
                ["type"] = MapTypeToSchema(param.ParameterType)
            };

            var agentToolParamAttr = param.GetCustomAttribute<AgentToolParameterAttribute>();
            if (agentToolParamAttr != null)
            {
                propDef["description"] = agentToolParamAttr.Description;
            }
            else
            {
                var descAttr = param.GetCustomAttribute<DescriptionAttribute>();
                if (descAttr != null)
                {
                    propDef["description"] = descAttr.Description;
                }
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
}

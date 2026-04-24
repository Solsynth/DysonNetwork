namespace DysonNetwork.Insight.Agent.Foundation;

/// <summary>
/// Marks a method as an agent tool that can be invoked by the AI.
/// This replaces Semantic Kernel's KernelFunction attribute.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class AgentToolAttribute : Attribute
{
    /// <summary>
    /// The name of the tool. If not specified, uses the method name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Description of what the tool does.
    /// </summary>
    public string? Description { get; set; }

    public AgentToolAttribute()
    {
    }

    public AgentToolAttribute(string name)
    {
        Name = name;
    }
}

/// <summary>
/// Marks a parameter as the description for an agent tool parameter.
/// This replaces Semantic Kernel's Description attribute.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public class AgentToolParameterAttribute : Attribute
{
    /// <summary>
    /// Description of the parameter.
    /// </summary>
    public string Description { get; }

    public AgentToolParameterAttribute(string description)
    {
        Description = description;
    }
}

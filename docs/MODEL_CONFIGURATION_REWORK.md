# Model Configuration Refactoring

## Overview

This document describes the refactoring of the AI model and kernel configuration system in DysonNetwork.Insight. The changes provide a more maintainable, type-safe, and extensible way to configure and manage AI models across the application.

## Motivation

### Before: Problems with the Old System

1. **String-based model references** - Prone to typos and runtime errors
   ```csharp
   // Old way - fragile string reference
   public string ThinkingService { get; set; } = "deepseek-chat";
   ```

2. **Scattered configuration** - Model settings were defined in multiple places
   - `appsettings.json` for service definitions
   - `MiChanConfig` for MiChan-specific strings
   - Hardcoded defaults in providers

3. **No validation** - Configuration errors only surfaced at runtime

4. **Difficult to switch models** - Required code changes and restarts

5. **Duplicated kernel creation logic** - Both `MiChanKernelProvider` and `ThoughtProvider` had similar initialization code

## New Architecture

### Directory Structure

```
DysonNetwork.Insight/
├── Agent/                          # Global AI Agent infrastructure
│   ├── Models/
│   │   ├── ModelRef.cs            # Strongly-typed model references
│   │   └── ModelConfiguration.cs  # Model configuration with validation
│   └── KernelBuilding/
│       ├── IKernelBuilder.cs      # Fluent builder interface
│       ├── SemanticKernelBuilder.cs
│       └── KernelBuildOptions.cs
├── MiChan/
│   ├── KernelBuilding/
│   │   └── MiChanKernelBuilderExtensions.cs
│   ├── MiChanKernelProvider.cs
│   └── MiChanConfig.cs
└── Thought/
    └── ThoughtProvider.cs
```

### Key Components

#### 1. ModelRef (Agent.Models)

Strongly-typed reference to an AI model with compile-time safety.

```csharp
public sealed record ModelRef
{
    public string Id { get; }           // "deepseek-chat"
    public string Provider { get; }     // "deepseek", "openrouter", "aliyun"
    public string ModelName { get; }    // "deepseek-chat"
    public string DisplayName { get; }
    public bool SupportsVision { get; }
    public bool SupportsReasoning { get; }
    public double DefaultTemperature { get; }
    public string? DefaultReasoningEffort { get; }
}
```

#### 2. ModelRegistry (Agent.Models)

Central registry of predefined models:

```csharp
public static class ModelRegistry
{
    public static readonly ModelRef DeepSeekChat = new(...);
    public static readonly ModelRef DeepSeekReasoner = new(...);
    public static readonly ModelRef ClaudeOpus = new(...);
    public static readonly ModelRef QwenMax = new(...);
    
    public static ModelRef? GetById(string id) => ...
    public static IEnumerable<ModelRef> VisionModels => ...
    public static void Register(ModelRef model) => ...  // Runtime registration
}
```

#### 3. ModelConfiguration (Agent.Models)

Configuration with validation and fluent API:

```csharp
public class ModelConfiguration
{
    public string ModelId { get; set; }
    public double? Temperature { get; set; }
    public string? ReasoningEffort { get; set; }
    public int? MaxTokens { get; set; }
    public bool EnableFunctions { get; set; } = true;
    public bool AllowRuntimeSwitch { get; set; } = true;
    
    // Validation
    public virtual IEnumerable<ValidationResult> Validate(ValidationContext ctx)
    
    // Fluent extensions
    public ModelConfiguration WithTemperature(double temp)
    public ModelConfiguration WithReasoningEffort(string effort)
}
```

#### 4. IKernelBuilder (Agent.KernelBuilding)

Fluent interface for kernel construction:

```csharp
public interface IKernelBuilder
{
    IKernelBuilder WithModel(ModelConfiguration model);
    IKernelBuilder WithModel(string modelId);
    IKernelBuilder WithModel(ModelRef modelRef);
    
    IKernelBuilder WithEmbeddings(bool include = true);
    IKernelBuilder WithWebSearch(bool include = true);
    IKernelBuilder WithPlugins(Action<Kernel> setup);
    
    IKernelBuilder WithTemperature(double temperature);
    IKernelBuilder WithReasoningEffort(string effort);
    IKernelBuilder WithMaxTokens(int maxTokens);
    IKernelBuilder WithAutoInvoke(bool autoInvoke = true);
    
    Kernel Build();
    PromptExecutionSettings CreatePromptExecutionSettings(...);
}
```

#### 5. MiChanKernelBuilderExtensions

MiChan-specific extension methods:

```csharp
public static class MiChanKernelBuilderExtensions
{
    public static IKernelBuilder WithMiChanModel(this IKernelBuilder builder, MiChanConfig config)
    public static IKernelBuilder WithMiChanAutonomousModel(this IKernelBuilder builder, MiChanConfig config)
    public static IKernelBuilder WithMiChanPlugins(this IKernelBuilder builder, IServiceProvider sp)
    
    public static IKernelBuilder ForMiChanChat(this IKernelBuilder builder, MiChanConfig config, IServiceProvider sp)
    public static IKernelBuilder ForMiChanAutonomous(this IKernelBuilder builder, MiChanConfig config, IServiceProvider sp)
    public static IKernelBuilder ForMiChanVision(this IKernelBuilder builder, MiChanConfig config)
    public static IKernelBuilder ForTopicGeneration(this IKernelBuilder builder, MiChanConfig config)
    public static IKernelBuilder ForCompaction(this IKernelBuilder builder, MiChanConfig config)
}
```

## Configuration Format

### appsettings.json

```json
{
  "MiChan": {
    "Enabled": true,
    
    // Model configurations using new format
    "ThinkingModel": {
      "ModelId": "deepseek-chat",
      "Temperature": 0.75,
      "ReasoningEffort": "medium",
      "EnableFunctions": true,
      "AllowRuntimeSwitch": true
    },
    
    "AutonomousModel": {
      "ModelId": "deepseek-reasoner",
      "Temperature": 0.7,
      "ReasoningEffort": "high",
      "EnableFunctions": true,
      "AllowRuntimeSwitch": true
    },
    
    // Optional: Falls back to ThinkingModel if not set
    "ScheduledTaskModel": { "ModelId": "deepseek-chat" },
    "CompactionModel": { "ModelId": "deepseek-chat", "Temperature": 0.5 },
    "TopicGenerationModel": { "ModelId": "deepseek-chat" },
    
    "Vision": {
      "VisionThinkingService": "vision-openrouter",
      "EnableVisionAnalysis": true,
      "MaxImagesPerRequest": 10,
      "FallbackToTextModel": true
    }
  }
}
```

## Usage Examples

### Basic Kernel Creation

```csharp
// Using fluent API
var kernel = kernelBuilder
    .WithModel(ModelRegistry.DeepSeekChat)
    .WithEmbeddings()
    .WithWebSearch()
    .WithMiChanPlugins(serviceProvider)
    .Build();

// Using pre-built configuration
var kernel = kernelBuilder
    .ForMiChanChat(config, serviceProvider)
    .Build();
```

### MiChanKernelProvider

```csharp
public class MiChanKernelProvider
{
    private readonly IKernelBuilder _kernelBuilder;
    
    public Kernel GetKernel()
    {
        return _kernelBuilder
            .ForMiChanChat(_config, _serviceProvider)
            .Build();
    }
    
    public Kernel GetAutonomousKernel()
    {
        return _kernelBuilder
            .ForMiChanAutonomous(_config, _serviceProvider)
            .Build();
    }
    
    public Kernel GetVisionKernel()
    {
        return _kernelBuilder
            .ForMiChanVision(_config)
            .Build();
    }
}
```

### Runtime Model Switching

```csharp
// Check if switching is allowed
if (config.ThinkingModel.AllowRuntimeSwitch)
{
    // Switch to a different model
    config.ThinkingModel.ModelId = ModelRegistry.ClaudeOpus.Id;
}

// Or use the provider's helper method
if (miChanKernelProvider.TrySwitchModel("deepseek-reasoner"))
{
    // Model switched successfully
}
```

### Custom Model Configuration

```csharp
// Using fluent API
var modelConfig = new ModelConfiguration
{
    ModelId = "deepseek-chat",
    Temperature = 0.8,
    MaxTokens = 4096
}.WithReasoningEffort("high")
 .WithParameter("customKey", "customValue");

// Using implicit conversion
ModelConfiguration config1 = "deepseek-chat";  // From string
ModelConfiguration config2 = ModelRegistry.DeepSeekChat;  // From ModelRef
```

## Dependency Injection Setup

```csharp
// In Startup/Program.cs
services.AddSingleton<KernelFactory>();

// Register kernel builder
services.AddSingleton<SemanticKernelBuilder>();
services.AddSingleton<Agent.KernelBuilding.IKernelBuilder>(
    sp => sp.GetRequiredService<SemanticKernelBuilder>());

// Register providers
services.AddSingleton<MiChanKernelProvider>();
```

## Adding New Models

To add a new model to the registry:

```csharp
// In Agent/Models/ModelRef.cs
public static class ModelRegistry
{
    // Add new model reference
    public static readonly ModelRef GPT4Turbo = new(
        id: "gpt-4-turbo",
        provider: "openrouter",
        modelName: "openai/gpt-4-turbo",
        displayName: "GPT-4 Turbo",
        supportsVision: true,
        defaultTemperature: 0.7);
}
```

Then register it in the static constructor:

```csharp
private static readonly Dictionary<string, ModelRef> _models = new()
{
    [DeepSeekChat.Id] = DeepSeekChat,
    [DeepSeekReasoner.Id] = DeepSeekReasoner,
    [GPT4Turbo.Id] = GPT4Turbo,  // Add here
    // ...
};
```

## Benefits

1. **Type Safety**: No more magic strings - use `ModelRegistry.DeepSeekChat` instead of `"deepseek-chat"`

2. **Centralized Configuration**: All model settings in one place with validation

3. **Fluent API**: Readable, chainable kernel construction

4. **Runtime Flexibility**: Switch models at runtime with `AllowRuntimeSwitch`

5. **Extensibility**: Easy to add new models and configurations

6. **Testability**: Interfaces make testing easier

7. **Consistency**: Same pattern used across MiChan and SN-chan

## Migration Guide

### From String Properties

```csharp
// Old
public string ThinkingService { get; set; } = "deepseek-chat";

// New
public ModelConfiguration ThinkingModel { get; set; } = ModelRegistry.DeepSeekChat;
```

### From Direct KernelFactory Usage

```csharp
// Old
var kernel = kernelFactory.CreateKernel(config.ThinkingService, addEmbeddings: true);

// New
var kernel = kernelBuilder
    .WithModel(config.ThinkingModel)
    .WithEmbeddings()
    .Build();
```

### Configuration in appsettings.json

```json
// Old
{
  "MiChan": {
    "ThinkingService": "deepseek-chat",
    "AutonomousThinkingService": "deepseek-reasoner"
  }
}

// New
{
  "MiChan": {
    "ThinkingModel": {
      "ModelId": "deepseek-chat",
      "Temperature": 0.75
    },
    "AutonomousModel": {
      "ModelId": "deepseek-reasoner",
      "Temperature": 0.7
    }
  }
}
```

## Best Practices

1. **Use ModelRegistry**: Always use predefined models from `ModelRegistry` instead of string literals

2. **Validate Configuration**: Call `Validate()` on configurations in startup

3. **Use Extension Methods**: Create domain-specific extension methods for common kernel configurations

4. **Allow Runtime Switching**: Set `AllowRuntimeSwitch = true` for models that might need to change without restart

5. **Fallback Pattern**: Use null-coalescing for optional model configurations:
   ```csharp
   public ModelConfiguration GetAutonomousModel() =>
       AutonomousModel ?? ThinkingModel;
   ```

## Future Enhancements

- Dynamic model discovery from configuration
- Model performance metrics and auto-selection
- A/B testing different models
- Model fallback chains
- Cost-aware model selection

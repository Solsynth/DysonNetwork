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
│   │   ├── ModelConfiguration.cs  # Model configuration with validation
│   │   ├── ModelUseCase.cs        # Use case enum (MiChanChat, SnChanChat, etc.)
│   │   ├── ModelUseCaseMapping.cs # PerkLevel-based model mappings
│   │   ├── IModelSelector.cs      # Model selection interface
│   │   └── ModelSelector.cs       # PerkLevel-aware model selector
│   └── KernelBuilding/
│       ├── IKernelBuilder.cs      # Fluent builder interface
│       ├── SemanticKernelBuilder.cs
│       └── KernelBuildOptions.cs
├── MiChan/
│   ├── KernelBuilding/
│   │   └── MiChanKernelBuilderExtensions.cs
│   ├── MiChanKernelProvider.cs    # Updated with PerkLevel support
│   └── MiChanConfig.cs            # Updated with ModelSelection config
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

#### 6. ModelUseCase (Agent.Models)

Defines different use cases for model selection:

```csharp
public enum ModelUseCase
{
    Default = 0,
    MiChanChat = 1,           // MiChan interactive chat
    MiChanAutonomous = 2,     // MiChan self-directed actions
    MiChanVision = 3,         // MiChan image analysis
    MiChanScheduledTask = 4,  // MiChan background tasks
    MiChanCompaction = 5,     // MiChan conversation summarization
    MiChanTopicGeneration = 6,// MiChan topic generation
    SnChanChat = 10,          // SN-chan user chat
    SnChanReasoning = 11,     // SN-chan complex reasoning
    SnChanVision = 12,        // SN-chan image analysis
    SystemTask = 20,          // Internal system operations
    Embedding = 30            // Vector embedding generation
}
```

#### 7. ModelUseCaseMapping (Agent.Models)

Maps models to use cases with PerkLevel requirements:

```csharp
public class ModelUseCaseMapping
{
    public ModelUseCase UseCase { get; set; }
    public string ModelId { get; set; }
    public int MinPerkLevel { get; set; } = 0
    public int? MaxPerkLevel { get; set; }
    public bool IsDefault { get; set; }
    public int Priority { get; set; }
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public bool Enabled { get; set; } = true
    
    public bool CanUse(int userPerkLevel) => 
        Enabled && 
        userPerkLevel >= MinPerkLevel &&
        (!MaxPerkLevel.HasValue || userPerkLevel <= MaxPerkLevel.Value);
}
```

#### 8. IModelSelector (Agent.Models)

Service for PerkLevel-aware model selection:

```csharp
public interface IModelSelector
{
    ModelSelectionResult SelectModel(UserModelContext context);
    ModelSelectionResult SelectModel(ModelUseCase useCase, int perkLevel = 0, string? preferredModelId = null);
    IEnumerable<ModelUseCaseMapping> GetAvailableModels(ModelUseCase useCase, int perkLevel);
    bool CanAccessModel(ModelUseCase useCase, string modelId, int perkLevel);
    ModelConfiguration GetDefaultConfiguration(ModelUseCase useCase, int perkLevel = 0);
}
```

#### 9. MiChanModelSelectionConfig (MiChan)

MiChan-specific model selection configuration:

```csharp
public class MiChanModelSelectionConfig
{
    public string DefaultModelId { get; set; } = ModelRegistry.DeepSeekChat.Id;
    public List<MiChanModelMapping> Mappings { get; set; } = new();
    public bool AllowUserOverride { get; set; } = true;
    
    public IEnumerable<MiChanModelMapping> GetAvailableModels(ModelUseCase useCase, int perkLevel);
    public MiChanModelMapping? GetDefaultMapping(ModelUseCase useCase, int perkLevel);
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
    
    // Basic usage (uses default configuration)
    public Kernel GetKernel()
    {
        return _kernelBuilder
            .ForMiChanChat(_config, _serviceProvider)
            .Build();
    }
    
    // With PerkLevel-based model selection
    public Kernel GetKernel(int? userPerkLevel = null, string? preferredModelId = null)
    {
        return _kernelBuilder
            .ForMiChanChat(_config, _serviceProvider)
            .Build();
    }
    
    public Kernel GetAutonomousKernel(int? userPerkLevel = null)
    {
        return _kernelBuilder
            .ForMiChanAutonomous(_config, _serviceProvider)
            .Build();
    }
    
    public Kernel GetVisionKernel(int? userPerkLevel = null)
    {
        return _kernelBuilder
            .ForMiChanVision(_config)
            .Build();
    }
    
    // Get available models for UI display
    public IEnumerable<MiChanModelMapping> GetAvailableModelsForUseCase(
        ModelUseCase useCase, 
        int userPerkLevel)
    {
        // Returns models the user can access based on PerkLevel
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

// New - Basic configuration
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

// New - With PerkLevel-based model selection
{
  "MiChan": {
    "UseModelSelection": true,
    "ModelSelection": {
      "DefaultModelId": "deepseek-chat",
      "AllowUserOverride": true,
      "Mappings": [
        {
          "UseCase": "MiChanChat",
          "ModelId": "deepseek-chat",
          "MinPerkLevel": 0,
          "IsDefault": true,
          "DisplayName": "DeepSeek Chat"
        },
        {
          "UseCase": "MiChanChat",
          "ModelId": "deepseek-reasoner",
          "MinPerkLevel": 1,
          "DisplayName": "DeepSeek Reasoner"
        }
      ]
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

6. **PerkLevel-Based Access**: Configure `MinPerkLevel` appropriately for tiered access:
   - PerkLevel 0: Free tier - basic models
   - PerkLevel 1: Standard tier - better models
   - PerkLevel 2+: Premium tier - best models

7. **Use Priority for Model Selection**: Set higher priority for preferred models within the same tier:
   ```json
   {
     "Priority": 200,  // Higher priority
     "MinPerkLevel": 1
   }
   ```

8. **Enable User Override**: Allow users to choose from available models in their tier:
   ```json
   { "AllowUserOverride": true }
   ```

9. **Set Default Models**: Always mark one model as `IsDefault` for each use case to ensure fallback:
   ```json
   { "IsDefault": true }
   ```

10. **Custom Providers**: When using custom providers, always specify `BaseUrl` and `ApiKey`:
    ```json
    {
      "Provider": "custom",
      "BaseUrl": "https://api.together.xyz/v1",
      "ApiKey": "sk-your-api-key"
    }
    ```

## Custom Model Providers

The system supports custom OpenAI-compatible API providers with configurable base URLs.

### Configuration

```json
{
  "MiChan": {
    "ThinkingModel": {
      "ModelId": "deepseek-chat",
      "Provider": "custom",
      "BaseUrl": "https://api.together.xyz/v1",
      "ApiKey": "sk-your-api-key",
      "CustomModelName": "meta-llama/Llama-3.1-70B-Instruct-Turbo",
      "Temperature": 0.75
    }
  }
}
```

### Using Custom Providers in Code

```csharp
// Using fluent API
var modelConfig = new ModelConfiguration { ModelId = "deepseek-chat" }
    .WithCustomProvider(
        baseUrl: "https://api.together.xyz/v1",
        apiKey: "sk-your-api-key",
        modelName: "meta-llama/Llama-3.1-70B-Instruct-Turbo"
    );

var kernel = kernelBuilder
    .WithModel(modelConfig)
    .Build();
```

### ModelRef with Custom Base URL

```csharp
// Create a custom model reference
var customModel = ModelRef.CreateCustom(
    id: "together-llama",
    modelName: "meta-llama/Llama-3.1-70B-Instruct-Turbo",
    baseUrl: "https://api.together.xyz/v1",
    apiKey: "sk-your-api-key",
    displayName: "Llama 3.1 70B (Together)"
);

// Register at runtime
ModelRegistry.Register(customModel);
```

## Multi-Model Configuration with PerkLevel Support

The system supports use-case based model selection with PerkLevel-based access control. Different users can access different models based on their subscription tier.

### ModelUseCase Enum

```csharp
public enum ModelUseCase
{
    Default = 0,
    MiChanChat = 1,
    MiChanAutonomous = 2,
    MiChanVision = 3,
    MiChanScheduledTask = 4,
    MiChanCompaction = 5,
    MiChanTopicGeneration = 6,
    SnChanChat = 10,
    SnChanReasoning = 11,
    SnChanVision = 12,
    SystemTask = 20,
    Embedding = 30
}
```

### Configuration

```json
{
  "MiChan": {
    "UseModelSelection": true,
    "ModelSelection": {
      "DefaultModelId": "deepseek-chat",
      "AllowUserOverride": true,
      "Mappings": [
        {
          "UseCase": "MiChanChat",
          "ModelId": "deepseek-chat",
          "MinPerkLevel": 0,
          "IsDefault": true,
          "Priority": 100,
          "DisplayName": "DeepSeek Chat",
          "Description": "Fast and efficient for everyday conversations"
        },
        {
          "UseCase": "MiChanChat",
          "ModelId": "deepseek-reasoner",
          "MinPerkLevel": 1,
          "Priority": 200,
          "DisplayName": "DeepSeek Reasoner",
          "Description": "Advanced reasoning for complex discussions"
        },
        {
          "UseCase": "MiChanChat",
          "ModelId": "claude-sonnet",
          "MinPerkLevel": 2,
          "Priority": 300,
          "DisplayName": "Claude 3 Sonnet",
          "Description": "Premium model with enhanced capabilities"
        },
        {
          "UseCase": "MiChanVision",
          "ModelId": "vision-aliyun",
          "MinPerkLevel": 0,
          "IsDefault": true,
          "DisplayName": "Qwen Vision"
        },
        {
          "UseCase": "MiChanVision",
          "ModelId": "vision-openrouter",
          "MinPerkLevel": 2,
          "Priority": 200,
          "DisplayName": "Claude 3 Opus"
        }
      ]
    }
  }
}
```

### Using IModelSelector

```csharp
public class MyService
{
    private readonly IModelSelector _modelSelector;
    
    public MyService(IModelSelector modelSelector)
    {
        _modelSelector = modelSelector;
    }
    
    public async Task ProcessWithModel(int userPerkLevel)
    {
        // Select model based on use case and PerkLevel
        var result = _modelSelector.SelectModel(
            ModelUseCase.MiChanChat, 
            userPerkLevel,
            preferredModelId: "deepseek-reasoner"
        );
        
        if (result.Success)
        {
            var modelConfig = result.Configuration;
            var availableModels = result.AvailableModels; // For UI display
            
            // Use the selected model
            var kernel = kernelBuilder
                .WithModel(modelConfig)
                .Build();
        }
    }
}
```

### MiChanKernelProvider with PerkLevel

```csharp
// Get kernel for specific user with PerkLevel
var kernel = miChanKernelProvider.GetKernel(
    userPerkLevel: 2, 
    preferredModelId: "claude-sonnet"
);

// Get available models for UI
var availableModels = miChanKernelProvider.GetAvailableModelsForUseCase(
    ModelUseCase.MiChanChat, 
    userPerkLevel: 1
);

// Returns: deepseek-chat (Perk 0), deepseek-reasoner (Perk 1)
// Excludes: claude-sonnet (requires Perk 2)
```

### UserModelContext

```csharp
// Create context for model selection
var context = new UserModelContext
{
    AccountId = userId,
    PerkLevel = 2,
    IsSuperuser = false,
    UseCase = ModelUseCase.MiChanChat,
    PreferredModelId = "claude-sonnet",
    UseBestAvailable = true
};

var result = modelSelector.SelectModel(context);
```

### MiChanModelMapping Properties

| Property | Description |
|----------|-------------|
| `UseCase` | The use case this mapping applies to |
| `ModelId` | Reference to Thinking:Services or ModelRegistry |
| `MinPerkLevel` | Minimum PerkLevel required (0 = available to all) |
| `MaxPerkLevel` | Maximum PerkLevel allowed (null = no limit) |
| `IsDefault` | Whether this is the default model for the use case |
| `Priority` | Selection priority (higher = preferred) |
| `DisplayName` | Human-readable name for UI |
| `Description` | Help text for UI |
| `Enabled` | Whether this mapping is active |

## Model Selection Flow

1. **User Request** → System receives request with user's PerkLevel
2. **Use Case Detection** → Determines the use case (MiChanChat, SnChanChat, etc.)
3. **Filter by PerkLevel** → Gets mappings where `MinPerkLevel <= userPerkLevel`
4. **Check Preference** → If user has preferred model and `AllowUserOverride` is true, use it
5. **Select Best Model** → Choose highest priority available model
6. **Apply Overrides** → Apply temperature, reasoning effort from context
7. **Create Kernel** → Build kernel with selected model configuration

## Benefits of Multi-Model Configuration

1. **Tiered Access**: Different subscription levels get different model quality
2. **Cost Control**: Free users use cheaper models, premium users get better models
3. **User Choice**: Users can select from available models within their tier
4. **Dynamic Scaling**: Easy to add/remove models from tiers without code changes
5. **A/B Testing**: Different models can be tested with different user groups
6. **Fallback Chain**: Automatic fallback if a model is unavailable

## Future Enhancements

- Dynamic model discovery from configuration
- Model performance metrics and auto-selection
- A/B testing different models
- Model fallback chains
- Cost-aware model selection
- Real-time model performance monitoring
- Automatic model degradation on errors

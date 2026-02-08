#pragma warning disable SKEXP0050
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DysonNetwork.Insight.MiChan.Plugins;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace DysonNetwork.Insight.MiChan.Controllers;

[ApiController]
[Route("/api/michan")]
[Authorize(Roles = "admin,superuser")]
public class MiChanAdminController : ControllerBase
{
    private readonly MiChanConfig _config;
    private readonly ILogger<MiChanAdminController> _logger;
    private readonly MiChanMemoryService _memoryService;
    private readonly MiChanKernelProvider _kernelProvider;
    private readonly SolarNetworkApiClient _apiClient;
    private readonly IServiceProvider _serviceProvider;

    public MiChanAdminController(
        MiChanConfig config,
        ILogger<MiChanAdminController> logger,
        MiChanMemoryService memoryService,
        MiChanKernelProvider kernelProvider,
        SolarNetworkApiClient apiClient,
        IServiceProvider serviceProvider)
    {
        _config = config;
        _logger = logger;
        _memoryService = memoryService;
        _kernelProvider = kernelProvider;
        _apiClient = apiClient;
        _serviceProvider = serviceProvider;
    }

    public class ChatWithMiChanRequest
    {
        [Required]
        public string Message { get; set; } = null!;
        public string? ContextId { get; set; }
        public bool UsePlugins { get; set; } = true;
    }

    public class CommandMiChanRequest
    {
        [Required]
        public string Command { get; set; } = null!;
        public Dictionary<string, object>? Parameters { get; set; }
    }

    /// <summary>
    /// Stream a conversation with MiChan
    /// </summary>
    [HttpPost("chat")]
    [Experimental("SKEXP0050")]
    public async IAsyncEnumerable<string> ChatWithMiChan(
        [FromBody] ChatWithMiChanRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!_config.Enabled)
        {
            yield return "MiChan is currently disabled.";
            yield break;
        }

        var contextId = request.ContextId ?? $"admin_{Guid.NewGuid():N}";
        _logger.LogInformation("Admin chat started with context {ContextId}", contextId);

        // Get or create conversation history
        var history = await _memoryService.GetRecentInteractionsAsync(contextId, 20);
        
        // Build kernel and chat history
        var kernel = _kernelProvider.GetKernel();
        
        if (request.UsePlugins)
        {
            // Register plugins for admin use
            var chatPlugin = _serviceProvider.GetRequiredService<ChatPlugin>();
            var postPlugin = _serviceProvider.GetRequiredService<PostPlugin>();
            var notificationPlugin = _serviceProvider.GetRequiredService<NotificationPlugin>();
            var accountPlugin = _serviceProvider.GetRequiredService<AccountPlugin>();

            kernel.Plugins.AddFromObject(chatPlugin, "chat");
            kernel.Plugins.AddFromObject(postPlugin, "post");
            kernel.Plugins.AddFromObject(notificationPlugin, "notification");
            kernel.Plugins.AddFromObject(accountPlugin, "account");
        }

        // Load personality
        var personality = PersonalityLoader.LoadPersonality(_config.PersonalityFile, _config.Personality, _logger);
        
        var chatHistory = new ChatHistory($"""
            {personality}
            
            You are in an administrative chat session. The user has full control over you.
            You can use any of your available tools/functions to help the user.
            Be helpful, direct, and execute any commands the user gives you.
            """);

        // Add conversation history
        foreach (var interaction in history.OrderBy(i => i.CreatedAt))
        {
            if (interaction.Context.TryGetValue("message", out var msg))
            {
                chatHistory.AddUserMessage(msg?.ToString() ?? "");
            }
            if (interaction.Context.TryGetValue("response", out var resp))
            {
                chatHistory.AddAssistantMessage(resp?.ToString() ?? "");
            }
        }

        // Add current message
        chatHistory.AddUserMessage(request.Message);

        // Stream response
        var chatService = kernel.GetRequiredService<IChatCompletionService>();
        var executionSettings = _kernelProvider.CreatePromptExecutionSettings();
        
        var fullResponse = new System.Text.StringBuilder();

        await foreach (var update in chatService.GetStreamingChatMessageContentsAsync(
            chatHistory, executionSettings, kernel, cancellationToken))
        {
            var content = update.Content;
            if (string.IsNullOrEmpty(content)) continue;

            fullResponse.Append(content);
            yield return content;
        }

        // Store interaction in memory
        await _memoryService.StoreInteractionAsync(
            "admin",
            contextId,
            new Dictionary<string, object>
            {
                ["message"] = request.Message,
                ["response"] = fullResponse.ToString(),
                ["timestamp"] = DateTime.UtcNow
            }
        );

        _logger.LogInformation("Admin chat completed for context {ContextId}", contextId);
    }

    /// <summary>
    /// Send a command to MiChan and get the result
    /// </summary>
    [HttpPost("command")]
    [Experimental("SKEXP0050")]
    public async Task<ActionResult> CommandMiChan([FromBody] CommandMiChanRequest request)
    {
        if (!_config.Enabled)
        {
            return BadRequest(new { error = "MiChan is currently disabled" });
        }

        try
        {
            _logger.LogInformation("Executing command: {Command}", request.Command);

            var kernel = _kernelProvider.GetKernel();
            
            // Register plugins
            var chatPlugin = _serviceProvider.GetRequiredService<ChatPlugin>();
            var postPlugin = _serviceProvider.GetRequiredService<PostPlugin>();
            var notificationPlugin = _serviceProvider.GetRequiredService<NotificationPlugin>();

            kernel.Plugins.AddFromObject(chatPlugin, "chat");
            kernel.Plugins.AddFromObject(postPlugin, "post");
            kernel.Plugins.AddFromObject(notificationPlugin, "notification");

            // Execute command
            var personality = PersonalityLoader.LoadPersonality(_config.PersonalityFile, _config.Personality, _logger);
            
            var prompt = $"""
                {personality}
                
                The administrator is giving you a command: "{request.Command}"
                
                Execute this command immediately using your available tools.
                Provide a brief summary of what you did.
                """;

            if (request.Parameters != null)
            {
                prompt += $"\n\nParameters: {JsonSerializer.Serialize(request.Parameters)}";
            }

            var executionSettings = _kernelProvider.CreatePromptExecutionSettings();
            var result = await kernel.InvokePromptAsync(prompt, new KernelArguments(executionSettings));
            var response = result.GetValue<string>();

            return Ok(new
            {
                success = true,
                command = request.Command,
                result = response,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing command: {Command}", request.Command);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get MiChan's current status and memory
    /// </summary>
    [HttpGet("status")]
    public async Task<ActionResult> GetStatus()
    {
        var activeContexts = await _memoryService.GetActiveContextsAsync();
        var recentAutonomous = await _memoryService.GetInteractionsByTypeAsync("autonomous", 5);
        var recentMentions = await _memoryService.GetInteractionsByTypeAsync("mention_response", 5);

        return Ok(new
        {
            enabled = _config.Enabled,
            personality_file = _config.PersonalityFile,
            autonomous_enabled = _config.AutonomousBehavior.Enabled,
            memory_enabled = _config.Memory.PersistToDatabase,
            active_contexts = activeContexts.Count,
            recent_autonomous_actions = recentAutonomous.Count,
            recent_mention_responses = recentMentions.Count,
            personality_preview = PersonalityLoader.LoadPersonality(_config.PersonalityFile, _config.Personality, _logger)[..Math.Min(200, PersonalityLoader.LoadPersonality(_config.PersonalityFile, _config.Personality, _logger).Length)] + "..."
        });
    }

    /// <summary>
    /// Test a personality prompt without changing the config
    /// </summary>
    [HttpPost("test-personality")]
    [Experimental("SKEXP0050")]
    public async Task<ActionResult> TestPersonality([FromBody] TestPersonalityRequest request)
    {
        try
        {
            var kernel = _kernelProvider.GetKernel();
            
            var chatHistory = new ChatHistory(request.Personality);
            chatHistory.AddUserMessage(request.TestMessage);

            var chatService = kernel.GetRequiredService<IChatCompletionService>();
            var executionSettings = _kernelProvider.CreatePromptExecutionSettings();
            
            var result = await chatService.GetChatMessageContentAsync(chatHistory, executionSettings, kernel);

            return Ok(new
            {
                personality = request.Personality,
                test_message = request.TestMessage,
                response = result.Content
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    public class TestPersonalityRequest
    {
        [Required]
        public string Personality { get; set; } = null!;
        [Required]
        public string TestMessage { get; set; } = null!;
    }

    /// <summary>
    /// Clear MiChan's memory for a specific context or all contexts
    /// </summary>
    [HttpDelete("memory")]
    public async Task<ActionResult> ClearMemory([FromQuery] string? contextId)
    {
        if (!string.IsNullOrEmpty(contextId))
        {
            await _memoryService.ClearContextAsync(contextId);
            return Ok(new { message = $"Cleared memory for context {contextId}" });
        }
        else
        {
            var contexts = await _memoryService.GetActiveContextsAsync();
            foreach (var ctx in contexts)
            {
                await _memoryService.ClearContextAsync(ctx);
            }
            return Ok(new { message = $"Cleared memory for {contexts.Count} contexts" });
        }
    }
}

#pragma warning restore SKEXP0050

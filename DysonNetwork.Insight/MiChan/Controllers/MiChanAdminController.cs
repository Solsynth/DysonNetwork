#pragma warning disable SKEXP0050
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DysonNetwork.Insight.MiChan.Plugins;
using DysonNetwork.Shared.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace DysonNetwork.Insight.MiChan.Controllers;

[ApiController]
[Route("/api/michan")]
public class MiChanAdminController(
    MiChanConfig config,
    MiChanService michan,
    ILogger<MiChanAdminController> logger,
    MiChanMemoryService memoryService,
    MiChanKernelProvider kernelProvider,
    IServiceProvider serviceProvider)
    : ControllerBase
{
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
    [AskPermission("michan.admin")]
    public async IAsyncEnumerable<string> ChatWithMiChan(
        [FromBody] ChatWithMiChanRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!config.Enabled)
        {
            yield return "MiChan is currently disabled.";
            yield break;
        }

        var contextId = request.ContextId ?? $"admin_{Guid.NewGuid():N}";
        logger.LogInformation("Admin chat started with context {ContextId}", contextId);

        // Get or create conversation history
        var history = await memoryService.GetRecentInteractionsAsync(contextId, 20);
        
        // Build kernel and chat history
        var kernel = kernelProvider.GetKernel();
        
        if (request.UsePlugins)
        {
            // Register plugins for admin use
            var chatPlugin = serviceProvider.GetRequiredService<ChatPlugin>();
            var postPlugin = serviceProvider.GetRequiredService<PostPlugin>();
            var notificationPlugin = serviceProvider.GetRequiredService<NotificationPlugin>();
            var accountPlugin = serviceProvider.GetRequiredService<AccountPlugin>();

            kernel.Plugins.AddFromObject(chatPlugin, "chat");
            kernel.Plugins.AddFromObject(postPlugin, "post");
            kernel.Plugins.AddFromObject(notificationPlugin, "notification");
            kernel.Plugins.AddFromObject(accountPlugin, "account");
        }

        // Load personality
        var personality = PersonalityLoader.LoadPersonality(config.PersonalityFile, config.Personality, logger);
        
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
        var executionSettings = kernelProvider.CreatePromptExecutionSettings();
        
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
        await memoryService.StoreInteractionAsync(
            "admin",
            contextId,
            new Dictionary<string, object>
            {
                ["message"] = request.Message,
                ["response"] = fullResponse.ToString(),
                ["timestamp"] = DateTime.UtcNow
            }
        );

        logger.LogInformation("Admin chat completed for context {ContextId}", contextId);
    }

    /// <summary>
    /// Send a command to MiChan and get the result
    /// </summary>
    [HttpPost("command")]
    [Experimental("SKEXP0050")]
    [AskPermission("michan.admin")]
    public async Task<ActionResult> CommandMiChan([FromBody] CommandMiChanRequest request)
    {
        if (!config.Enabled)
        {
            return BadRequest(new { error = "MiChan is currently disabled" });
        }

        try
        {
            logger.LogInformation("Executing command: {Command}", request.Command);

            var kernel = kernelProvider.GetKernel();
            
            // Register plugins
            var chatPlugin = serviceProvider.GetRequiredService<ChatPlugin>();
            var postPlugin = serviceProvider.GetRequiredService<PostPlugin>();
            var notificationPlugin = serviceProvider.GetRequiredService<NotificationPlugin>();

            kernel.Plugins.AddFromObject(chatPlugin, "chat");
            kernel.Plugins.AddFromObject(postPlugin, "post");
            kernel.Plugins.AddFromObject(notificationPlugin, "notification");

            // Execute command
            var personality = PersonalityLoader.LoadPersonality(config.PersonalityFile, config.Personality, logger);
            
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

            var executionSettings = kernelProvider.CreatePromptExecutionSettings();
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
            logger.LogError(ex, "Error executing command: {Command}", request.Command);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get MiChan's current status and memory
    /// </summary>
    [HttpGet("status")]
    [AskPermission("michan.admin")]
    public async Task<ActionResult> GetStatus()
    {
        var activeContexts = await memoryService.GetActiveContextsAsync();
        var recentAutonomous = await memoryService.GetInteractionsByTypeAsync("autonomous", 5);
        var recentMentions = await memoryService.GetInteractionsByTypeAsync("mention_response", 5);

        return Ok(new
        {
            enabled = config.Enabled,
            personality_file = config.PersonalityFile,
            autonomous_enabled = config.AutonomousBehavior.Enabled,
            memory_enabled = config.Memory.PersistToDatabase,
            active_contexts = activeContexts.Count,
            recent_autonomous_actions = recentAutonomous.Count,
            recent_mention_responses = recentMentions.Count,
            personality_preview = PersonalityLoader.LoadPersonality(config.PersonalityFile, config.Personality, logger)[..Math.Min(200, PersonalityLoader.LoadPersonality(config.PersonalityFile, config.Personality, logger).Length)] + "..."
        });
    }

    /// <summary>
    /// Test a personality prompt without changing the config
    /// </summary>
    [HttpGet("personality")]
    [Experimental("SKEXP0050")]
    [AskPermission("michan.admin")]
    public async Task<ActionResult> GetPersonality()
    {
        return Ok(new
        {
            personality = michan.GetPersonality()
        });
    }

    /// <summary>
    /// Clear MiChan's memory for a specific context or all contexts
    /// </summary>
    [HttpDelete("memory")]
    public async Task<ActionResult> ClearMemory([FromQuery] string? contextId)
    {
        if (!string.IsNullOrEmpty(contextId))
        {
            await memoryService.ClearContextAsync(contextId);
            return Ok(new { message = $"Cleared memory for context {contextId}" });
        }
        else
        {
            var contexts = await memoryService.GetActiveContextsAsync();
            foreach (var ctx in contexts)
            {
                await memoryService.ClearContextAsync(ctx);
            }
            return Ok(new { message = $"Cleared memory for {contexts.Count} contexts" });
        }
    }
}

#pragma warning restore SKEXP0050

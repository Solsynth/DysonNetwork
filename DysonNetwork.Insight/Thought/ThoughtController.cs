#pragma warning disable SKEXP0050
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using DysonNetwork.Insight.MiChan;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Mvc;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace DysonNetwork.Insight.Thought;

[ApiController]
[Route("/api/thought")]
public class ThoughtController(
    ThoughtService service,
    MiChanConfig miChanConfig
) : ControllerBase
{
    public static readonly List<string> AvailableProposals = ["post_create"];
    public static readonly List<string> AvailableBots = ["snchan", "michan"];

    public class StreamThinkingRequest
    {
        [Required] public string UserMessage { get; set; } = null!;

        public string Bot { get; set; } = "snchan"; // "snchan" or "michan"

        public Guid? SequenceId { get; set; }
        public List<string>? AttachedPosts { get; set; } = [];
        public List<Dictionary<string, dynamic>>? AttachedMessages { get; set; }
        public List<string> AcceptProposals { get; set; } = [];
    }

    public class UpdateSharingRequest
    {
        public bool IsPublic { get; set; }
    }

    public class BotInfo
    {
        public string Id { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string Description { get; set; } = null!;
    }

    public class ThoughtServicesResponse
    {
        public string DefaultBot { get; set; } = null!;
        public IEnumerable<BotInfo> Bots { get; set; } = null!;
    }

    [HttpGet("services")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<ThoughtServicesResponse> GetAvailableServices()
    {
        var bots = new List<BotInfo>
        {
            new()
            {
                Id = "snchan",
                Name = "Sn-chan",
                Description = "Cute and helpful assistant with full access to Solar Network tools via gRPC"
            },
            new()
            {
                Id = "michan",
                Name = "MiChan",
                Description = "Casual and friendly AI that lives on the Solar Network with API access"
            }
        };

        return Ok(new ThoughtServicesResponse
        {
            DefaultBot = "snchan",
            Bots = bots
        });
    }

    [HttpPost]
    [Experimental("SKEXP0110")]
    public async Task<ActionResult> Think([FromBody] StreamThinkingRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        // Validate bot selection
        if (!AvailableBots.Contains(request.Bot.ToLower()))
            return BadRequest($"Invalid bot. Available bots: {string.Join(", ", AvailableBots)}");

        if (request.AcceptProposals.Any(e => !AvailableProposals.Contains(e)))
            return BadRequest("Request contains unavailable proposal");

        // Route to appropriate bot
        if (request.Bot.ToLower() == "michan")
        {
            return await ThinkWithMiChanAsync(request, currentUser, accountId);
        }
        else
        {
            return await ThinkWithSnChanAsync(request, currentUser, accountId);
        }
    }

    private async Task<ActionResult> ThinkWithSnChanAsync(StreamThinkingRequest request, Account currentUser,
        Guid accountId)
    {
        var (serviceId, serviceInfo) = service.GetSnChanServiceInfo();
        if (serviceInfo is null)
        {
            return BadRequest("Service not found or configured.");
        }

        if (serviceInfo.PerkLevel > 0 && !currentUser.IsSuperuser)
            if (currentUser.PerkSubscription is null ||
                PerkSubscriptionPrivilege.GetPrivilegeFromIdentifier(currentUser.PerkSubscription.Identifier) <
                serviceInfo.PerkLevel)
                return StatusCode(403, "Not enough perk level");

        var kernel = service.GetSnChanKernel();
        if (kernel is null)
        {
            return BadRequest("Service not found or configured.");
        }

        // Generate a topic if creating a new sequence
        string? topic = null;
        if (!request.SequenceId.HasValue)
        {
            topic = await service.GenerateTopicAsync(request.UserMessage, useMiChan: false);
            if (topic is null)
            {
                return BadRequest("Default service not found or configured.");
            }
        }

        // Handle sequence (creates if new)
        var sequence = await service.GetOrCreateSequenceAsync(accountId, request.SequenceId, topic);
        if (sequence == null) return Forbid();

        // Save user thought with bot identifier
        await service.SaveThoughtAsync(sequence, [
            new SnThinkingMessagePart
            {
                Type = ThinkingMessagePartType.Text,
                Text = request.UserMessage
            }
        ], ThinkingThoughtRole.User, botName: "snchan");

        // Build chat history using service
        var chatHistory = await service.BuildSnChanChatHistoryAsync(
            sequence,
            currentUser,
            request.UserMessage,
            request.AttachedPosts,
            request.AttachedMessages,
            request.AcceptProposals
        );

        // Set response for streaming
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.StatusCode = 200;

        var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
        var executionSettings = service.CreateSnChanExecutionSettings();

        var assistantParts = new List<SnThinkingMessagePart>();
        var fullResponse = new StringBuilder();

        while (true)
        {
            var textContentBuilder = new StringBuilder();
            AuthorRole? authorRole = null;
            var functionCallBuilder = new FunctionCallContentBuilder();

            await foreach (
                var streamingContent in chatCompletionService.GetStreamingChatMessageContentsAsync(
                    chatHistory, executionSettings, kernel)
            )
            {
                authorRole ??= streamingContent.Role;

                if (streamingContent.Content is not null)
                {
                    textContentBuilder.Append(streamingContent.Content);
                    fullResponse.Append(streamingContent.Content);
                    var messageJson = JsonSerializer.Serialize(new
                        { type = "text", data = streamingContent.Content });
                    await Response.Body.WriteAsync(Encoding.UTF8.GetBytes($"data: {messageJson}\n\n"));
                    await Response.Body.FlushAsync();
                }

                functionCallBuilder.Append(streamingContent);
            }

            var finalMessageText = textContentBuilder.ToString();
            if (!string.IsNullOrEmpty(finalMessageText))
            {
                assistantParts.Add(new SnThinkingMessagePart
                    { Type = ThinkingMessagePartType.Text, Text = finalMessageText });
            }

            var functionCalls = functionCallBuilder.Build()
                .Where(fc => !string.IsNullOrEmpty(fc.Id)).ToList();

            if (functionCalls.Count == 0)
                break;

            var assistantMessage = new ChatMessageContent(
                authorRole ?? AuthorRole.Assistant,
                string.IsNullOrEmpty(finalMessageText) ? null : finalMessageText
            );
            foreach (var functionCall in functionCalls)
            {
                assistantMessage.Items.Add(functionCall);
            }

            chatHistory.Add(assistantMessage);

            foreach (var functionCall in functionCalls)
            {
                var part = new SnThinkingMessagePart
                {
                    Type = ThinkingMessagePartType.FunctionCall,
                    FunctionCall = new SnFunctionCall
                    {
                        Id = functionCall.Id!,
                        PluginName = functionCall.PluginName,
                        Name = functionCall.FunctionName,
                        Arguments = JsonSerializer.Serialize(functionCall.Arguments)
                    }
                };
                assistantParts.Add(part);

                var messageJson = JsonSerializer.Serialize(new { type = "function_call", data = part.FunctionCall });
                await Response.Body.WriteAsync(Encoding.UTF8.GetBytes($"data: {messageJson}\n\n"));
                await Response.Body.FlushAsync();

                FunctionResultContent resultContent;
                try
                {
                    var result = await functionCall.InvokeAsync(kernel);
                    resultContent = new FunctionResultContent(
                        callId: functionCall.Id!,
                        functionName: functionCall.FunctionName,
                        pluginName: functionCall.PluginName,
                        result: result.Result?.ToString()
                    );
                }
                catch (Exception ex)
                {
                    resultContent = new FunctionResultContent(
                        callId: functionCall.Id!,
                        functionName: functionCall.FunctionName,
                        pluginName: functionCall.PluginName,
                        result: ex.Message
                    );
                }

                chatHistory.Add(resultContent.ToChatMessage());

                var resultPart = new SnThinkingMessagePart
                {
                    Type = ThinkingMessagePartType.FunctionResult,
                    FunctionResult = new SnFunctionResult
                    {
                        CallId = resultContent.CallId!,
                        PluginName = resultContent.PluginName,
                        FunctionName = resultContent.FunctionName,
                        Result = resultContent.Result!,
                        IsError = resultContent.Result is Exception
                    }
                };
                assistantParts.Add(resultPart);

                var resultMessageJson = JsonSerializer.Serialize(new
                    { type = "function_result", data = resultPart.FunctionResult });
                await Response.Body.WriteAsync(Encoding.UTF8.GetBytes($"data: {resultMessageJson}\n\n"));
                await Response.Body.FlushAsync();
            }
        }

        // Save assistant thought
        var savedThought = await service.SaveThoughtAsync(
            sequence,
            assistantParts,
            ThinkingThoughtRole.Assistant,
            miChanConfig.ThinkingService,
            botName: "snchan"
        );

        // Write final metadata
        using (var streamBuilder = new MemoryStream())
        {
            await streamBuilder.WriteAsync("\n\n"u8.ToArray());
            if (topic != null)
            {
                var topicJson = JsonSerializer.Serialize(new { type = "topic", data = sequence.Topic ?? "" });
                await streamBuilder.WriteAsync(Encoding.UTF8.GetBytes($"topic: {topicJson}\n\n"));
            }

            var thoughtJson = JsonSerializer.Serialize(new { type = "thought", data = savedThought },
                InfraObjectCoder.SerializerOptions);
            await streamBuilder.WriteAsync(Encoding.UTF8.GetBytes($"thought: {thoughtJson}\n\n"));
            var outputBytes = streamBuilder.ToArray();
            await Response.Body.WriteAsync(outputBytes);
            await Response.Body.FlushAsync();
        }

        return new EmptyResult();
    }

    private async Task<ActionResult> ThinkWithMiChanAsync(StreamThinkingRequest request, Account currentUser,
        Guid accountId)
    {
        var (serviceId, serviceInfo) = service.GetMiChanServiceInfo();
        if (serviceInfo is null)
        {
            return BadRequest("Service not found or configured.");
        }

        if (serviceInfo.PerkLevel > 0 && !currentUser.IsSuperuser)
            if (currentUser.PerkSubscription is null ||
                PerkSubscriptionPrivilege.GetPrivilegeFromIdentifier(currentUser.PerkSubscription.Identifier) <
                serviceInfo.PerkLevel)
                return StatusCode(403, "Not enough perk level");

        var kernel = service.GetMiChanKernel();
        if (kernel is null)
        {
            return BadRequest("Service not found or configured.");
        }

        string? topic = null;
        if (!request.SequenceId.HasValue)
        {
            topic = await service.GenerateTopicAsync(request.UserMessage, useMiChan: true);
            if (topic is null)
            {
                return BadRequest("Default service not found or configured.");
            }
        }

        var sequence = await service.GetOrCreateSequenceAsync(accountId, request.SequenceId, topic);
        if (sequence == null) return Forbid();

        await service.SaveThoughtAsync(sequence, [
            new SnThinkingMessagePart
            {
                Type = ThinkingMessagePartType.Text,
                Text = request.UserMessage
            }
        ], ThinkingThoughtRole.User, botName: "michan");

        var (chatHistory, shouldRefuse, refusalReason) = await service.BuildMiChanChatHistoryAsync(
            sequence,
            currentUser,
            request.UserMessage,
            request.AttachedPosts,
            request.AttachedMessages,
            request.AcceptProposals
        );

        if (shouldRefuse)
        {
            var refusalJson = JsonSerializer.Serialize(new { type = "text", data = refusalReason ?? "I cannot do that." });
            await Response.Body.WriteAsync(Encoding.UTF8.GetBytes($"data: {refusalJson}\n\n"));
            await Response.Body.FlushAsync();
            return new EmptyResult();
        }

        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.StatusCode = 200;

        var chatService = kernel.GetRequiredService<IChatCompletionService>();
        var executionSettings = service.CreateMiChanExecutionSettings();

        var assistantParts = new List<SnThinkingMessagePart>();
        var fullResponse = new StringBuilder();

        var contextId = $"{sequence.Id}-{Guid.NewGuid()}";

        while (true)
        {
            var textContentBuilder = new StringBuilder();
            AuthorRole? authorRole = null;
            var functionCallBuilder = new FunctionCallContentBuilder();

            await foreach (var streamingContent in chatService.GetStreamingChatMessageContentsAsync(
                               chatHistory, executionSettings, kernel))
            {
                authorRole ??= streamingContent.Role;

                if (streamingContent.Content is not null)
                {
                    textContentBuilder.Append(streamingContent.Content);
                    fullResponse.Append(streamingContent.Content);
                    var messageJson = JsonSerializer.Serialize(new { type = "text", data = streamingContent.Content });
                    await Response.Body.WriteAsync(Encoding.UTF8.GetBytes($"data: {messageJson}\n\n"));
                    await Response.Body.FlushAsync();
                }

                functionCallBuilder.Append(streamingContent);
            }

            var finalMessageText = textContentBuilder.ToString();
            if (!string.IsNullOrEmpty(finalMessageText))
            {
                assistantParts.Add(new SnThinkingMessagePart
                {
                    Type = ThinkingMessagePartType.Text,
                    Text = finalMessageText
                });
            }

            var functionCalls = functionCallBuilder.Build()
                .Where(fc => !string.IsNullOrEmpty(fc.Id)).ToList();

            if (functionCalls.Count == 0)
                break;

            var assistantMessage = new ChatMessageContent(
                authorRole ?? AuthorRole.Assistant,
                string.IsNullOrEmpty(finalMessageText) ? null : finalMessageText
            );
            foreach (var functionCall in functionCalls)
            {
                assistantMessage.Items.Add(functionCall);
            }

            chatHistory.Add(assistantMessage);

            foreach (var functionCall in functionCalls)
            {
                var part = new SnThinkingMessagePart
                {
                    Type = ThinkingMessagePartType.FunctionCall,
                    FunctionCall = new SnFunctionCall
                    {
                        Id = functionCall.Id!,
                        PluginName = functionCall.PluginName,
                        Name = functionCall.FunctionName,
                        Arguments = JsonSerializer.Serialize(functionCall.Arguments)
                    }
                };
                assistantParts.Add(part);

                var messageJson = JsonSerializer.Serialize(new { type = "function_call", data = part.FunctionCall });
                await Response.Body.WriteAsync(Encoding.UTF8.GetBytes($"data: {messageJson}\n\n"));
                await Response.Body.FlushAsync();

                FunctionResultContent resultContent;
                try
                {
                    var result = await functionCall.InvokeAsync(kernel);
                    resultContent = new FunctionResultContent(
                        callId: functionCall.Id!,
                        functionName: functionCall.FunctionName,
                        pluginName: functionCall.PluginName,
                        result: result.Result?.ToString()
                    );
                }
                catch (Exception ex)
                {
                    resultContent = new FunctionResultContent(
                        callId: functionCall.Id!,
                        functionName: functionCall.FunctionName,
                        pluginName: functionCall.PluginName,
                        result: ex.Message
                    );
                }

                chatHistory.Add(resultContent.ToChatMessage());

                var resultPart = new SnThinkingMessagePart
                {
                    Type = ThinkingMessagePartType.FunctionResult,
                    FunctionResult = new SnFunctionResult
                    {
                        CallId = resultContent.CallId!,
                        PluginName = resultContent.PluginName,
                        FunctionName = resultContent.FunctionName,
                        Result = resultContent.Result!,
                        IsError = resultContent.Result is Exception
                    }
                };
                assistantParts.Add(resultPart);

                var resultMessageJson = JsonSerializer.Serialize(new
                    { type = "function_result", data = resultPart.FunctionResult });
                await Response.Body.WriteAsync(Encoding.UTF8.GetBytes($"data: {resultMessageJson}\n\n"));
                await Response.Body.FlushAsync();
            }
        }

        // Save assistant thought
        var savedThought = await service.SaveThoughtAsync(
            sequence,
            assistantParts,
            ThinkingThoughtRole.Assistant,
            miChanConfig.ThinkingService,
            botName: "michan"
        );

        // Store in MiChan memory
        await service.StoreMiChanInteractionAsync(
            contextId,
            request.UserMessage,
            fullResponse.ToString(),
            currentUser.IsSuperuser
        );

        // Write final metadata
        using (var streamBuilder = new MemoryStream())
        {
            await streamBuilder.WriteAsync("\n\n"u8.ToArray());
            if (topic != null)
            {
                var topicJson = JsonSerializer.Serialize(new { type = "topic", data = sequence.Topic ?? "" });
                await streamBuilder.WriteAsync(Encoding.UTF8.GetBytes($"topic: {topicJson}\n\n"));
            }

            var thoughtJson = JsonSerializer.Serialize(new { type = "thought", data = savedThought },
                InfraObjectCoder.SerializerOptions);
            await streamBuilder.WriteAsync(Encoding.UTF8.GetBytes($"thought: {thoughtJson}\n\n"));
            var outputBytes = streamBuilder.ToArray();
            await Response.Body.WriteAsync(outputBytes);
            await Response.Body.FlushAsync();
        }

        return new EmptyResult();
    }

    [HttpGet("sequences")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<SnThinkingSequence>>> ListSequences(
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20
    )
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var (totalCount, sequences) = await service.ListSequencesAsync(accountId, offset, take);

        Response.Headers["X-Total"] = totalCount.ToString();

        return Ok(sequences);
    }

    [HttpPatch("sequences/{sequenceId:guid}/sharing")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult> UpdateSequenceSharing(Guid sequenceId, [FromBody] UpdateSharingRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var sequence = await service.GetSequenceAsync(sequenceId);
        if (sequence == null) return NotFound();
        if (sequence.AccountId != accountId) return Forbid();

        sequence.IsPublic = request.IsPublic;
        await service.UpdateSequenceAsync(sequence);

        return NoContent();
    }

    [HttpGet("sequences/{sequenceId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<SnThinkingThought>>> GetSequenceThoughts(Guid sequenceId)
    {
        var sequence = await service.GetSequenceAsync(sequenceId);
        if (sequence == null) return NotFound();

        if (!sequence.IsPublic)
        {
            if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
            var accountId = Guid.Parse(currentUser.Id);

            if (sequence.AccountId != accountId)
                return StatusCode(403);
        }

        var thoughts = await service.GetPreviousThoughtsAsync(sequence);

        return Ok(thoughts);
    }

    [HttpDelete("sequences/{sequenceId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> DeleteSequenceThoughts(Guid sequenceId)
    {
        var  sequence = await service.GetSequenceAsync(sequenceId);
        if (sequence == null) return NotFound();
        
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        if (sequence.AccountId != accountId)
            return StatusCode(403);
        
        await service.DeleteSequenceAsync(sequenceId);
        return Ok();
    }
    
    /// <summary>
    /// Manually trigger memory analysis for a thought sequence.
    /// MiChan will read the conversation and decide what to memorize.
    /// </summary>
    [HttpPost("sequences/{sequenceId:guid}/memorize")]
    [AskPermission("michan.admin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> MemorizeSequence(Guid sequenceId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var sequence = await service.GetSequenceAsync(sequenceId);
        if (sequence == null) return NotFound();

        if (sequence.AccountId != accountId)
        {
            if (!sequence.IsPublic)
                return Forbid();
        }

        var (success, summary) = await service.MemorizeSequenceAsync(sequenceId, accountId);

        if (!success)
        {
            return BadRequest(new { error = summary });
        }

        return Ok(new { success, summary, sequenceId });
    }
}

#pragma warning restore SKEXP0050

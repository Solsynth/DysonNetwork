#pragma warning disable SKEXP0050
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using DysonNetwork.Insight.MiChan;
using DysonNetwork.Insight.MiChan.Plugins;
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
    MiChanConfig miChanConfig,
    IServiceProvider serviceProvider,
    DyFileService.DyFileServiceClient files,
    FreeQuotaService freeQuotaService,
    ILogger<ThoughtController> logger
) : ControllerBase
{
    public static readonly List<string> AvailableProposals = ["post_create"];
    public static readonly List<string> AvailableBots = ["snchan", "michan"];

    public class StreamThinkingRequest
    {
        public string? UserMessage { get; set; }

        public string Bot { get; set; } = "snchan"; // "snchan" or "michan"

        public Guid? SequenceId { get; set; }
        public List<string>? AttachedPosts { get; set; } = [];
        public List<string>? AttachedFiles { get; set; } = [];
        public List<Dictionary<string, dynamic>>? AttachedMessages { get; set; }
        public List<string> AcceptProposals { get; set; } = [];
        public string? ReasoningEffort { get; set; } // "low", "medium", "high"
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
                Name = "SN Chan",
                Description = "The helpful assistant who have ability to solve problems for you on the Solar Network."
            },
            new()
            {
                Id = "michan",
                Name = "Mi Chan",
                Description = "A mysterious girl"
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
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        if ((request.AttachedFiles is null || request.AttachedFiles.Count == 0) &&
            string.IsNullOrWhiteSpace(request.UserMessage))
            return BadRequest("You cannot send empty messages.");

        if (request.AcceptProposals.Any(e => !AvailableProposals.Contains(e)))
            return BadRequest("Request contains unavailable proposal");

        return request.Bot.ToLower() switch
        {
            // Route to appropriate bot
            "michan" => await ThinkWithMiChanAsync(request, currentUser, accountId),
            "snchan" => await ThinkWithSnChanAsync(request, currentUser, accountId),
            _ => BadRequest($"Invalid bot. Available bots: {string.Join(", ", AvailableBots)}")
        };
    }

    private async Task<ActionResult> ThinkWithSnChanAsync(
        StreamThinkingRequest request,
        DyAccount currentUser,
        Guid accountId
    )
    {
        var serviceInfo = service.GetSnChanServiceInfo();
        if (serviceInfo is null)
            return BadRequest("Service not found or configured.");

        if (request.AttachedFiles is { Count: > 0 })
            return BadRequest("Sorry, SN-chan currently does not support requests with files attached.");

        if (serviceInfo.PerkLevel > 0 && !currentUser.IsSuperuser)
            if (currentUser.PerkLevel < serviceInfo.PerkLevel)
                return StatusCode(403, "Not enough perk level");

        if (request.SequenceId.HasValue &&
            await service.IsCanonicalMiChanSequenceAsync(accountId, request.SequenceId.Value))
        {
            return BadRequest("SnChan cannot use MiChan's unified conversation. Start a new SnChan thread instead.");
        }

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
        var userPart = new SnThinkingMessagePart
        {
            Type = ThinkingMessagePartType.Text,
            Metadata = new Dictionary<string, object>(),
            Text = request.UserMessage
        };
        if (request.AttachedMessages is not null) userPart.Metadata.Add("attached_messages", request.AttachedMessages);
        if (request.AttachedPosts is not null) userPart.Metadata.Add("attached_posts", request.AttachedPosts);
        await service.SaveThoughtAsync(sequence, [userPart], ThinkingThoughtRole.User, botName: "snchan");

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
        var executionSettings = service.CreateSnChanExecutionSettings(request.ReasoningEffort);

        var assistantParts = new List<SnThinkingMessagePart>();
        var fullResponse = new StringBuilder();
        var reasoningBuilder = new StringBuilder();
        var hasReasoning = false;

        const int maxMiChanToolRounds = 8;
        var toolRound = 0;
        var repeatedToolCalls = new Dictionary<string, int>();

        while (true)
        {
            toolRound++;
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

                if (streamingContent.Metadata != null)
                {
                    object? reasoningContent = null;
                    if (streamingContent.Metadata.TryGetValue("reasoning_content", out reasoningContent) ||
                        streamingContent.Metadata.TryGetValue("thinking", out reasoningContent))
                    {
                        var reasoningText = reasoningContent?.ToString();
                        if (!string.IsNullOrEmpty(reasoningText))
                        {
                            hasReasoning = true;
                            reasoningBuilder.Append(reasoningText);
                            var reasoningJson = JsonSerializer.Serialize(new
                                { type = "reasoning", data = reasoningText });
                            await Response.Body.WriteAsync(Encoding.UTF8.GetBytes($"data: {reasoningJson}\n\n"));
                            await Response.Body.FlushAsync();
                        }
                    }
                }

                if (streamingContent.InnerContent is { } innerContent)
                {
                    var innerTypeName = innerContent.GetType().Name;
                    if (innerTypeName.Contains("Reasoning"))
                    {
                        var prop = innerContent.GetType().GetProperty("Thinking");
                        var reasoningText = prop?.GetValue(innerContent)?.ToString();
                        if (!string.IsNullOrEmpty(reasoningText))
                        {
                            hasReasoning = true;
                            reasoningBuilder.Append(reasoningText);
                            var reasoningJson = JsonSerializer.Serialize(new
                                { type = "reasoning", data = reasoningText });
                            await Response.Body.WriteAsync(Encoding.UTF8.GetBytes($"data: {reasoningJson}\n\n"));
                            await Response.Body.FlushAsync();
                        }
                    }
                }

                functionCallBuilder.Append(streamingContent);
            }

            var finalMessageText = textContentBuilder.ToString();
            if (!string.IsNullOrEmpty(finalMessageText))
            {
                assistantParts.Add(new SnThinkingMessagePart
                    { Type = ThinkingMessagePartType.Text, Text = finalMessageText });
            }

            if (hasReasoning)
            {
                assistantParts.Add(new SnThinkingMessagePart
                {
                    Type = ThinkingMessagePartType.Reasoning,
                    Reasoning = reasoningBuilder.ToString()
                });
            }

            var functionCalls = functionCallBuilder.Build()
                .Where(fc => !string.IsNullOrEmpty(fc.Id)).ToList();

            if (functionCalls.Count == 0)
                break;

            if (toolRound > maxMiChanToolRounds)
            {
                const string fallbackMessage = "抱歉，刚才内部处理花了太多轮。我先停下来：请再说一次你的需求，或把问题缩小一点，我会直接回答你。";
                assistantParts.Add(new SnThinkingMessagePart
                {
                    Type = ThinkingMessagePartType.Text,
                    Text = fallbackMessage
                });
                fullResponse.Append(fallbackMessage);
                var fallbackJson = JsonSerializer.Serialize(new { type = "text", data = fallbackMessage });
                await Response.Body.WriteAsync(Encoding.UTF8.GetBytes($"data: {fallbackJson}\n\n"));
                await Response.Body.FlushAsync();
                break;
            }

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
                var toolSignature = $"{functionCall.PluginName}.{functionCall.FunctionName}:{JsonSerializer.Serialize(functionCall.Arguments)}";
                repeatedToolCalls.TryGetValue(toolSignature, out var duplicateCount);
                repeatedToolCalls[toolSignature] = duplicateCount + 1;

                if (duplicateCount >= 2)
                {
                    resultContent = new FunctionResultContent(
                        callId: functionCall.Id!,
                        functionName: functionCall.FunctionName,
                        pluginName: functionCall.PluginName,
                        result: "Skipped repeated tool call because the same call was requested too many times in one reply. Reply to the user directly instead."
                    );
                }
                else
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

            chatHistory.AddSystemMessage("工具结果已经返回。除非确实缺少关键参数，否则不要继续重复调用工具，直接向用户作答。");
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

    private async Task<ActionResult> ThinkWithMiChanAsync(
        StreamThinkingRequest request,
        DyAccount currentUser,
        Guid accountId
    )
    {
        var overallStopwatch = Stopwatch.StartNew();
        logger.LogInformation(
            "Received MiChan thought request from user {AccountId}. sequenceId={SequenceId}, attachedPosts={AttachedPostsCount}, attachedFiles={AttachedFilesCount}, attachedMessages={AttachedMessagesCount}",
            accountId,
            request.SequenceId,
            request.AttachedPosts?.Count ?? 0,
            request.AttachedFiles?.Count ?? 0,
            request.AttachedMessages?.Count ?? 0
        );

        var serviceInfo = service.GetMiChanServiceInfo(request.AttachedFiles is { Count: > 0 });
        if (serviceInfo is null)
            return BadRequest("Service not found or configured.");

        if (serviceInfo.PerkLevel > 0 && !currentUser.IsSuperuser)
            if (currentUser.PerkLevel < serviceInfo.PerkLevel)
                return StatusCode(403, "Not enough perk level");

        var canonicalSequence = await service.GetCanonicalMiChanSequenceAsync(accountId);
        if (canonicalSequence != null &&
            request.SequenceId.HasValue &&
            request.SequenceId.Value != canonicalSequence.Id)
        {
            return BadRequest("MiChan now uses a unified conversation. Please continue with the canonical MiChan sequence.");
        }

        string? topic = null;
        if (canonicalSequence == null)
        {
            if (request.SequenceId.HasValue)
            {
                return BadRequest("MiChan now uses a unified conversation. Start without sequenceId to create the canonical MiChan thread.");
            }

            topic = await service.GenerateTopicAsync(request.UserMessage, useMiChan: true);
            if (topic is null)
            {
                return BadRequest("Default service not found or configured.");
            }
        }

        var resolution = await service.ResolveMiChanSequenceAsync(accountId, request.SequenceId, topic);
        if (resolution.ErrorMessage != null)
        {
            return BadRequest(resolution.ErrorMessage);
        }

        var sequence = resolution.Sequence;
        if (sequence == null) return Forbid();
        logger.LogInformation(
            "MiChan request resolved sequence {SequenceId} for user {AccountId}. created={Created}, topicGenerated={TopicGenerated}",
            sequence.Id,
            accountId,
            resolution.Created,
            !string.IsNullOrWhiteSpace(topic)
        );

        var filesRetrieveRequest = new DyGetFileBatchRequest();
        if (request.AttachedFiles is { Count: > 0 })
            filesRetrieveRequest.Ids.AddRange(request.AttachedFiles);
        var filesData = request.AttachedFiles is { Count: > 0 }
            ? (await files.GetFileBatchAsync(filesRetrieveRequest)).Files.ToList()
            : null;
        logger.LogDebug(
            "MiChan request fetched {FilesCount} attached files for sequence {SequenceId} in {ElapsedMs}ms",
            filesData?.Count ?? 0,
            sequence.Id,
            overallStopwatch.ElapsedMilliseconds
        );

        var userPart = new SnThinkingMessagePart
        {
            Type = ThinkingMessagePartType.Text,
            Metadata = new Dictionary<string, object>(),
            Text = request.UserMessage
        };
        if (request.AttachedMessages is not null) userPart.Metadata.Add("attached_messages", request.AttachedMessages);
        if (request.AttachedPosts is not null) userPart.Metadata.Add("attached_posts", request.AttachedPosts);
        if (filesData is not null)
            userPart.Files = filesData.Select(SnCloudFileReferenceObject.FromProtoValue).ToList();
        var userThought = await service.SaveThoughtAsync(sequence, [userPart], ThinkingThoughtRole.User, botName: "michan");

        await service.TouchMiChanUserProfileAsync(accountId);

        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.StatusCode = 200;
        var preparingJson = JsonSerializer.Serialize(new { type = "status", data = "preparing_context" });
        await Response.Body.WriteAsync(Encoding.UTF8.GetBytes($"data: {preparingJson}\n\n"));
        await Response.Body.FlushAsync();
        logger.LogInformation(
            "MiChan SSE stream opened for user {AccountId}, sequence {SequenceId} at {ElapsedMs}ms",
            accountId,
            sequence.Id,
            overallStopwatch.ElapsedMilliseconds
        );

        var historyStopwatch = Stopwatch.StartNew();
        var (chatHistory, useVisionKernel) = await service.BuildMiChanChatHistoryAsync(
            sequence,
            currentUser,
            request.UserMessage,
            request.AttachedPosts,
            request.AttachedMessages,
            request.AcceptProposals,
            userPart.Files ?? [],
            userThought.Id
        );
        logger.LogInformation(
            "MiChan context prepared for user {AccountId}, sequence {SequenceId} in {ElapsedMs}ms. useVisionKernel={UseVisionKernel}, chatMessages={ChatMessagesCount}",
            accountId,
            sequence.Id,
            historyStopwatch.ElapsedMilliseconds,
            useVisionKernel,
            chatHistory.Count
        );

        var kernel = useVisionKernel ? service.GetMiChanVisionKernel() : service.GetMiChanKernel();

        // Register plugins using centralized extension method
        kernel.AddMiChanPlugins(serviceProvider);

        var chatService = kernel.GetRequiredService<IChatCompletionService>();
        var executionSettings = service.CreateMiChanExecutionSettings(request.ReasoningEffort);

        var assistantParts = new List<SnThinkingMessagePart>();
        var fullResponse = new StringBuilder();
        var reasoningBuilder = new StringBuilder();
        var hasReasoning = false;

        var firstChunkStopwatch = Stopwatch.StartNew();
        var streamedAnyContent = false;
        var toolRound = 0;

        while (true)
        {
            toolRound++;
            var textContentBuilder = new StringBuilder();
            AuthorRole? authorRole = null;
            var functionCallBuilder = new FunctionCallContentBuilder();

            await foreach (var streamingContent in chatService.GetStreamingChatMessageContentsAsync(
                               chatHistory, executionSettings, kernel))
            {
                authorRole ??= streamingContent.Role;

                if (streamingContent.Content is not null)
                {
                    if (!streamedAnyContent)
                    {
                        streamedAnyContent = true;
                        logger.LogInformation(
                            "MiChan streamed first text chunk for user {AccountId}, sequence {SequenceId} after {ElapsedMs}ms",
                            accountId,
                            sequence.Id,
                            firstChunkStopwatch.ElapsedMilliseconds
                        );
                    }

                    textContentBuilder.Append(streamingContent.Content);
                    fullResponse.Append(streamingContent.Content);
                    var messageJson = JsonSerializer.Serialize(new { type = "text", data = streamingContent.Content });
                    await Response.Body.WriteAsync(Encoding.UTF8.GetBytes($"data: {messageJson}\n\n"));
                    await Response.Body.FlushAsync();
                }

                if (streamingContent.Metadata != null)
                {
                    object? reasoningContent = null;
                    if (streamingContent.Metadata.TryGetValue("reasoning_content", out reasoningContent) ||
                        streamingContent.Metadata.TryGetValue("thinking", out reasoningContent))
                    {
                        var reasoningText = reasoningContent?.ToString();
                        if (!string.IsNullOrEmpty(reasoningText))
                        {
                            hasReasoning = true;
                            reasoningBuilder.Append(reasoningText);
                            var reasoningJson = JsonSerializer.Serialize(new
                                { type = "reasoning", data = reasoningText });
                            await Response.Body.WriteAsync(Encoding.UTF8.GetBytes($"data: {reasoningJson}\n\n"));
                            await Response.Body.FlushAsync();
                        }
                    }
                }

                if (streamingContent.InnerContent is { } innerContent)
                {
                    var innerTypeName = innerContent.GetType().Name;
                    if (innerTypeName.Contains("Reasoning"))
                    {
                        var prop = innerContent.GetType().GetProperty("Thinking");
                        var reasoningText = prop?.GetValue(innerContent)?.ToString();
                        if (!string.IsNullOrEmpty(reasoningText))
                        {
                            hasReasoning = true;
                            reasoningBuilder.Append(reasoningText);
                            var reasoningJson = JsonSerializer.Serialize(new
                                { type = "reasoning", data = reasoningText });
                            await Response.Body.WriteAsync(Encoding.UTF8.GetBytes($"data: {reasoningJson}\n\n"));
                            await Response.Body.FlushAsync();
                        }
                    }
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

            if (hasReasoning)
            {
                assistantParts.Add(new SnThinkingMessagePart
                {
                    Type = ThinkingMessagePartType.Reasoning,
                    Reasoning = reasoningBuilder.ToString()
                });
            }

            var functionCalls = functionCallBuilder.Build()
                .Where(fc => !string.IsNullOrEmpty(fc.Id)).ToList();

            if (functionCalls.Count == 0)
                break;

            logger.LogInformation(
                "MiChan entered tool round {ToolRound} for user {AccountId}, sequence {SequenceId} with {FunctionCallCount} tool calls",
                toolRound,
                accountId,
                sequence.Id,
                functionCalls.Count
            );

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
            useVisionKernel ? miChanConfig.Vision.VisionThinkingService : miChanConfig.ThinkingService,
            botName: "michan"
        );
        logger.LogInformation(
            "MiChan completed thought request for user {AccountId}, sequence {SequenceId} in {ElapsedMs}ms. assistantParts={AssistantPartsCount}, responseChars={ResponseLength}",
            accountId,
            sequence.Id,
            overallStopwatch.ElapsedMilliseconds,
            assistantParts.Count,
            fullResponse.Length
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
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var (totalCount, sequences) = await service.ListSequencesAsync(accountId, offset, take);

        Response.Headers["X-Total"] = totalCount.ToString();

        return Ok(sequences);
    }

    [HttpGet("michan/sequence")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SnThinkingSequence>> GetMiChanUnifiedSequence()
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var sequence = await service.GetCanonicalMiChanSequenceAsync(accountId);
        if (sequence == null)
        {
            return NotFound();
        }

        return Ok(sequence);
    }

    [HttpPatch("sequences/{sequenceId:guid}/sharing")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult> UpdateSequenceSharing(Guid sequenceId, [FromBody] UpdateSharingRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
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
    public async Task<ActionResult<List<SnThinkingThought>>> GetSequenceThoughts(
        Guid sequenceId,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 50)
    {
        if (offset < 0) return BadRequest("offset must be greater than or equal to 0.");
        if (take <= 0) return BadRequest("take must be greater than 0.");
        take = Math.Min(take, 200);

        var currentUser = HttpContext.Items["CurrentUser"] as DyAccount;
        var currentAccountId = currentUser != null ? Guid.Parse(currentUser.Id) : (Guid?)null;

        var sequence = await service.GetSequenceAsync(sequenceId);
        if (sequence == null && currentAccountId.HasValue)
        {
            sequence = await service.ResolveSequenceForOwnerAsync(currentAccountId.Value, sequenceId);
        }

        if (sequence == null) return NotFound();

        if (!sequence.IsPublic)
        {
            if (currentUser == null) return Unauthorized();
            var accountId = currentAccountId!.Value;

            if (sequence.AccountId != accountId)
                return StatusCode(403);
        }

        var (thoughts, hasMore) = await service.GetVisibleThoughtsPageAsync(sequence, offset, take);
        Response.Headers["X-Has-More"] = hasMore.ToString().ToLowerInvariant();
        Response.Headers["X-Offset"] = offset.ToString();
        Response.Headers["X-Take"] = take.ToString();

        return Ok(thoughts);
    }

    [HttpDelete("sequences/{sequenceId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> DeleteSequenceThoughts(Guid sequenceId)
    {
        var sequence = await service.GetSequenceAsync(sequenceId);
        if (sequence == null) return NotFound();

        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        if (sequence.AccountId != accountId)
            return StatusCode(403);

        await service.DeleteSequenceAsync(sequenceId);
        return Ok();
    }

    /// <summary>
    /// Marks a thought sequence as read by the user.
    /// Updates the UserLastReadAt timestamp for agent-initiated conversations.
    /// </summary>
    [HttpPost("sequences/{sequenceId:guid}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> MarkSequenceAsRead(Guid sequenceId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var sequence = await service.GetSequenceAsync(sequenceId);
        if (sequence == null) return NotFound();

        if (sequence.AccountId != accountId)
            return Forbid();

        await service.MarkSequenceAsReadAsync(sequenceId, accountId);
        return NoContent();
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
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
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

    /// <summary>
    /// Get current user's free token quota status
    /// </summary>
    [HttpGet("quota")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> GetQuotaStatus()
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        if (!freeQuotaService.IsEnabled)
        {
            return Ok(new
            {
                enabled = false,
                message = "Free quota is not enabled"
            });
        }

        var (freeRemaining, freeUsed) = await freeQuotaService.GetFreeQuotaStatusAsync(accountId);

        return Ok(new
        {
            enabled = true,
            tokensPerDay = freeQuotaService.TokensPerDay,
            resetPeriodHours = freeQuotaService.ResetPeriodHours,
            freeRemaining,
            freeUsed,
            freeTotal = freeQuotaService.TokensPerDay
        });
    }

    /// <summary>
    /// Reset free quota for current user (admin only)
    /// </summary>
    [HttpPost("quota/reset")]
    [AskPermission("michan.admin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> ResetQuota()
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        if (!freeQuotaService.IsEnabled)
        {
            return BadRequest(new { error = "Free quota is not enabled" });
        }

        await freeQuotaService.ResetQuotasForAccountAsync(accountId);

        return Ok(new { success = true, message = "Quota reset successfully" });
    }

    /// <summary>
    /// Reset all users' free quotas (admin only)
    /// </summary>
    [HttpPost("quota/reset-all")]
    [AskPermission("michan.admin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> ResetAllQuotas()
    {
        if (!freeQuotaService.IsEnabled)
        {
            return BadRequest(new { error = "Free quota is not enabled" });
        }

        await freeQuotaService.ResetAllQuotasAsync();

        return Ok(new { success = true, message = "All quotas reset successfully" });
    }
}

#pragma warning restore SKEXP0050

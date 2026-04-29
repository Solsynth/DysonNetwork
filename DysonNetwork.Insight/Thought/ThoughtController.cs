using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DysonNetwork.Insight.Agent.Foundation;
using DysonNetwork.Insight.Agent.Foundation.Models;
using DysonNetwork.Insight.Agent.Foundation.Providers;
using DysonNetwork.Insight.Agent.Models;
using DysonNetwork.Insight.MiChan;
using DysonNetwork.Insight.MiChan.Plugins;
using DysonNetwork.Insight.SnChan;
using DysonNetwork.Insight.Thought.Voice;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Insight.Thought;

[ApiController]
[Route("/api/thought")]
public class ThoughtController(
    ThoughtService service,
    MiChanConfig miChanConfig,
    SnChanConfig snChanConfig,
    IServiceProvider serviceProvider,
    DyFileService.DyFileServiceClient files,
    ThinkingVoiceService voiceService,
    FreeQuotaService freeQuotaService,
    IAgentClientProvider agentClientProvider,
    SnChanModelSelector? snChanModelSelector,
    IAgentToolRegistry toolRegistry,
    FoundationChatStreamingService streamingService,
    ISnChanFoundationProvider snChanFoundationProvider,
    IMiChanFoundationProvider miChanFoundationProvider,
    ModelRegistry modelRegistry,
    ILogger<ThoughtController> logger
) : ControllerBase
{
    public static readonly List<string> AvailableProposals = ["post_create"];
    public static readonly List<string> AvailableBots = ["snchan", "michan"];
    public static readonly List<string> AvailableCommands = ["/clear", "/compact", "/reset"];

    public class CommandResponse
    {
        public string Command { get; set; } = null!;
        public string Description { get; set; } = null!;
    }

    public class CommandsListResponse
    {
        public List<CommandResponse> Commands { get; set; } = [];
    }

    public class SequenceMemorySearchResponse
    {
        public int Total { get; set; }
        public List<ThoughtService.SequenceMemoryHit> Results { get; set; } = [];
    }

    public class MemoryMaintenanceResponse
    {
        public bool Success { get; set; }
        public int RoundsExecuted { get; set; }
        public int BackfilledRows { get; set; }
        public int SummarizedSequences { get; set; }
        public bool HasMoreWork { get; set; }
    }

    public class StreamThinkingRequest
    {
        public string? UserMessage { get; set; }

        public string Bot { get; set; } = "snchan"; // "snchan" or "michan"

        public Guid? SequenceId { get; set; }
        public List<string>? AttachedPosts { get; set; } = [];
        public List<string>? AttachedFiles { get; set; } = [];
        public List<string>? AttachedVoices { get; set; } = [];
        public List<Dictionary<string, dynamic>>? AttachedMessages { get; set; }
        public List<string> AcceptProposals { get; set; } = [];
        public string? ReasoningEffort { get; set; } // "low", "medium", "high"
        public bool? Thinking { get; set; } // Enable/disable thinking mode (default: true for capable models)
        public string? Model { get; set; } // Custom model ID to use (optional)
        public string? Topic { get; set; } // Topic for new thread creation (when no sequenceId provided)
    }

    public class UpdateSharingRequest
    {
        public bool IsPublic { get; set; }
    }

    public class SendVoiceThinkingRequest
    {
        [Required]
        public IFormFile File { get; set; } = null!;
        public Guid? SequenceId { get; set; }
        public string Bot { get; set; } = "michan";
        public int? DurationMs { get; set; }
    }

    public class BotModelInfo
    {
        public string Id { get; set; } = null!;
        public string DisplayName { get; set; } = null!;
        public string Description { get; set; }
        public int MinPerkLevel { get; set; }
        public bool IsDefault { get; set; }
    }

    public class BotInfo
    {
        public string Id { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string Description { get; set; } = null!;
        public List<BotModelInfo> AvailableModels { get; set; } = [];
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
        var currentUser = HttpContext.Items["CurrentUser"] as DyAccount;
        var perkLevel = currentUser?.PerkLevel ?? 0;

        var bots = new List<BotInfo>();

        // SN-chan bot with available models
        var snChanModels = new List<BotModelInfo>();
        if (snChanConfig.UseModelSelection && snChanModelSelector != null)
        {
            var availableModels = snChanModelSelector.GetAvailableModels(
                ModelUseCase.SnChanChat,
                perkLevel
            );
            snChanModels = availableModels
                .Select(m => new BotModelInfo
                {
                    Id = m.ModelId,
                    DisplayName = m.DisplayName ?? m.ModelId,
                    Description = m.Description ?? $"Usage: {m.UseCase}",
                    MinPerkLevel = m.MinPerkLevel,
                    IsDefault = m.IsDefault,
                })
                .ToList();
        }
        else
        {
            // Fallback to all registered models when model selection is disabled
            snChanModels = modelRegistry
                .All.Select(m => new BotModelInfo
                {
                    Id = m.Id,
                    DisplayName = m.DisplayName,
                    Description = $"Provider: {m.Provider}",
                    MinPerkLevel = 0,
                    IsDefault = m.Id == "deepseek-chat",
                })
                .ToList();
        }

        bots.Add(
            new()
            {
                Id = "snchan",
                Name = "SN Chan",
                Description =
                    "The helpful assistant who have ability to solve problems for you on the Solar Network.",
                AvailableModels = snChanModels,
            }
        );

        // Mi-chan bot with available models
        var miChanModels = new List<BotModelInfo>();
        if (miChanConfig.UseModelSelection)
        {
            var availableModels = agentClientProvider.GetAvailableModelsForUseCase(
                ModelUseCase.MiChanChat,
                perkLevel
            );
            miChanModels = availableModels
                .Select(m => new BotModelInfo
                {
                    Id = m.ModelId,
                    DisplayName = m.DisplayName ?? m.ModelId,
                    Description = m.Description ?? $"Usage: {m.UseCase}",
                    MinPerkLevel = m.MinPerkLevel,
                    IsDefault = m.IsDefault,
                })
                .ToList();
        }
        else
        {
            // Fallback to all registered models when model selection is disabled
            miChanModels = modelRegistry
                .All.Select(m => new BotModelInfo
                {
                    Id = m.Id,
                    DisplayName = m.DisplayName,
                    Description = $"Provider: {m.Provider}",
                    MinPerkLevel = 0,
                    IsDefault = m.Id == "deepseek-chat",
                })
                .ToList();
        }

        bots.Add(
            new()
            {
                Id = "michan",
                Name = "Mi Chan",
                Description = "A mysterious girl",
                AvailableModels = miChanModels,
            }
        );

        return Ok(new ThoughtServicesResponse { DefaultBot = "snchan", Bots = bots });
    }

    [HttpGet("commands")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<CommandsListResponse> GetAvailableCommands()
    {
        var commands = new List<CommandResponse>
        {
            new()
            {
                Command = "/clear",
                Description =
                    "Clear conversation context, summarize history as memory, start fresh",
            },
            new()
            {
                Command = "/compact",
                Description = "Summarize old messages, keep recent context",
            },
            new() { Command = "/reset", Description = "Alias for /clear" },
        };

        return Ok(new CommandsListResponse { Commands = commands });
    }

    [HttpPost]
    public async Task<ActionResult> Think([FromBody] StreamThinkingRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        if (
            (request.AttachedFiles is null || request.AttachedFiles.Count == 0)
            && string.IsNullOrWhiteSpace(request.UserMessage)
        )
            return BadRequest("You cannot send empty messages.");

        if (request.AcceptProposals.Any(e => !AvailableProposals.Contains(e)))
            return BadRequest("Request contains unavailable proposal");

        // Early validation: Check bot ownership if sequenceId is provided
        if (request.SequenceId.HasValue)
        {
            var sequence = await service.GetSequenceAsync(request.SequenceId.Value);
            if (sequence != null && sequence.AccountId == accountId)
            {
                // If sequence has a BotName, validate it matches the requested bot
                if (
                    !string.IsNullOrEmpty(sequence.BotName)
                    && !sequence.BotName.Equals(request.Bot, StringComparison.OrdinalIgnoreCase)
                )
                {
                    return BadRequest(
                        $"This sequence belongs to '{sequence.BotName}' and cannot be accessed by '{request.Bot}'."
                    );
                }
            }
        }

        return request.Bot.ToLower() switch
        {
            // Route to appropriate bot
            "michan" => await ThinkWithMiChanAsync(request, currentUser, accountId),
            "snchan" => await ThinkWithSnChanAsync(request, currentUser, accountId),
            _ => BadRequest($"Invalid bot. Available bots: {string.Join(", ", AvailableBots)}"),
        };
    }

    [HttpPost("voice")]
    [AskPermission("michan.think")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> UploadVoice([FromForm] SendVoiceThinkingRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var bot = (request.Bot ?? "michan").ToLowerInvariant();
        if (!AvailableBots.Contains(bot))
        {
            return BadRequest($"Invalid bot. Available bots: {string.Join(", ", AvailableBots)}");
        }

        if (request.SequenceId.HasValue)
        {
            var ownershipError = await ValidateSequenceBotOwnershipAsync(
                accountId,
                request.SequenceId,
                bot
            );
            if (ownershipError != null)
            {
                return ownershipError;
            }
        }

        SnThinkingVoiceClip clip;
        try
        {
            clip = await voiceService.SaveVoiceClipAsync(
                accountId,
                request.SequenceId,
                request.File,
                request.DurationMs,
                HttpContext.RequestAborted
            );
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }

        return Ok(
            new
            {
                id = clip.Id,
                mimeType = clip.MimeType,
                durationMs = clip.DurationMs,
                size = clip.Size,
                expiresAt = clip.ExpiresAt,
                url = voiceService.BuildStreamUrl(clip.Id, clip.AccessToken),
            }
        );
    }

    [HttpGet("voice/{voiceId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> GetVoice(Guid voiceId, [FromQuery] string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return Unauthorized();
        }

        var clip = await voiceService.GetVoiceClipAsync(voiceId, HttpContext.RequestAborted);
        if (clip == null)
        {
            return NotFound();
        }

        if (!string.Equals(clip.AccessToken, token, StringComparison.Ordinal))
        {
            return Unauthorized();
        }

        if (clip.ExpiresAt <= NodaTime.SystemClock.Instance.GetCurrentInstant())
        {
            return NotFound();
        }

        var file = await voiceService.OpenVoiceClipAsync(clip, HttpContext.RequestAborted);
        if (file == null)
        {
            return NotFound();
        }

        if (file.ContentLength.HasValue)
        {
            Response.ContentLength = file.ContentLength.Value;
        }

        return File(file.Stream, clip.MimeType);
    }

    /// <summary>
    /// Validates that the sequence belongs to the requesting bot.
    /// Returns null if valid, otherwise returns an ActionResult with the error.
    /// </summary>
    private async Task<ActionResult?> ValidateSequenceBotOwnershipAsync(
        Guid accountId,
        Guid? sequenceId,
        string requestedBot
    )
    {
        if (!sequenceId.HasValue)
            return null;

        var sequence = await service.GetSequenceAsync(sequenceId.Value);
        if (sequence == null || sequence.AccountId != accountId)
            return Forbid();

        // If sequence has no BotName, it's a legacy sequence - allow any bot
        if (string.IsNullOrEmpty(sequence.BotName))
            return null;

        // Check if sequence belongs to a different bot
        if (!sequence.BotName.Equals(requestedBot, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(
                $"This sequence belongs to '{sequence.BotName}' and cannot be accessed by '{requestedBot}'."
            );
        }

        return null;
    }

    private async Task<ActionResult> ThinkWithSnChanAsync(
        StreamThinkingRequest request,
        DyAccount currentUser,
        Guid accountId
    )
    {
        const string targetBot = "snchan";

        var serviceInfo = service.GetSnChanServiceInfo();
        if (serviceInfo is null)
            return BadRequest("Service not found or configured.");

        if (!string.IsNullOrEmpty(request.Model))
        {
            var canUseModel =
                snChanConfig.UseModelSelection && snChanModelSelector != null
                    ? snChanModelSelector.CanAccessModel(
                        ModelUseCase.SnChanChat,
                        request.Model,
                        currentUser.PerkLevel
                    )
                    : true;

            if (!canUseModel)
            {
                return StatusCode(403, $"You don't have access to model '{request.Model}'");
            }
        }

        if (serviceInfo.PerkLevel > 0 && !currentUser.IsSuperuser)
            if (currentUser.PerkLevel < serviceInfo.PerkLevel)
                return StatusCode(403, "Not enough perk level");

        if (request.SequenceId.HasValue)
        {
            var ownershipError = await ValidateSequenceBotOwnershipAsync(
                accountId,
                request.SequenceId,
                targetBot
            );
            if (ownershipError != null)
                return ownershipError;

            if (await service.IsCanonicalMiChanSequenceAsync(accountId, request.SequenceId.Value))
            {
                return BadRequest(
                    "SnChan cannot use MiChan's unified conversation. Start a new SnChan thread instead."
                );
            }
        }

        string? topic = null;
        if (!request.SequenceId.HasValue)
        {
            topic = await service.GenerateTopicAsync(request.UserMessage, useMiChan: false);
            if (topic is null)
            {
                return BadRequest("Default service not found or configured.");
            }
        }

        var sequence = await service.GetOrCreateSequenceAsync(
            accountId,
            request.SequenceId,
            topic,
            "snchan"
        );
        if (sequence == null)
            return Forbid();

        var filesRetrieveRequest = new DyGetFileBatchRequest();
        if (request.AttachedFiles is { Count: > 0 })
            filesRetrieveRequest.Ids.AddRange(request.AttachedFiles);
        var filesData = request.AttachedFiles is { Count: > 0 }
            ? (await files.GetFileBatchAsync(filesRetrieveRequest)).Files.ToList()
            : null;
        var attachedVoices = request.AttachedVoices is { Count: > 0 }
            ? request
                .AttachedVoices.Select(id =>
                    Guid.TryParse(id, out var parsed) ? parsed : Guid.Empty
                )
                .Where(id => id != Guid.Empty)
                .ToList()
            : [];
        var voiceFiles =
            attachedVoices.Count > 0
                ? await voiceService.GetAccessibleVoiceClipsAsync(
                    accountId,
                    attachedVoices,
                    HttpContext.RequestAborted
                )
                : [];

        var userPart = new SnThinkingMessagePart
        {
            Type = ThinkingMessagePartType.Text,
            Metadata = new Dictionary<string, object>(),
            Text = request.UserMessage,
        };
        if (request.AttachedMessages is not null)
            userPart.Metadata.Add("attached_messages", request.AttachedMessages);
        if (request.AttachedPosts is not null)
            userPart.Metadata.Add("attached_posts", request.AttachedPosts);
        var mergedFiles = new List<SnCloudFileReferenceObject>();
        if (filesData is not null)
            mergedFiles.AddRange(filesData.Select(SnCloudFileReferenceObject.FromProtoValue));
        if (voiceFiles.Count > 0)
            mergedFiles.AddRange(voiceFiles.Select(voiceService.ToFileReference));
        if (mergedFiles.Count > 0)
            userPart.Files = mergedFiles;
        var userThought = await service.SaveThoughtAsync(
            sequence,
            [userPart],
            ThinkingThoughtRole.User,
            botName: "snchan"
        );

        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.StatusCode = 200;

        List<SnThinkingMessagePart>? assistantParts = null;
        const int maxStreamingAttempts = 2;
        for (var attempt = 0; attempt < maxStreamingAttempts; attempt++)
        {
            var preparingJson = JsonSerializer.Serialize(
                new { type = "status", data = attempt == 0 ? "preparing_context" : "retrying_with_compaction" }
            );
            await Response.Body.WriteAsync(Encoding.UTF8.GetBytes($"data: {preparingJson}\n\n"));
            await Response.Body.FlushAsync();

            var (conversation, _) = await service.BuildSnChanConversationAsync(
                sequence,
                currentUser,
                request.UserMessage,
                request.AttachedPosts,
                request.AttachedMessages,
                request.AcceptProposals,
                userPart.Files ?? [],
                userThought.Id
            );

            toolRegistry.RegisterMiChanPlugins(serviceProvider);
            conversation = new AgentConversation(conversation.Messages)
            {
                Tools = toolRegistry.GetAllDefinitions().ToList()
            };

            var provider = snChanFoundationProvider.GetChatAdapter(request.Model);
            var options = snChanFoundationProvider.CreateExecutionOptions(
                reasoningEffort: request.ReasoningEffort,
                enableThinking: request.Thinking ?? true
            );

            var attemptAssistantParts = new List<SnThinkingMessagePart>();
            var attemptFullResponse = new StringBuilder();
            var currentToolCalls = new List<(string Id, string Name, string Arguments)>();
            var streamErrorMessage = (string?)null;
            var shouldRetryWithCompaction = false;

            await foreach (
                var evt in streamingService.StreamChatAsync(
                    provider,
                    conversation,
                    options,
                    HttpContext.RequestAborted
                )
            )
            {
                switch (evt)
                {
                    case StreamingChatEvent.Text text:
                        attemptFullResponse.Append(text.Delta);
                        var textJson = JsonSerializer.Serialize(
                            new { type = "text", data = text.Delta }
                        );
                        await Response.Body.WriteAsync(Encoding.UTF8.GetBytes($"data: {textJson}\n\n"));
                        await Response.Body.FlushAsync();
                        break;

                    case StreamingChatEvent.Reasoning reasoning:
                        var reasoningJson = JsonSerializer.Serialize(
                            new { type = "reasoning", data = reasoning.Delta }
                        );
                        await Response.Body.WriteAsync(
                            Encoding.UTF8.GetBytes($"data: {reasoningJson}\n\n")
                        );
                        await Response.Body.FlushAsync();
                        break;

                    case StreamingChatEvent.ToolCallStarted toolStarted:
                        currentToolCalls.Add((toolStarted.Id, toolStarted.Name, ""));
                        var startedJson = JsonSerializer.Serialize(
                            new
                            {
                                type = "tool_call_started",
                                id = toolStarted.Id,
                                name = toolStarted.Name,
                            }
                        );
                        await Response.Body.WriteAsync(
                            Encoding.UTF8.GetBytes($"data: {startedJson}\n\n")
                        );
                        await Response.Body.FlushAsync();
                        break;

                    case StreamingChatEvent.ToolCallDelta toolDelta:
                        var call = currentToolCalls.FirstOrDefault(c => c.Id == toolDelta.Id);
                        if (call != default)
                        {
                            var idx = currentToolCalls.IndexOf(call);
                            currentToolCalls[idx] = (
                                call.Id,
                                call.Name,
                                call.Arguments + (toolDelta.ArgumentsDelta ?? "")
                            );
                        }
                        break;

                    case StreamingChatEvent.ToolResult toolResult:
                        var functionCallPart = new SnThinkingMessagePart
                        {
                            Type = ThinkingMessagePartType.FunctionCall,
                            FunctionCall = new SnFunctionCall
                            {
                                Id = toolResult.Id,
                                Name = toolResult.Name,
                                Arguments =
                                    currentToolCalls
                                        .FirstOrDefault(c => c.Id == toolResult.Id)
                                        .Arguments ?? "",
                            },
                        };
                        attemptAssistantParts.Add(functionCallPart);

                        var callJson = JsonSerializer.Serialize(
                            new { type = "function_call", data = functionCallPart.FunctionCall }
                        );
                        await Response.Body.WriteAsync(Encoding.UTF8.GetBytes($"data: {callJson}\n\n"));
                        await Response.Body.FlushAsync();

                        var resultPart = new SnThinkingMessagePart
                        {
                            Type = ThinkingMessagePartType.FunctionResult,
                            FunctionResult = new SnFunctionResult
                            {
                                CallId = toolResult.Id,
                                FunctionName = toolResult.Name,
                                Result = toolResult.Result,
                                IsError = toolResult.IsError,
                            },
                        };
                        attemptAssistantParts.Add(resultPart);

                        var resultJson = JsonSerializer.Serialize(
                            new { type = "function_result", data = resultPart.FunctionResult }
                        );
                        await Response.Body.WriteAsync(
                            Encoding.UTF8.GetBytes($"data: {resultJson}\n\n")
                        );
                        await Response.Body.FlushAsync();
                        break;

                    case StreamingChatEvent.Finished finished:
                        if (!string.IsNullOrEmpty(finished.FinalText))
                        {
                            attemptAssistantParts.Add(
                                new SnThinkingMessagePart
                                {
                                    Type = ThinkingMessagePartType.Text,
                                    Text = finished.FinalText,
                                }
                            );
                        }
                        if (!string.IsNullOrEmpty(finished.FinalReasoning))
                        {
                            attemptAssistantParts.Add(
                                new SnThinkingMessagePart
                                {
                                    Type = ThinkingMessagePartType.Reasoning,
                                    Reasoning = finished.FinalReasoning,
                                }
                            );
                        }
                        break;

                    case StreamingChatEvent.Error error:
                        streamErrorMessage = error.Message;
                        if (attempt == 0 && IsContextTooLargeError(error.Message))
                        {
                            shouldRetryWithCompaction = true;
                            logger.LogWarning(
                                "SnChan streaming hit context limit for user {AccountId}, sequence {SequenceId}. Triggering one-time compaction retry. error={Error}",
                                accountId,
                                sequence.Id,
                                error.Message
                            );
                        }
                        else
                        {
                            var errorJson = JsonSerializer.Serialize(
                                new { type = "error", data = error.Message }
                            );
                            await Response.Body.WriteAsync(
                                Encoding.UTF8.GetBytes($"data: {errorJson}\n\n")
                            );
                            await Response.Body.FlushAsync();
                        }

                        break;
                }
            }

            if (shouldRetryWithCompaction)
            {
                var compactingJson = JsonSerializer.Serialize(
                    new { type = "status", data = "compacting" }
                );
                await Response.Body.WriteAsync(Encoding.UTF8.GetBytes($"data: {compactingJson}\n\n"));
                await Response.Body.FlushAsync();

                try
                {
                    var compactResult = await service.CompactHistoryAsync(sequence.Id, accountId);
                    logger.LogInformation(
                        "Auto-compacted SnChan after context-limit error for user {AccountId}, sequence {SequenceId}, archivedCount={ArchivedCount}",
                        accountId,
                        sequence.Id,
                        compactResult.ArchivedCount
                    );

                    var compactedJson = JsonSerializer.Serialize(
                        new
                        {
                            type = "auto_compacted",
                            summary = compactResult.Summary,
                            archived_count = compactResult.ArchivedCount,
                        }
                    );
                    await Response.Body.WriteAsync(
                        Encoding.UTF8.GetBytes($"data: {compactedJson}\n\n")
                    );
                    await Response.Body.FlushAsync();
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Auto-compact on SnChan context-limit retry failed for user {AccountId}, sequence {SequenceId}",
                        accountId,
                        sequence.Id
                    );
                    var errorJson = JsonSerializer.Serialize(
                        new { type = "error", data = "对话整理失败，请稍后重试" }
                    );
                    await Response.Body.WriteAsync(Encoding.UTF8.GetBytes($"data: {errorJson}\n\n"));
                    await Response.Body.FlushAsync();
                    return new EmptyResult();
                }

                continue;
            }

            if (!string.IsNullOrWhiteSpace(streamErrorMessage))
            {
                return new EmptyResult();
            }

            if (attemptAssistantParts.Count == 0 && attemptFullResponse.Length > 0)
            {
                attemptAssistantParts.Add(
                    new SnThinkingMessagePart
                    {
                        Type = ThinkingMessagePartType.Text,
                        Text = attemptFullResponse.ToString(),
                    }
                );
            }

            assistantParts = attemptAssistantParts;
            break;
        }

        if (assistantParts == null)
        {
            var errorJson = JsonSerializer.Serialize(
                new { type = "error", data = "对话生成失败，请稍后重试" }
            );
            await Response.Body.WriteAsync(Encoding.UTF8.GetBytes($"data: {errorJson}\n\n"));
            await Response.Body.FlushAsync();
            return new EmptyResult();
        }

        var savedThought = await service.SaveThoughtAsync(
            sequence,
            assistantParts,
            ThinkingThoughtRole.Assistant,
            miChanConfig.ThinkingModel.ModelId,
            botName: "snchan"
        );

        using (var streamBuilder = new MemoryStream())
        {
            await streamBuilder.WriteAsync("\n\n"u8.ToArray());
            if (topic != null)
            {
                var topicJson = JsonSerializer.Serialize(
                    new { type = "topic", data = sequence.Topic ?? "" }
                );
                await streamBuilder.WriteAsync(Encoding.UTF8.GetBytes($"topic: {topicJson}\n\n"));
            }

            var thoughtJson = JsonSerializer.Serialize(
                new { type = "thought", data = savedThought },
                InfraObjectCoder.SerializerOptionsWithoutIgnore
            );
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
        const string targetBot = "michan";

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

        // Check if user can access the requested custom model
        if (!string.IsNullOrEmpty(request.Model))
        {
            var canUseModel = miChanConfig.UseModelSelection
                ? agentClientProvider
                    .GetAvailableModelsForUseCase(ModelUseCase.MiChanChat, currentUser.PerkLevel)
                    .Any(m => m.ModelId == request.Model)
                : true; // If model selection is disabled, allow any model

            if (!canUseModel)
            {
                return StatusCode(403, $"You don't have access to model '{request.Model}'");
            }
        }

        if (serviceInfo.PerkLevel > 0 && !currentUser.IsSuperuser)
            if (currentUser.PerkLevel < serviceInfo.PerkLevel)
                return StatusCode(403, "Not enough perk level");

        // Validate bot ownership of the sequence
        if (request.SequenceId.HasValue)
        {
            var ownershipError = await ValidateSequenceBotOwnershipAsync(
                accountId,
                request.SequenceId,
                targetBot
            );
            if (ownershipError != null)
                return ownershipError;
        }

        if (!string.IsNullOrWhiteSpace(request.UserMessage))
        {
            var message = request.UserMessage.Trim();
            if (message.StartsWith("/clear") || message.StartsWith("/reset"))
            {
                return await HandleClearCommandAsync(request, currentUser, accountId);
            }
            if (message.StartsWith("/compact"))
            {
                return await HandleCompactCommandAsync(request, currentUser, accountId);
            }
        }

        string? topic = request.Topic;
        if (string.IsNullOrWhiteSpace(topic) && !string.IsNullOrWhiteSpace(request.UserMessage))
        {
            topic = await service.GenerateTopicAsync(request.UserMessage, useMiChan: true);
        }

        // Auto-create new thread when:
        // 1. No sequenceId is provided AND
        // 2. A topic is provided (either from request or generated)
        bool shouldCreateNewThread =
            !request.SequenceId.HasValue && !string.IsNullOrWhiteSpace(topic);

        var resolution = await service.ResolveMiChanSequenceAsync(
            accountId,
            request.SequenceId,
            topic,
            shouldCreateNewThread,
            "michan"
        );
        if (resolution.ErrorMessage != null)
        {
            return BadRequest(resolution.ErrorMessage);
        }

        var sequence = resolution.Sequence;
        if (sequence == null)
            return Forbid();
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
        var attachedVoices = request.AttachedVoices is { Count: > 0 }
            ? request
                .AttachedVoices.Select(id =>
                    Guid.TryParse(id, out var parsed) ? parsed : Guid.Empty
                )
                .Where(id => id != Guid.Empty)
                .ToList()
            : [];
        var voiceFiles =
            attachedVoices.Count > 0
                ? await voiceService.GetAccessibleVoiceClipsAsync(
                    accountId,
                    attachedVoices,
                    HttpContext.RequestAborted
                )
                : [];
        logger.LogDebug(
            "MiChan request fetched {FilesCount} attached files and {VoicesCount} attached voices for sequence {SequenceId} in {ElapsedMs}ms",
            filesData?.Count ?? 0,
            voiceFiles.Count,
            sequence.Id,
            overallStopwatch.ElapsedMilliseconds
        );

        var userPart = new SnThinkingMessagePart
        {
            Type = ThinkingMessagePartType.Text,
            Metadata = new Dictionary<string, object>(),
            Text = request.UserMessage,
        };
        if (request.AttachedMessages is not null)
            userPart.Metadata.Add("attached_messages", request.AttachedMessages);
        if (request.AttachedPosts is not null)
            userPart.Metadata.Add("attached_posts", request.AttachedPosts);
        var mergedFiles = new List<SnCloudFileReferenceObject>();
        if (filesData is not null)
            mergedFiles.AddRange(filesData.Select(SnCloudFileReferenceObject.FromProtoValue));
        if (voiceFiles.Count > 0)
            mergedFiles.AddRange(voiceFiles.Select(voiceService.ToFileReference));
        if (mergedFiles.Count > 0)
            userPart.Files = mergedFiles;
        var userThought = await service.SaveThoughtAsync(
            sequence,
            [userPart],
            ThinkingThoughtRole.User,
            botName: "michan"
        );

        try
        {
            await service.TouchMiChanUserProfileAsync(accountId, "michan");
            await service.RecordMiChanMoodEventAsync(
                "user_interaction",
                HttpContext.RequestAborted
            );
            if (userPart.Files is { Count: > 0 })
            {
                await service.RecordMiChanMoodEventAsync(
                    "shared_media",
                    HttpContext.RequestAborted
                );
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to record mood events for user turn. accountId={AccountId}, sequenceId={SequenceId}",
                accountId,
                sequence.Id
            );
        }

        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.StatusCode = 200;

        logger.LogInformation(
            "MiChan SSE stream opened for user {AccountId}, sequence {SequenceId} at {ElapsedMs}ms",
            accountId,
            sequence.Id,
            overallStopwatch.ElapsedMilliseconds
        );

        List<SnThinkingMessagePart>? assistantParts = null;
        var finalResponseLength = 0;
        var completedSuccessfully = false;
        var finalModelName = miChanConfig.ThinkingModel.ModelId;

        const int maxStreamingAttempts = 2;
        for (var attempt = 0; attempt < maxStreamingAttempts; attempt++)
        {
            var preparingJson = JsonSerializer.Serialize(
                new { type = "status", data = attempt == 0 ? "preparing_context" : "retrying_with_compaction" }
            );
            await Response.Body.WriteAsync(Encoding.UTF8.GetBytes($"data: {preparingJson}\n\n"));
            await Response.Body.FlushAsync();

            var historyStopwatch = Stopwatch.StartNew();
            var (conversation, useVisionKernel) = await service.BuildMiChanConversationAsync(
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
                "MiChan context prepared for user {AccountId}, sequence {SequenceId} in {ElapsedMs}ms. useVisionKernel={UseVisionKernel}, messageCount={MessageCount}, attempt={Attempt}",
                accountId,
                sequence.Id,
                historyStopwatch.ElapsedMilliseconds,
                useVisionKernel,
                conversation.Messages.Count,
                attempt + 1
            );

            toolRegistry.RegisterMiChanPlugins(serviceProvider);
            conversation = new AgentConversation(conversation.Messages)
            {
                Tools = toolRegistry.GetAllDefinitions().ToList()
            };

            var provider = useVisionKernel
                ? miChanFoundationProvider.GetVisionAdapter(currentUser.PerkLevel)
                : miChanFoundationProvider.GetChatAdapter(currentUser.PerkLevel, request.Model);
            var enableThinking = request.Thinking ?? true;
            var options = useVisionKernel
                ? miChanFoundationProvider.CreateVisionExecutionOptions(
                    reasoningEffort: request.ReasoningEffort,
                    enableThinking: enableThinking
                )
                : miChanFoundationProvider.CreateExecutionOptions(
                    reasoningEffort: request.ReasoningEffort,
                    enableThinking: enableThinking
                );
            var modelNameForAttempt = useVisionKernel
                ? miChanConfig.Vision.VisionThinkingService
                : request.Model ?? miChanConfig.ThinkingModel.ModelId;
            logger.LogInformation(
                "MiChan selected provider {ProviderId} with model label {ModelName} for sequence {SequenceId}, attempt={Attempt}",
                provider.ProviderId,
                modelNameForAttempt,
                sequence.Id,
                attempt + 1
            );

            var attemptAssistantParts = new List<SnThinkingMessagePart>();
            var attemptFullResponse = new StringBuilder();
            var currentToolCalls = new List<(string Id, string Name, string Arguments)>();
            var streamErrorMessage = (string?)null;
            var shouldRetryWithCompaction = false;

            await foreach (
                var evt in streamingService.StreamChatAsync(
                    provider,
                    conversation,
                    options,
                    HttpContext.RequestAborted
                )
            )
            {
                switch (evt)
                {
                    case StreamingChatEvent.Text text:
                        attemptFullResponse.Append(text.Delta);
                        var textJson = JsonSerializer.Serialize(
                            new { type = "text", data = text.Delta }
                        );
                        await Response.Body.WriteAsync(Encoding.UTF8.GetBytes($"data: {textJson}\n\n"));
                        await Response.Body.FlushAsync();
                        break;

                    case StreamingChatEvent.Reasoning reasoning:
                        var reasoningJson = JsonSerializer.Serialize(
                            new { type = "reasoning", data = reasoning.Delta }
                        );
                        await Response.Body.WriteAsync(
                            Encoding.UTF8.GetBytes($"data: {reasoningJson}\n\n")
                        );
                        await Response.Body.FlushAsync();
                        break;

                    case StreamingChatEvent.ToolCallStarted toolStarted:
                        currentToolCalls.Add((toolStarted.Id, toolStarted.Name, ""));
                        break;

                    case StreamingChatEvent.ToolCallDelta toolDelta:
                        var call = currentToolCalls.FirstOrDefault(c => c.Id == toolDelta.Id);
                        if (call != default)
                        {
                            var idx = currentToolCalls.IndexOf(call);
                            currentToolCalls[idx] = (
                                call.Id,
                                call.Name,
                                call.Arguments + (toolDelta.ArgumentsDelta ?? "")
                            );
                        }
                        break;

                    case StreamingChatEvent.ToolResult toolResult:
                        var functionCallPart = new SnThinkingMessagePart
                        {
                            Type = ThinkingMessagePartType.FunctionCall,
                            FunctionCall = new SnFunctionCall
                            {
                                Id = toolResult.Id,
                                Name = toolResult.Name,
                                Arguments =
                                    currentToolCalls
                                        .FirstOrDefault(c => c.Id == toolResult.Id)
                                        .Arguments ?? "",
                            },
                        };
                        attemptAssistantParts.Add(functionCallPart);

                        var callJson = JsonSerializer.Serialize(
                            new { type = "function_call", data = functionCallPart.FunctionCall }
                        );
                        await Response.Body.WriteAsync(Encoding.UTF8.GetBytes($"data: {callJson}\n\n"));
                        await Response.Body.FlushAsync();

                        var resultPart = new SnThinkingMessagePart
                        {
                            Type = ThinkingMessagePartType.FunctionResult,
                            FunctionResult = new SnFunctionResult
                            {
                                CallId = toolResult.Id,
                                FunctionName = toolResult.Name,
                                Result = toolResult.Result,
                                IsError = toolResult.IsError,
                            },
                        };
                        attemptAssistantParts.Add(resultPart);

                        var resultJson = JsonSerializer.Serialize(
                            new { type = "function_result", data = resultPart.FunctionResult }
                        );
                        await Response.Body.WriteAsync(
                            Encoding.UTF8.GetBytes($"data: {resultJson}\n\n")
                        );
                        await Response.Body.FlushAsync();
                        break;

                    case StreamingChatEvent.Finished finished:
                        if (!string.IsNullOrEmpty(finished.FinalText))
                        {
                            attemptAssistantParts.Add(
                                new SnThinkingMessagePart
                                {
                                    Type = ThinkingMessagePartType.Text,
                                    Text = finished.FinalText,
                                }
                            );
                        }
                        if (!string.IsNullOrEmpty(finished.FinalReasoning))
                        {
                            attemptAssistantParts.Add(
                                new SnThinkingMessagePart
                                {
                                    Type = ThinkingMessagePartType.Reasoning,
                                    Reasoning = finished.FinalReasoning,
                                }
                            );
                        }
                        break;

                    case StreamingChatEvent.Error error:
                        streamErrorMessage = error.Message;
                        if (attempt == 0 && IsContextTooLargeError(error.Message))
                        {
                            shouldRetryWithCompaction = true;
                            logger.LogWarning(
                                "MiChan streaming hit context limit for user {AccountId}, sequence {SequenceId}. Triggering one-time compaction retry. error={Error}",
                                accountId,
                                sequence.Id,
                                error.Message
                            );
                        }
                        else
                        {
                            var errorJson = JsonSerializer.Serialize(
                                new { type = "error", data = error.Message }
                            );
                            await Response.Body.WriteAsync(
                                Encoding.UTF8.GetBytes($"data: {errorJson}\n\n")
                            );
                            await Response.Body.FlushAsync();
                        }

                        break;
                }
            }

            if (shouldRetryWithCompaction)
            {
                var compactingJson = JsonSerializer.Serialize(
                    new { type = "status", data = "compacting" }
                );
                await Response.Body.WriteAsync(Encoding.UTF8.GetBytes($"data: {compactingJson}\n\n"));
                await Response.Body.FlushAsync();

                try
                {
                    var autoCompactStopwatch = Stopwatch.StartNew();
                    var compactResult = await service.CompactHistoryAsync(sequence.Id, accountId);
                    logger.LogInformation(
                        "Auto-compacted after context-limit error for user {AccountId}, sequence {SequenceId} in {ElapsedMs}ms, archivedCount={ArchivedCount}",
                        accountId,
                        sequence.Id,
                        autoCompactStopwatch.ElapsedMilliseconds,
                        compactResult.ArchivedCount
                    );

                    var compactedJson = JsonSerializer.Serialize(
                        new
                        {
                            type = "auto_compacted",
                            summary = compactResult.Summary,
                            archived_count = compactResult.ArchivedCount,
                        }
                    );
                    await Response.Body.WriteAsync(
                        Encoding.UTF8.GetBytes($"data: {compactedJson}\n\n")
                    );
                    await Response.Body.FlushAsync();
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Auto-compact on context-limit retry failed for user {AccountId}, sequence {SequenceId}",
                        accountId,
                        sequence.Id
                    );
                    var errorJson = JsonSerializer.Serialize(
                        new { type = "error", data = "对话整理失败，请稍后重试" }
                    );
                    await Response.Body.WriteAsync(Encoding.UTF8.GetBytes($"data: {errorJson}\n\n"));
                    await Response.Body.FlushAsync();
                    return new EmptyResult();
                }

                continue;
            }

            if (!string.IsNullOrWhiteSpace(streamErrorMessage))
            {
                return new EmptyResult();
            }

            if (attemptAssistantParts.Count == 0 && attemptFullResponse.Length > 0)
            {
                attemptAssistantParts.Add(
                    new SnThinkingMessagePart
                    {
                        Type = ThinkingMessagePartType.Text,
                        Text = attemptFullResponse.ToString(),
                    }
                );
            }

            if (attemptAssistantParts.Count == 0)
            {
                logger.LogWarning(
                    "MiChan returned an empty response for user {AccountId}, sequence {SequenceId}, provider {ProviderId}, model {ModelName}",
                    accountId,
                    sequence.Id,
                    provider.ProviderId,
                    modelNameForAttempt
                );
                var errorJson = JsonSerializer.Serialize(
                    new { type = "error", data = "模型返回了空响应，请稍后重试" }
                );
                await Response.Body.WriteAsync(Encoding.UTF8.GetBytes($"data: {errorJson}\n\n"));
                await Response.Body.FlushAsync();
                return new EmptyResult();
            }

            assistantParts = attemptAssistantParts;
            finalResponseLength = attemptFullResponse.Length;
            finalModelName = modelNameForAttempt;
            completedSuccessfully = true;
            break;
        }

        if (!completedSuccessfully || assistantParts == null)
        {
            var errorJson = JsonSerializer.Serialize(
                new { type = "error", data = "对话生成失败，请稍后重试" }
            );
            await Response.Body.WriteAsync(Encoding.UTF8.GetBytes($"data: {errorJson}\n\n"));
            await Response.Body.FlushAsync();
            return new EmptyResult();
        }

        var savedThought = await service.SaveThoughtAsync(
            sequence,
            assistantParts,
            ThinkingThoughtRole.Assistant,
            finalModelName,
            botName: "michan"
        );

        try
        {
            await service.RecordMiChanMoodEventAsync(
                "assistant_response",
                HttpContext.RequestAborted
            );
            await service.TryUpdateMiChanMoodAsync(HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to update mood after assistant turn. accountId={AccountId}, sequenceId={SequenceId}",
                accountId,
                sequence.Id
            );
        }
        logger.LogInformation(
            "MiChan completed thought request for user {AccountId}, sequence {SequenceId} in {ElapsedMs}ms. assistantParts={AssistantPartsCount}, responseChars={ResponseLength}",
            accountId,
            sequence.Id,
            overallStopwatch.ElapsedMilliseconds,
            assistantParts.Count,
            finalResponseLength
        );

        using (var streamBuilder = new MemoryStream())
        {
            await streamBuilder.WriteAsync("\n\n"u8.ToArray());
            if (topic != null)
            {
                var topicJson = JsonSerializer.Serialize(
                    new { type = "topic", data = sequence.Topic ?? "" }
                );
                await streamBuilder.WriteAsync(Encoding.UTF8.GetBytes($"topic: {topicJson}\n\n"));
            }

            var thoughtJson = JsonSerializer.Serialize(
                new { type = "thought", data = savedThought },
                InfraObjectCoder.SerializerOptionsWithoutIgnore
            );
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
        [FromQuery] int take = 20,
        [FromQuery] string? botName = null
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var (totalCount, sequences) = await service.ListSequencesAsync(
            accountId,
            offset,
            take,
            botName
        );

        Response.Headers["X-Total"] = totalCount.ToString();

        return Ok(sequences);
    }

    [HttpGet("michan/sequence")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SnThinkingSequence>> GetMiChanUnifiedSequence()
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        // Get the last active MiChan sequence (canonical or most recent)
        var sequence = await service.GetLastActiveSequenceAsync(accountId, "michan");
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
    public async Task<ActionResult> UpdateSequenceSharing(
        Guid sequenceId,
        [FromBody] UpdateSharingRequest request
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var sequence = await service.GetSequenceAsync(sequenceId);
        if (sequence == null)
            return NotFound();
        if (sequence.AccountId != accountId)
            return Forbid();

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
        [FromQuery] int take = 50
    )
    {
        if (offset < 0)
            return BadRequest("offset must be greater than or equal to 0.");
        if (take <= 0)
            return BadRequest("take must be greater than 0.");
        take = Math.Min(take, 200);

        var currentUser = HttpContext.Items["CurrentUser"] as DyAccount;
        var currentAccountId = currentUser != null ? Guid.Parse(currentUser.Id) : (Guid?)null;

        var sequence = await service.GetSequenceAsync(sequenceId);
        if (sequence == null && currentAccountId.HasValue)
        {
            sequence = await service.ResolveSequenceForOwnerAsync(
                currentAccountId.Value,
                sequenceId
            );
        }

        if (sequence == null)
            return NotFound();

        if (!sequence.IsPublic)
        {
            if (currentUser == null)
                return Unauthorized();
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
        if (sequence == null)
            return NotFound();

        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();
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
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var sequence = await service.GetSequenceAsync(sequenceId);
        if (sequence == null)
            return NotFound();

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
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var sequence = await service.GetSequenceAsync(sequenceId);
        if (sequence == null)
            return NotFound();

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

        return Ok(
            new
            {
                success,
                summary,
                sequenceId,
            }
        );
    }

    [HttpGet("memory/search")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<SequenceMemorySearchResponse>> SearchSequenceMemory(
        [FromQuery] string q,
        [FromQuery] int limit = 10,
        [FromQuery] double minSimilarity = 0.6,
        [FromQuery] Guid? accountId = null
    )
    {
        var currentUser = HttpContext.Items["CurrentUser"] as DyAccount;
        if (currentUser == null)
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(q))
        {
            return BadRequest(new { error = "Query cannot be empty." });
        }

        limit = Math.Clamp(limit, 1, 50);
        minSimilarity = Math.Clamp(minSimilarity, 0.0, 1.0);

        var currentAccountId = Guid.Parse(currentUser.Id);
        var effectiveAccountId =
            accountId.HasValue && accountId.Value == currentAccountId
                ? accountId
                : currentAccountId;

        var results = await service.SearchSequenceMemoryAsync(
            q,
            effectiveAccountId,
            limit,
            minSimilarity
        );
        return Ok(new SequenceMemorySearchResponse { Total = results.Count, Results = results });
    }

    /// <summary>
    /// Manually trigger conversation memory maintenance (backfill part rows and refresh summaries).
    /// </summary>
    [HttpPost("admin/memory/maintenance")]
    [AskPermission("michan.admin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<MemoryMaintenanceResponse>> RunMemoryMaintenance(
        [FromQuery] int backfillBatch = 300,
        [FromQuery] int summaryBatch = 16,
        [FromQuery] int maxRounds = 5
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount)
        {
            return Unauthorized();
        }

        backfillBatch = Math.Clamp(backfillBatch, 50, 5000);
        summaryBatch = Math.Clamp(summaryBatch, 5, 200);
        maxRounds = Math.Clamp(maxRounds, 1, 30);

        var roundsExecuted = 0;
        var totalBackfilledRows = 0;
        var totalSummarizedSequences = 0;
        var hasMoreWork = false;

        for (var round = 0; round < maxRounds; round++)
        {
            var backfilled = await service.BackfillThoughtPartRowsAsync(
                backfillBatch,
                HttpContext.RequestAborted
            );
            var summarized = await service.RefreshHistoricSequenceSummariesAsync(
                summaryBatch,
                HttpContext.RequestAborted
            );

            roundsExecuted++;
            totalBackfilledRows += backfilled;
            totalSummarizedSequences += summarized;

            if (backfilled == 0 && summarized == 0)
            {
                hasMoreWork = false;
                break;
            }

            hasMoreWork = round == maxRounds - 1;
        }

        return Ok(
            new MemoryMaintenanceResponse
            {
                Success = true,
                RoundsExecuted = roundsExecuted,
                BackfilledRows = totalBackfilledRows,
                SummarizedSequences = totalSummarizedSequences,
                HasMoreWork = hasMoreWork,
            }
        );
    }

    /// <summary>
    /// Get current user's free token quota status
    /// </summary>
    [HttpGet("quota")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> GetQuotaStatus()
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        if (!freeQuotaService.IsEnabled)
        {
            return Ok(new { enabled = false, message = "Free quota is not enabled" });
        }

        var (freeRemaining, freeUsed) = await freeQuotaService.GetFreeQuotaStatusAsync(accountId);

        return Ok(
            new
            {
                enabled = true,
                tokensPerDay = freeQuotaService.TokensPerDay,
                resetPeriodHours = freeQuotaService.ResetPeriodHours,
                freeRemaining,
                freeUsed,
                freeTotal = freeQuotaService.TokensPerDay,
            }
        );
    }

    /// <summary>
    /// Reset free quota for current user (admin only)
    /// </summary>
    [HttpPost("quota/reset")]
    [AskPermission("michan.admin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> ResetQuota()
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();
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

    private async Task<ActionResult> HandleClearCommandAsync(
        StreamThinkingRequest request,
        DyAccount currentUser,
        Guid accountId
    )
    {
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.StatusCode = 200;

        try
        {
            var sequenceId = request.SequenceId;
            var result = await service.ClearConversationAsync(accountId, sequenceId);

            if (result.NewSequenceId == Guid.Empty)
            {
                return BadRequest(new { error = "无法清理对话" });
            }

            var resultJson = JsonSerializer.Serialize(
                new
                {
                    type = "context_cleared",
                    newSequenceId = result.NewSequenceId,
                    summary = result.Summary,
                    archived_count = result.ArchivedCount,
                }
            );
            await Response.Body.WriteAsync(Encoding.UTF8.GetBytes($"data: {resultJson}\n\n"));
            await Response.Body.FlushAsync();

            var doneJson = JsonSerializer.Serialize(new { type = "DONE" });
            await Response.Body.WriteAsync(Encoding.UTF8.GetBytes($"data: {doneJson}\n\n"));
            await Response.Body.FlushAsync();

            return new EmptyResult();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to handle /clear command for user {AccountId}", accountId);
            return BadRequest(new { error = "清理对话失败，请稍后重试" });
        }
    }

    private async Task<ActionResult> HandleCompactCommandAsync(
        StreamThinkingRequest request,
        DyAccount currentUser,
        Guid accountId
    )
    {
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.StatusCode = 200;

        try
        {
            var sequence = await service.GetCanonicalMiChanSequenceAsync(accountId);
            if (sequence == null)
            {
                return BadRequest(new { error = "没有找到对话" });
            }

            var result = await service.CompactHistoryAsync(sequence.Id, accountId);

            if (
                string.IsNullOrWhiteSpace(result.Summary)
                || result.Summary == "对话历史太短，无需整理"
            )
            {
                return BadRequest(new { error = result.Summary });
            }

            var resultJson = JsonSerializer.Serialize(
                new
                {
                    type = "compacted",
                    summary = result.Summary,
                    archived_count = result.ArchivedCount,
                }
            );
            await Response.Body.WriteAsync(Encoding.UTF8.GetBytes($"data: {resultJson}\n\n"));
            await Response.Body.FlushAsync();

            var doneJson = JsonSerializer.Serialize(new { type = "DONE" });
            await Response.Body.WriteAsync(Encoding.UTF8.GetBytes($"data: {doneJson}\n\n"));
            await Response.Body.FlushAsync();

            return new EmptyResult();
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to handle /compact command for user {AccountId}",
                accountId
            );
            return BadRequest(new { error = "整理对话失败，请稍后重试" });
        }
    }

    private static bool IsContextTooLargeError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var text = message.ToLowerInvariant();
        return text.Contains("context") &&
               (
                   text.Contains("too large") ||
                   text.Contains("length") ||
                   text.Contains("window") ||
                   text.Contains("maximum") ||
                   text.Contains("limit") ||
                   text.Contains("exceed")
               );
    }
}

#pragma warning disable SKEXP0050
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
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
    ThoughtProvider provider,
    ThoughtService service,
    IConfiguration configuration,
    ILogger<ThoughtController> logger,
    MiChanConfig miChanConfig,
    MiChanKernelProvider miChanKernelProvider,
    MiChanMemoryService miChanMemoryService,
    SolarNetworkApiClient apiClient,
    IServiceProvider serviceProvider) : ControllerBase
{
    public static readonly List<string> AvailableProposals = ["post_create"];
    public static readonly List<string> AvailableBots = ["snchan", "michan"];

    public class StreamThinkingRequest
    {
        [Required] 
        public string UserMessage { get; set; } = null!;
        
        [Required]
        public string Bot { get; set; } = "snchan"; // "snchan" or "michan"
        
        public string? ServiceId { get; set; }
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

    private async Task<ActionResult> ThinkWithSnChanAsync(StreamThinkingRequest request, Account currentUser, Guid accountId)
    {
        var serviceId = provider.GetServiceId(request.ServiceId);
        var serviceInfo = provider.GetServiceInfo(serviceId);
        if (serviceInfo is null)
        {
            return BadRequest("Service not found or configured.");
        }

        if (serviceInfo.PerkLevel > 0 && !currentUser.IsSuperuser)
            if (currentUser.PerkSubscription is null ||
                PerkSubscriptionPrivilege.GetPrivilegeFromIdentifier(currentUser.PerkSubscription.Identifier) <
                serviceInfo.PerkLevel)
                return StatusCode(403, "Not enough perk level");

        var kernel = provider.GetKernel(request.ServiceId);
        if (kernel is null)
        {
            return BadRequest("Service not found or configured.");
        }

        // Generate a topic if creating a new sequence
        string? topic = null;
        if (!request.SequenceId.HasValue)
        {
            var summaryHistory = new ChatHistory(
                "You are a helpful assistant. Summarize the following user message into a concise topic title (max 100 characters).\n" +
                "Direct give the topic you summerized, do not add extra prefix / suffix."
            );
            summaryHistory.AddUserMessage(request.UserMessage);

            var summaryKernel = provider.GetKernel();
            if (summaryKernel is null)
            {
                return BadRequest("Default service not found or configured.");
            }

            var summaryResult = await summaryKernel
                .GetRequiredService<IChatCompletionService>()
                .GetChatMessageContentAsync(summaryHistory);

            topic = summaryResult.Content?[..Math.Min(summaryResult.Content.Length, 4096)];
        }

        // Handle sequence
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

        // Build chat history with file-based system prompt support
        var defaultSystemPrompt = 
            "You're a helpful assistant on the Solar Network, a social network.\n" +
            "Your name is Sn-chan (or SN 酱 in chinese), a cute sweet heart with passion for almost everything.\n" +
            "When you talk to user, you can add some modal particles and emoticons to your response to be cute, but prevent use a lot of emojis." +
            "Your creator is @littlesheep, which is also the creator of the Solar Network, if you met some problems you was unable to solve, trying guide the user to ask (DM) the @littlesheep.\n" +
            "\n" +
            "The ID on the Solar Network is UUID, so mostly hard to read, so do not show ID to user unless user ask to do so or necessary.\n" +
            "\n" +
            "Your aim is to helping solving questions for the users on the Solar Network.\n" +
            "And the Solar Network is the social network platform you live on.\n" +
            "When the user asks questions about the Solar Network (also known as SN and Solian), try use the tools you have to get latest and accurate data.";
        
        var systemPromptFile = configuration.GetValue<string>("Thinking:SystemPromptFile");
        var systemPrompt = SystemPromptLoader.LoadSystemPrompt(systemPromptFile, defaultSystemPrompt, logger);

        var chatHistory = new ChatHistory(systemPrompt);

        chatHistory.AddSystemMessage(
            "You can issue some proposals to user, like creating a post. The proposal syntax is like a xml tag, with an attribute indicates which proposal.\n" +
            "Depends on the proposal type, the payload (content inside the xml tag) might be different.\n" +
            "\n" +
            "Example: <proposal type=\"post_create\">...post content...</proposal>\n" +
            "\n" +
            "Here are some references of the proposals you can issue, but if you want to issue one, make sure the user is accept it.\n" +
            "1. post_create: body takes simple string, create post for user." +
            "\n" +
            $"The user currently accept these proposals: {string.Join(',', request.AcceptProposals)}"
        );

        chatHistory.AddSystemMessage(
            $"The user you're currently talking to is {currentUser.Nick} ({currentUser.Name}), ID is {currentUser.Id}"
        );

        if (request.AttachedPosts is { Count: > 0 })
        {
            chatHistory.AddUserMessage(
                $"Attached post IDs: {string.Join(',', request.AttachedPosts!)}");
        }

        if (request.AttachedMessages is { Count: > 0 })
        {
            chatHistory.AddUserMessage(
                $"Attached chat messages data: {JsonSerializer.Serialize(request.AttachedMessages)}");
        }

        // Add previous thoughts
        var previousThoughts = await service.GetPreviousThoughtsAsync(sequence);
        var count = previousThoughts.Count;
        for (var i = count - 1; i >= 1; i--)
        {
            var thought = previousThoughts[i];
            var textContent = new StringBuilder();
            var functionCalls = new List<FunctionCallContent>();
            var functionResults = new List<FunctionResultContent>();

            foreach (var part in thought.Parts)
            {
                switch (part.Type)
                {
                    case ThinkingMessagePartType.Text:
                        textContent.Append(part.Text);
                        break;
                    case ThinkingMessagePartType.FunctionCall:
                        var arguments = !string.IsNullOrEmpty(part.FunctionCall!.Arguments)
                            ? JsonSerializer.Deserialize<Dictionary<string, object?>>(part.FunctionCall!.Arguments)
                            : null;
                        var kernelArgs = arguments is not null ? new KernelArguments(arguments) : null;

                        functionCalls.Add(new FunctionCallContent(
                            functionName: part.FunctionCall!.Name,
                            pluginName: part.FunctionCall.PluginName,
                            id: part.FunctionCall.Id,
                            arguments: kernelArgs
                        ));
                        break;
                    case ThinkingMessagePartType.FunctionResult:
                        var resultObject = part.FunctionResult!.Result;
                        var resultString = resultObject as string ?? JsonSerializer.Serialize(resultObject);
                        functionResults.Add(new FunctionResultContent(
                            callId: part.FunctionResult.CallId,
                            functionName: part.FunctionResult.FunctionName,
                            pluginName: part.FunctionResult.PluginName,
                            result: resultString
                        ));
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            if (thought.Role == ThinkingThoughtRole.User)
            {
                chatHistory.AddUserMessage(textContent.ToString());
            }
            else
            {
                var assistantMessage = new ChatMessageContent(AuthorRole.Assistant, textContent.ToString());
                if (functionCalls.Count > 0)
                {
                    assistantMessage.Items = [];
                    foreach (var fc in functionCalls)
                    {
                        assistantMessage.Items.Add(fc);
                    }
                }

                chatHistory.Add(assistantMessage);

                if (functionResults.Count <= 0) continue;
                foreach (var fr in functionResults)
                {
                    chatHistory.Add(fr.ToChatMessage());
                }
            }
        }

        chatHistory.AddUserMessage(request.UserMessage);

        // Set response for streaming
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.StatusCode = 200;

        var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
        var executionSettings = provider.CreatePromptExecutionSettings(request.ServiceId);

        var assistantParts = new List<SnThinkingMessagePart>();

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
                    resultContent = await functionCall.InvokeAsync(kernel);
                }
                catch (Exception ex)
                {
                    resultContent = new FunctionResultContent(functionCall.Id!, ex.Message);
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

                var resultMessageJson =
                    JsonSerializer.Serialize(new { type = "function_result", data = resultPart.FunctionResult });
                await Response.Body.WriteAsync(Encoding.UTF8.GetBytes($"data: {resultMessageJson}\n\n"));
                await Response.Body.FlushAsync();
            }
        }

        // Save assistant thought
        var savedThought = await service.SaveThoughtAsync(
            sequence,
            assistantParts,
            ThinkingThoughtRole.Assistant,
            serviceId,
            botName: "snchan"
        );

        // Write the topic if it was newly set, then the thought object as JSON to the stream
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

    private async Task<ActionResult> ThinkWithMiChanAsync(StreamThinkingRequest request, Account currentUser, Guid accountId)
    {
        if (!miChanConfig.Enabled)
        {
            return BadRequest("MiChan is currently disabled.");
        }

        // Generate a topic if creating a new sequence
        string? topic = null;
        if (!request.SequenceId.HasValue)
        {
            var summaryKernel = miChanKernelProvider.GetKernel();
            var summaryHistory = new ChatHistory(
                "You are a helpful assistant. Summarize the following user message into a concise topic title (max 100 characters).\n" +
                "Direct give the topic you summerized, do not add extra prefix / suffix."
            );
            summaryHistory.AddUserMessage(request.UserMessage);

            var summaryChatService = summaryKernel.GetRequiredService<IChatCompletionService>();
            var summaryResult = await summaryChatService.GetChatMessageContentAsync(summaryHistory);
            topic = summaryResult.Content?[..Math.Min(summaryResult.Content.Length, 4096)];
        }

        // Handle sequence
        var sequence = await service.GetOrCreateSequenceAsync(accountId, request.SequenceId, topic);
        if (sequence == null) return Forbid();

        // Save user thought with bot identifier
        await service.SaveThoughtAsync(sequence, [
            new SnThinkingMessagePart
            {
                Type = ThinkingMessagePartType.Text,
                Text = request.UserMessage
            }
        ], ThinkingThoughtRole.User, botName: "michan");

        // Get or create context ID
        var contextId = request.SequenceId?.ToString() ?? $"thought_{accountId}_{sequence.Id}";

        // Build kernel with all plugins (only if not already registered)
        var kernel = miChanKernelProvider.GetKernel();
        var chatPlugin = serviceProvider.GetRequiredService<ChatPlugin>();
        var postPlugin = serviceProvider.GetRequiredService<PostPlugin>();
        var notificationPlugin = serviceProvider.GetRequiredService<NotificationPlugin>();
        var accountPlugin = serviceProvider.GetRequiredService<AccountPlugin>();

        if (!kernel.Plugins.Contains("chat"))
            kernel.Plugins.AddFromObject(chatPlugin, "chat");
        if (!kernel.Plugins.Contains("post"))
            kernel.Plugins.AddFromObject(postPlugin, "post");
        if (!kernel.Plugins.Contains("notification"))
            kernel.Plugins.AddFromObject(notificationPlugin, "notification");
        if (!kernel.Plugins.Contains("account"))
            kernel.Plugins.AddFromObject(accountPlugin, "account");

        // Load personality
        var personality = PersonalityLoader.LoadPersonality(miChanConfig.PersonalityFile, miChanConfig.Personality, logger);

        // For non-superusers, MiChan decides whether to execute actions
        var isSuperuser = currentUser.IsSuperuser;
        
        // Decision gate for non-superusers
        if (!isSuperuser)
        {
            var decisionPrompt = $@"
You are MiChan. A user asked you to: ""{request.UserMessage}""

You have these tools available:
- chat: send_message, get_chat_history, list_chat_rooms
- post: get_post, create_post, like_post, reply_to_post, repost_post, search_posts
- notification: get_notifications, approve_chat_request, decline_chat_request
- account: get_account_info, search_accounts, follow_account, unfollow_account

Should you execute what the user is asking? Consider:
- Is this safe and appropriate?
- Does this align with helping users on the Solar Network?
- Is the user asking for something harmful or against platform rules?

Respond with ONLY 'EXECUTE' or 'REFUSE'.";

            var decisionHistory = new ChatHistory(personality);
            decisionHistory.AddUserMessage(decisionPrompt);

            var decisionService = kernel.GetRequiredService<IChatCompletionService>();
            var decisionExecutionSettings = miChanKernelProvider.CreatePromptExecutionSettings();
            var decisionResult = await decisionService.GetChatMessageContentAsync(decisionHistory, decisionExecutionSettings, kernel);
            var decision = decisionResult.Content?.Trim().ToUpper();

            if (decision?.Contains("REFUSE") == true)
            {
                // Save refusal
                await service.SaveThoughtAsync(sequence, [
                    new SnThinkingMessagePart
                    {
                        Type = ThinkingMessagePartType.Text,
                        Text = "I cannot do that."
                    }
                ], ThinkingThoughtRole.Assistant, miChanConfig.ThinkingService, botName: "michan");

                // Stream refusal
                Response.Headers.Append("Content-Type", "text/event-stream");
                Response.StatusCode = 200;

                var refusalJson = JsonSerializer.Serialize(new { type = "text", data = "I cannot do that." });
                await Response.Body.WriteAsync(Encoding.UTF8.GetBytes($"data: {refusalJson}\n\n"));
                await Response.Body.FlushAsync();

                // Save thought reference
                var thoughtJson = JsonSerializer.Serialize(new { type = "thought", data = new { Id = Guid.NewGuid(), Refused = true } });
                await Response.Body.WriteAsync(Encoding.UTF8.GetBytes($"thought: {thoughtJson}\n\n"));
                await Response.Body.FlushAsync();

                return new EmptyResult();
            }
        }

        // Build chat history
        var chatHistory = new ChatHistory($@"
{personality}

You are in a conversation with {currentUser.Nick} ({currentUser.Name}).
{(isSuperuser ? "This user is an administrator and has full control. Execute their commands immediately." : "Help the user with their requests using your available tools when appropriate.")}
");

        // Add proposal info
        chatHistory.AddSystemMessage(
            "You can issue some proposals to user, like creating a post. The proposal syntax is like a xml tag, with an attribute indicates which proposal.\n" +
            "Depends on the proposal type, the payload (content inside the xml tag) might be different.\n" +
            "\n" +
            "Example: <proposal type=\"post_create\">...post content...</proposal>\n" +
            "\n" +
            "Here are some references of the proposals you can issue, but if you want to issue one, make sure the user is accept it.\n" +
            "1. post_create: body takes simple string, create post for user." +
            "\n" +
            $"The user currently accept these proposals: {string.Join(',', request.AcceptProposals)}"
        );

        // Load conversation history from memory using hybrid semantic + recent search
        var history = await miChanMemoryService.GetRelevantContextAsync(
            contextId,
            currentQuery: request.UserMessage,
            semanticCount: 5,
            recentCount: 10);
        foreach (var interaction in history.OrderBy(h => h.CreatedAt))
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

        // Add attached posts with image analysis if available
        if (request.AttachedPosts is { Count: > 0 })
        {
            var postsWithImages = new List<SnPost>();
            var postTexts = new List<string>();

            foreach (var postId in request.AttachedPosts)
            {
                try
                {
                    if (Guid.TryParse(postId, out var postGuid))
                    {
                        var post = await apiClient.GetAsync<SnPost>("sphere", $"/posts/{postGuid}");
                        if (post != null)
                        {
                            postTexts.Add($"Post by @{post.Publisher?.Name}: {post.Content}");
                            if (post.Attachments?.Count > 0)
                            {
                                postsWithImages.Add(post);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to fetch attached post {PostId}", postId);
                }
            }

            // Add post text content
            if (postTexts.Count > 0)
            {
                chatHistory.AddUserMessage("Attached posts:\n" + string.Join("\n\n", postTexts));
            }

            // Analyze images using vision model if posts have attachments and vision is enabled
            if (postsWithImages.Count > 0 && miChanConfig.Vision.EnableVisionAnalysis && miChanKernelProvider.IsVisionModelAvailable())
            {
                try
                {
                    var visionChatHistory = await BuildVisionChatHistoryForPostsAsync(postsWithImages, request.UserMessage);
                    var visionKernel = miChanKernelProvider.GetVisionKernel();
                    var visionSettings = miChanKernelProvider.CreateVisionPromptExecutionSettings();
                    var chatCompletionService = visionKernel.GetRequiredService<IChatCompletionService>();
                    var visionResult = await chatCompletionService.GetChatMessageContentAsync(visionChatHistory, visionSettings);

                    if (!string.IsNullOrEmpty(visionResult.Content))
                    {
                        chatHistory.AddSystemMessage($"Image analysis: {visionResult.Content}");
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to analyze images in attached posts");
                }
            }
        }

        if (request.AttachedMessages is { Count: > 0 })
        {
            chatHistory.AddUserMessage($"Attached chat messages: {JsonSerializer.Serialize(request.AttachedMessages)}");
        }

        chatHistory.AddUserMessage(request.UserMessage);

        // Set response for streaming
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.StatusCode = 200;

        var chatService = kernel.GetRequiredService<IChatCompletionService>();
        var executionSettings = miChanKernelProvider.CreatePromptExecutionSettings();

        var assistantParts = new List<SnThinkingMessagePart>();
        var fullResponse = new StringBuilder();

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
                    resultContent = await functionCall.InvokeAsync(kernel);
                }
                catch (Exception ex)
                {
                    resultContent = new FunctionResultContent(functionCall.Id!, ex.Message);
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

                var resultMessageJson = JsonSerializer.Serialize(new { type = "function_result", data = resultPart.FunctionResult });
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
        await miChanMemoryService.StoreInteractionAsync(
            "thought",
            contextId,
            new Dictionary<string, object>
            {
                ["message"] = request.UserMessage,
                ["response"] = fullResponse.ToString(),
                ["timestamp"] = DateTime.UtcNow,
                ["is_superuser"] = isSuperuser
            }
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

    /// <summary>
    /// Build a ChatHistory with images for vision analysis of attached posts
    /// </summary>
    private async Task<ChatHistory> BuildVisionChatHistoryForPostsAsync(List<SnPost> posts, string userQuery)
    {
        var chatHistory = new ChatHistory("You are an AI assistant analyzing images in social media posts. Describe what you see in the images and relate it to the user's question.");

        // Build the text part of the message
        var textBuilder = new StringBuilder();
        textBuilder.AppendLine("The user has shared posts with images and asked a question.");
        textBuilder.AppendLine();
        textBuilder.AppendLine("Posts:");

        foreach (var post in posts)
        {
            textBuilder.AppendLine($"- Post by @{post.Publisher?.Name}: {post.Content}");
        }

        textBuilder.AppendLine();
        textBuilder.AppendLine($"User's question: {userQuery}");
        textBuilder.AppendLine();
        textBuilder.AppendLine("Please analyze the images and provide relevant context to help answer the user's question.");

        // Create a collection to hold all content items (text + images)
        var contentItems = new ChatMessageContentItemCollection();
        contentItems.Add(new TextContent(textBuilder.ToString()));

        // Download and add images
        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(miChanConfig.GatewayUrl)
        };
        httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("AtField", miChanConfig.AccessToken);

        foreach (var post in posts)
        {
            if (post.Attachments == null) continue;

            foreach (var attachment in post.Attachments)
            {
                try
                {
                    if (attachment.MimeType?.StartsWith("image/") == true)
                    {
                        var imagePath = attachment.Url ?? $"/drive/files/{attachment.Id}";
                        var imageBytes = await httpClient.GetByteArrayAsync(imagePath);
                        if (imageBytes != null && imageBytes.Length > 0)
                        {
                            contentItems.Add(new ImageContent(imageBytes, attachment.MimeType));
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to download image {FileId} for vision analysis", attachment.Id);
                }
            }
        }

        // Create a ChatMessageContent with all items and add it to history
        var userMessage = new ChatMessageContent
        {
            Role = AuthorRole.User,
            Items = contentItems
        };
        chatHistory.Add(userMessage);

        return chatHistory;
    }
}

#pragma warning restore SKEXP0050

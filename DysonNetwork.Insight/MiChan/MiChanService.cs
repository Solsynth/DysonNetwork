#pragma warning disable SKEXP0050
using System.Text.Json;
using DysonNetwork.Insight.MiChan.Plugins;
using DysonNetwork.Shared.Models;
using Microsoft.SemanticKernel;

namespace DysonNetwork.Insight.MiChan;

public class MiChanService(
    MiChanConfig config,
    ILogger<MiChanService> logger,
    IServiceProvider serviceProvider,
    Thought.ThoughtService thoughtService)
    : BackgroundService
{
    private MiChanWebSocketHandler? _webSocketHandler;
    private MiChanKernelProvider? _kernelProvider;
    private MiChanMemoryService? _memoryService;
    private MiChanAutonomousBehavior? _autonomousBehavior;
    private SolarNetworkApiClient? _apiClient;
    private Kernel? _kernel;
    private string? _cachedPersonality;

    private readonly Dictionary<Guid, List<MiChanMessage>> _conversations = new();
    private readonly SemaphoreSlim _conversationLock = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.Enabled)
        {
            logger.LogInformation("MiChan is disabled. Skipping initialization.");
            return;
        }

        if (string.IsNullOrEmpty(config.AccessToken) || string.IsNullOrEmpty(config.BotAccountId))
        {
            logger.LogWarning("MiChan is enabled but AccessToken or BotAccountId is not configured.");
            return;
        }

        logger.LogInformation("Starting MiChan service...");

        // Wait 1 minute for other services to start
        logger.LogInformation("MiChan waiting 60 seconds for other services to initialize...");
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        // Initialize services
        await InitializeAsync(stoppingToken);

        // Start autonomous behavior loop
        if (config.AutonomousBehavior.Enabled)
        {
            _ = Task.Run(async () => await AutonomousLoopAsync(stoppingToken), stoppingToken);
        }

        // Connect WebSocket
        await ConnectWebSocketAsync(stoppingToken);

        // Keep the service running
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Check if WebSocket is connected, reconnect if needed
                if (_webSocketHandler is { IsConnected: false })
                {
                    logger.LogWarning("WebSocket disconnected. Attempting to reconnect...");
                    await ConnectWebSocketAsync(stoppingToken);
                }

                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in MiChan service loop");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        logger.LogInformation("MiChan service stopped");
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Create API client
            _apiClient = serviceProvider.GetRequiredService<SolarNetworkApiClient>();

            // Create memory service
            _memoryService = serviceProvider.GetRequiredService<MiChanMemoryService>();

            // Create kernel provider and get kernel
            _kernelProvider = serviceProvider.GetRequiredService<MiChanKernelProvider>();
            _kernel = _kernelProvider.GetKernel();

            // Register plugins (only if not already registered)
            var chatPlugin = serviceProvider.GetRequiredService<ChatPlugin>();
            var postPlugin = serviceProvider.GetRequiredService<PostPlugin>();
            var notificationPlugin = serviceProvider.GetRequiredService<NotificationPlugin>();
            var accountPlugin = serviceProvider.GetRequiredService<AccountPlugin>();

            if (!_kernel.Plugins.Contains("chat"))
                _kernel.Plugins.AddFromObject(chatPlugin, "chat");
            if (!_kernel.Plugins.Contains("post"))
                _kernel.Plugins.AddFromObject(postPlugin, "post");
            if (!_kernel.Plugins.Contains("notification"))
                _kernel.Plugins.AddFromObject(notificationPlugin, "notification");
            if (!_kernel.Plugins.Contains("account"))
                _kernel.Plugins.AddFromObject(accountPlugin, "account");

            // Create autonomous behavior (includes post checking)
            _autonomousBehavior = serviceProvider.GetRequiredService<MiChanAutonomousBehavior>();
            await _autonomousBehavior.InitializeAsync();

            // Load personality from file if configured
            _cachedPersonality = PersonalityLoader.LoadPersonality(config.PersonalityFile, config.Personality, logger);

            logger.LogInformation("MiChan initialized successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize MiChan");
            throw;
        }
    }

    private async Task AutonomousLoopAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Starting autonomous behavior loop...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var executed = await _autonomousBehavior!.TryExecuteAutonomousActionAsync();
                
                if (executed)
                {
                    logger.LogInformation("Autonomous action executed successfully");
                }

                // Wait before checking again (5 minute interval check)
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in autonomous behavior loop");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }

        logger.LogInformation("Autonomous behavior loop stopped");
    }

    private async Task ConnectWebSocketAsync(CancellationToken cancellationToken)
    {
        try
        {
            _webSocketHandler = new MiChanWebSocketHandler(
                config,
                serviceProvider.GetRequiredService<ILogger<MiChanWebSocketHandler>>(),
                this
            );

            _webSocketHandler.OnPacketReceived += OnWebSocketPacketReceived;
            _webSocketHandler.OnConnected += (s, e) => logger.LogInformation("MiChan WebSocket connected");
            _webSocketHandler.OnDisconnected += (s, e) => logger.LogWarning("MiChan WebSocket disconnected");

            await _webSocketHandler.ConnectAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to connect WebSocket");
            throw;
        }
    }

    private void OnWebSocketPacketReceived(object? sender, WebSocketPacket packet)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                switch (packet.Type)
                {
                    case WebSocketPacketType.MessageNew:
                        await HandleNewMessageAsync(packet);
                        break;
                    case WebSocketPacketType.MessageUpdate:
                        // Handle message updates if needed
                        break;
                    case WebSocketPacketType.MessageDelete:
                        // Handle message deletions if needed
                        break;
                    default:
                        // Handle other packet types if needed
                        logger.LogDebug("Received WebSocket packet of type: {Type}", packet.Type);
                        break;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error handling WebSocket packet of type {Type}", packet.Type);
            }
        });
    }

    private async Task HandleNewMessageAsync(WebSocketPacket packet)
    {
        try
        {
            var messageData = packet.GetData<Dictionary<string, JsonElement>>();
            if (messageData == null) return;

            // Extract message info
            if (!messageData.TryGetValue("sender", out var senderElement) ||
                !messageData.TryGetValue("sender_id", out var senderIdElement) ||
                !messageData.TryGetValue("chat_room_id", out var roomIdElement) ||
                !messageData.TryGetValue("content", out var contentElement))
            {
                return;
            }

            var senderAccountId = senderElement.GetProperty("account_id").GetString();
            var senderId = senderIdElement.GetGuid();
            var roomId = roomIdElement.GetGuid();
            var content = contentElement.GetString();

            // Skip if the message is from MiChan herself
            if (senderAccountId == config.BotAccountId)
                return;

            // Check if this is a DM or mentions MiChan
            var shouldRespond = await ShouldRespondToMessageAsync(roomId, content);

            if (shouldRespond)
            {
                logger.LogInformation("Processing message from user {SenderId} in room {RoomId}", senderId, roomId);
                await GenerateAndSendResponseAsync(roomId, senderId, content);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling new message");
        }
    }

    private async Task<bool> ShouldRespondToMessageAsync(Guid roomId, string? content)
    {
        if (config.AutoRespond is { ToChatMessages: false, ToDirectMessages: false })
            return false;

        // Check if this is a DM (direct message with single user)
        var isDm = await IsDirectMessageAsync(roomId);

        if (isDm && config.AutoRespond.ToDirectMessages)
            return true;

        // Check if MiChan is mentioned in the message
        if (config.AutoRespond.ToMentions)
        {
            var botAccount = await _apiClient!.GetAsync<object>("pass", "/accounts/me");
            // Check if content mentions the bot by username or display name
            return content?.Contains("michan", StringComparison.OrdinalIgnoreCase) == true;
        }

        return false;
    }

    private async Task<bool> IsDirectMessageAsync(Guid roomId)
    {
        try
        {
            var roomInfo = await _apiClient!.GetAsync<Dictionary<string, JsonElement>>(
                "messager",
                $"/chat/rooms/{roomId}"
            );

            if (roomInfo != null && roomInfo.TryGetValue("type", out var typeElement))
                return typeElement.GetInt32() == 1;

            return false;
        }
        catch
        {
            return false;
        }
    }

    private async Task GenerateAndSendResponseAsync(Guid roomId, Guid senderId, string? content)
    {
        try
        {
            // Get conversation history
            var history = await GetConversationHistoryAsync(roomId);

            // Load personality (with potential hot reload from file)
            var systemMessage = GetPersonality();
            var chatHistory = new Microsoft.SemanticKernel.ChatCompletion.ChatHistory(systemMessage);

            // Add conversation history
            foreach (var msg in history.TakeLast(10))
            {
                if (msg.IsFromBot)
                    chatHistory.AddAssistantMessage(msg.Content);
                else
                    chatHistory.AddUserMessage(msg.Content);
            }

            // Add the current message
            chatHistory.AddUserMessage(content ?? "");

            // Store in memory service
            await _memoryService!.StoreInteractionAsync(
                "chat",
                roomId.ToString(),
                new Dictionary<string, object>
                {
                    ["sender_id"] = senderId.ToString(),
                    ["message"] = content ?? "",
                    ["timestamp"] = DateTime.UtcNow
                }
            );

            // Get response from AI
            var executionSettings = _kernelProvider!.CreatePromptExecutionSettings();
            var result = await _kernel!.InvokePromptAsync(
                string.Join("\n", chatHistory.Select(h => $"{h.Role}: {h.Content}")),
                new KernelArguments(executionSettings)
            );

            var response = result.GetValue<string>() ?? "I'm not sure how to respond to that.";

            // Store the messages
            await AddMessageToConversationAsync(roomId, senderId.ToString(), content ?? "", false);
            await AddMessageToConversationAsync(roomId, config.BotAccountId, response, true);

            // Update memory with response
            await _memoryService.StoreMemoryAsync(
                roomId.ToString(),
                "last_response",
                response
            );

            // Send response via chat plugin
            var chatPlugin = _kernel.Plugins["chat"];
            var sendMessageFunction = chatPlugin["send_message"];
            await _kernel.InvokeAsync(sendMessageFunction, new KernelArguments
            {
                ["chatRoomId"] = roomId.ToString(),
                ["content"] = response
            });

            logger.LogInformation("Sent response to room {RoomId}", roomId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error generating and sending response");
        }
    }

    public string GetPersonality()
    {
        // If personality file is configured, reload it each time (hot reload support)
        if (!string.IsNullOrWhiteSpace(config.PersonalityFile))
            return PersonalityLoader.LoadPersonality(config.PersonalityFile, config.Personality, logger);

        // Otherwise use cached personality or config value
        return _cachedPersonality ?? config.Personality;
    }

    private async Task<List<MiChanMessage>> GetConversationHistoryAsync(Guid roomId)
    {
        await _conversationLock.WaitAsync();
        try
        {
            if (!_conversations.ContainsKey(roomId))
            {
                _conversations[roomId] = new List<MiChanMessage>();
            }
            return _conversations[roomId];
        }
        finally
        {
            _conversationLock.Release();
        }
    }

    private async Task AddMessageToConversationAsync(Guid roomId, string senderId, string content, bool isFromBot)
    {
        await _conversationLock.WaitAsync();
        try
        {
            if (!_conversations.ContainsKey(roomId))
            {
                _conversations[roomId] = new List<MiChanMessage>();
            }

            _conversations[roomId].Add(new MiChanMessage
            {
                SenderId = senderId,
                Content = content,
                IsFromBot = isFromBot,
                Timestamp = DateTime.UtcNow
            });

            // Keep only last 50 messages per conversation
            if (_conversations[roomId].Count > 50)
            {
                _conversations[roomId] = _conversations[roomId].Skip(_conversations[roomId].Count - 50).ToList();
            }
        }
        finally
        {
            _conversationLock.Release();
        }
    }

    /// <summary>
    /// Gets or creates a thought sequence and memorizes it using the embedding service.
    /// </summary>
    /// <param name="accountId">The account ID for the thought sequence</param>
    /// <param name="sequenceId">Optional existing sequence ID to retrieve</param>
    /// <param name="topic">Optional topic for new sequences</param>
    /// <param name="contextId">The context ID for memory storage (e.g., chat room ID)</param>
    /// <param name="additionalContext">Optional additional context to store with the memory</param>
    /// <returns>The thought sequence ID if successful, null otherwise</returns>
    public async Task<Guid?> GetAndMemorizeThoughtSequenceAsync(
        Guid accountId,
        Guid? sequenceId = null,
        string? topic = null,
        string? contextId = null,
        Dictionary<string, object>? additionalContext = null)
    {
        try
        {
            // Get or create the thought sequence
            var sequence = await thoughtService.GetOrCreateSequenceAsync(accountId, sequenceId, topic);
            if (sequence == null)
            {
                logger.LogWarning("Failed to get or create thought sequence for account {AccountId}", accountId);
                return null;
            }

            // Prepare context for memory storage
            var memoryContext = new Dictionary<string, object>
            {
                ["sequence_id"] = sequence.Id,
                ["account_id"] = accountId,
                ["topic"] = topic ?? "No topic",
                ["total_tokens"] = sequence.TotalToken,
                ["created_at"] = sequence.CreatedAt,
                ["timestamp"] = DateTime.UtcNow
            };

            // Add any additional context provided
            if (additionalContext != null)
            {
                foreach (var kvp in additionalContext)
                {
                    memoryContext[kvp.Key] = kvp.Value;
                }
            }

            // Store the interaction with embedding using the memory service
            // This internally uses the EmbeddingService to generate embeddings
            await _memoryService!.StoreInteractionAsync(
                type: "thought_sequence",
                contextId: contextId ?? sequence.Id.ToString(),
                context: memoryContext,
                memory: new Dictionary<string, object>
                {
                    ["sequence_id"] = sequence.Id,
                    ["topic"] = topic ?? "No topic"
                }
            );

            logger.LogInformation(
                "Successfully memorized thought sequence {SequenceId} for account {AccountId} with topic: {Topic}",
                sequence.Id,
                accountId,
                topic ?? "No topic"
            );

            return sequence.Id;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting and memorizing thought sequence for account {AccountId}", accountId);
            return null;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Stopping MiChan service...");

        if (_webSocketHandler != null)
            await _webSocketHandler.DisposeAsync();

        await base.StopAsync(cancellationToken);
    }
}
#pragma warning restore SKEXP0050

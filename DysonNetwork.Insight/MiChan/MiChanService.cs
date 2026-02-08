#pragma warning disable SKEXP0050
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using DysonNetwork.Insight.MiChan.Plugins;
using DysonNetwork.Shared.Models;
using Microsoft.SemanticKernel;

namespace DysonNetwork.Insight.MiChan;

public class MiChanService : BackgroundService
{
    private readonly MiChanConfig _config;
    private readonly ILogger<MiChanService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private MiChanWebSocketHandler? _webSocketHandler;
    private MiChanKernelProvider? _kernelProvider;
    private MiChanMemoryService? _memoryService;
    private MiChanAutonomousBehavior? _autonomousBehavior;
    private MiChanPostMonitor? _postMonitor;
    private SolarNetworkApiClient? _apiClient;
    private Kernel? _kernel;
    private string? _cachedPersonality;

    private readonly Dictionary<Guid, List<MiChanMessage>> _conversations = new();
    private readonly SemaphoreSlim _conversationLock = new(1, 1);

    public MiChanService(
        MiChanConfig config,
        ILogger<MiChanService> logger,
        IServiceProvider serviceProvider)
    {
        _config = config;
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.Enabled)
        {
            _logger.LogInformation("MiChan is disabled. Skipping initialization.");
            return;
        }

        if (string.IsNullOrEmpty(_config.AccessToken) || string.IsNullOrEmpty(_config.BotAccountId))
        {
            _logger.LogWarning("MiChan is enabled but AccessToken or BotAccountId is not configured.");
            return;
        }

        _logger.LogInformation("Starting MiChan service...");

        // Initialize services
        await InitializeAsync(stoppingToken);

        // Start autonomous behavior loop
        if (_config.AutonomousBehavior.Enabled)
        {
            _ = Task.Run(async () => await AutonomousLoopAsync(stoppingToken), stoppingToken);
        }

        // Connect WebSocket
        await ConnectWebSocketAsync(stoppingToken);

        // Start post monitoring
        if (_config.PostMonitoring.Enabled && _postMonitor != null)
        {
            _postMonitor.StartMonitoring();
        }

        // Keep the service running
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Check if WebSocket is connected, reconnect if needed
                if (_webSocketHandler != null && !_webSocketHandler.IsConnected)
                {
                    _logger.LogWarning("WebSocket disconnected. Attempting to reconnect...");
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
                _logger.LogError(ex, "Error in MiChan service loop");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        _logger.LogInformation("MiChan service stopped");
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Create API client
            _apiClient = _serviceProvider.GetRequiredService<SolarNetworkApiClient>();

            // Create memory service
            _memoryService = _serviceProvider.GetRequiredService<MiChanMemoryService>();

            // Create kernel provider and get kernel
            _kernelProvider = _serviceProvider.GetRequiredService<MiChanKernelProvider>();
            _kernel = _kernelProvider.GetKernel();

            // Register plugins
            var chatPlugin = _serviceProvider.GetRequiredService<ChatPlugin>();
            var postPlugin = _serviceProvider.GetRequiredService<PostPlugin>();
            var notificationPlugin = _serviceProvider.GetRequiredService<NotificationPlugin>();
            var accountPlugin = _serviceProvider.GetRequiredService<AccountPlugin>();

            _kernel.Plugins.AddFromObject(chatPlugin, "chat");
            _kernel.Plugins.AddFromObject(postPlugin, "post");
            _kernel.Plugins.AddFromObject(notificationPlugin, "notification");
            _kernel.Plugins.AddFromObject(accountPlugin, "account");

            // Create autonomous behavior
            _autonomousBehavior = _serviceProvider.GetRequiredService<MiChanAutonomousBehavior>();
            await _autonomousBehavior.InitializeAsync();

            // Create post monitor
            _postMonitor = _serviceProvider.GetRequiredService<MiChanPostMonitor>();
            await _postMonitor.InitializeAsync();

            // Load personality from file if configured
            _cachedPersonality = PersonalityLoader.LoadPersonality(_config.PersonalityFile, _config.Personality, _logger);

            _logger.LogInformation("MiChan initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize MiChan");
            throw;
        }
    }

    private async Task AutonomousLoopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting autonomous behavior loop...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var executed = await _autonomousBehavior!.TryExecuteAutonomousActionAsync();
                
                if (executed)
                {
                    _logger.LogInformation("Autonomous action executed successfully");
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
                _logger.LogError(ex, "Error in autonomous behavior loop");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }

        _logger.LogInformation("Autonomous behavior loop stopped");
    }

    private async Task ConnectWebSocketAsync(CancellationToken cancellationToken)
    {
        try
        {
            _webSocketHandler = new MiChanWebSocketHandler(
                _config,
                _serviceProvider.GetRequiredService<ILogger<MiChanWebSocketHandler>>(),
                this
            );

            _webSocketHandler.OnPacketReceived += OnWebSocketPacketReceived;
            _webSocketHandler.OnConnected += (s, e) => _logger.LogInformation("MiChan WebSocket connected");
            _webSocketHandler.OnDisconnected += (s, e) => _logger.LogWarning("MiChan WebSocket disconnected");

            await _webSocketHandler.ConnectAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect WebSocket");
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
                    case "ping":
                        // Send pong
                        if (_webSocketHandler != null)
                        {
                            await _webSocketHandler.SendPacketAsync(new WebSocketPacket
                            {
                                Type = WebSocketPacketType.Pong
                            });
                        }
                        break;
                    default:
                        // Handle other packet types if needed
                        _logger.LogDebug("Received WebSocket packet of type: {Type}", packet.Type);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling WebSocket packet of type {Type}", packet.Type);
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
            if (!messageData.TryGetValue("id", out var idElement) ||
                !messageData.TryGetValue("sender_id", out var senderIdElement) ||
                !messageData.TryGetValue("chat_room_id", out var roomIdElement) ||
                !messageData.TryGetValue("content", out var contentElement))
            {
                return;
            }

            var messageId = idElement.GetGuid();
            var senderId = senderIdElement.GetGuid();
            var roomId = roomIdElement.GetGuid();
            var content = contentElement.GetString();

            // Skip if the message is from MiChan herself
            if (senderId.ToString() == _config.BotAccountId)
                return;

            // Check if this is a DM or mentions MiChan
            var shouldRespond = await ShouldRespondToMessageAsync(roomId, senderId, content);

            if (shouldRespond)
            {
                _logger.LogInformation("Processing message from user {SenderId} in room {RoomId}", senderId, roomId);
                await GenerateAndSendResponseAsync(roomId, senderId, content);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling new message");
        }
    }

    private async Task<bool> ShouldRespondToMessageAsync(Guid roomId, Guid senderId, string? content)
    {
        if (!_config.AutoRespond.ToChatMessages && !_config.AutoRespond.ToDirectMessages)
            return false;

        // Check if this is a DM (direct message with single user)
        var isDM = await IsDirectMessageAsync(roomId);

        if (isDM && _config.AutoRespond.ToDirectMessages)
            return true;

        // Check if MiChan is mentioned in the message
        if (_config.AutoRespond.ToMentions)
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
            {
                return typeElement.GetString() == "direct";
            }

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
                {
                    chatHistory.AddAssistantMessage(msg.Content);
                }
                else
                {
                    chatHistory.AddUserMessage(msg.Content);
                }
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
            await AddMessageToConversationAsync(roomId, _config.BotAccountId, response, true);

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

            _logger.LogInformation("Sent response to room {RoomId}", roomId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating and sending response");
        }
    }

    private string GetPersonality()
    {
        // If personality file is configured, reload it each time (hot reload support)
        if (!string.IsNullOrWhiteSpace(_config.PersonalityFile))
        {
            return PersonalityLoader.LoadPersonality(_config.PersonalityFile, _config.Personality, _logger);
        }

        // Otherwise use cached personality or config value
        return _cachedPersonality ?? _config.Personality;
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

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping MiChan service...");

        _postMonitor?.StopMonitoring();
        _postMonitor?.Dispose();

        if (_webSocketHandler != null)
        {
            await _webSocketHandler.DisposeAsync();
        }

        await base.StopAsync(cancellationToken);
    }
}
#pragma warning restore SKEXP0050

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using DysonNetwork.Messager.Chat;
using DysonNetwork.Messager.Chat.Voice;
using DysonNetwork.Messager.Chat.Realtime;
using DysonNetwork.Messager.Poll;
using DysonNetwork.Messager.Wallet;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.EventBus;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Models.Embed;
using DysonNetwork.Shared.Pagination;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Queue;
using DysonNetwork.Shared.Registry;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;
using WebSocketPacket = DysonNetwork.Shared.Models.WebSocketPacket;

namespace DysonNetwork.Messager.Startup;

public static class ServiceCollectionExtensions
{
    private class SendMessageWsRequest : ChatController.SendMessageRequest
    {
        public Guid ChatRoomId { get; set; }
    }

    extension(IServiceCollection services)
    {
        public IServiceCollection AddAppServices()
        {
            services.AddDbContext<AppDatabase>();
            services.AddHttpContextAccessor();

            services.AddHttpClient();

            services
                .AddControllers()
                .AddPaginationValidationFilter()
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.NumberHandling =
                        JsonNumberHandling.AllowNamedFloatingPointLiterals;
                    options.JsonSerializerOptions.PropertyNamingPolicy =
                        JsonNamingPolicy.SnakeCaseLower;

                    options.JsonSerializerOptions.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
                });

            services.AddGrpc(options => { options.EnableDetailedErrors = true; });
            services.AddGrpcReflection();

            return services;
        }

        public IServiceCollection AddAppAuthentication()
        {
            services.AddAuthorization();
            return services;
        }

        public IServiceCollection AddAppBusinessServices(IConfiguration configuration
        )
        {
            _ = services
                .AddSingleton<Shared.Localization.ILocalizationService,
                    Shared.Localization.JsonLocalizationService>(sp =>
                {
                    var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                    const string resourceNamespace = "DysonNetwork.Messager.Resources.Locales";
                    return new Shared.Localization.JsonLocalizationService(assembly, resourceNamespace);
                });

            services.AddScoped<ChatRoomService>();
            services.AddScoped<ChatService>();
            services.AddScoped<ChatVoiceService>();
            services.AddScoped<IRealtimeService, LiveKitRealtimeService>();
            services.AddLazyGrpcClientFactory<DyStickerService.DyStickerServiceClient>();

            services.AddEventBus()
                .AddListener<AccountDeletedEvent>(
                    AccountDeletedEvent.Type,
                    async (evt, ctx) =>
                    {
                        var logger = ctx.ServiceProvider.GetRequiredService<ILogger<EventBus>>();
                        var db = ctx.ServiceProvider.GetRequiredService<AppDatabase>();

                        logger.LogWarning("Account deleted: {AccountId}", evt.AccountId);

                        await db.ChatMembers
                            .Where(m => m.AccountId == evt.AccountId)
                            .ExecuteDeleteAsync(ctx.CancellationToken);

                        await using var transaction = await db.Database.BeginTransactionAsync(ctx.CancellationToken);
                        try
                        {
                            var now = SystemClock.Instance.GetCurrentInstant();
                            await db.ChatMessages
                                .Where(m => m.Sender.AccountId == evt.AccountId)
                                .ExecuteUpdateAsync(c => c.SetProperty(p => p.DeletedAt, now), ctx.CancellationToken);

                            await db.ChatReactions
                                .Where(r => r.Sender.AccountId == evt.AccountId)
                                .ExecuteUpdateAsync(c => c.SetProperty(p => p.DeletedAt, now), ctx.CancellationToken);

                            await db.ChatMembers
                                .Where(m => m.AccountId == evt.AccountId)
                                .ExecuteUpdateAsync(c => c.SetProperty(p => p.DeletedAt, now), ctx.CancellationToken);

                            await transaction.CommitAsync(ctx.CancellationToken);
                        }
                        catch
                        {
                            await transaction.RollbackAsync(ctx.CancellationToken);
                            throw;
                        }
                    },
                    opts =>
                    {
                        opts.UseJetStream = true;
                        opts.StreamName = "account_events";
                        opts.ConsumerName = "messager_account_deleted_handler";
                        opts.MaxRetries = 3;
                    }
                )
                .AddListener<WebSocketPacketEvent>(
                    WebSocketPacketEvent.SubjectPrefix + "messager",
                    async (evt, ctx) =>
                    {
                        var logger = ctx.ServiceProvider.GetRequiredService<ILogger<EventBus>>();

                        logger.LogDebug("Handling websocket packet...");

                        var packet = WebSocketPacket.FromBytes(evt.PacketBytes);
                        logger.LogInformation("Handling websocket packet... {Type}", packet.Type);

                        switch (packet.Type)
                        {
                            case "messages.send":
                                await HandleMessageSend(evt, packet, ctx);
                                break;
                            case "messages.read":
                                await HandleMessageRead(evt, packet, ctx);
                                break;
                            case "messages.typing":
                                await HandleMessageTyping(evt, packet, ctx);
                                break;
                            case "messages.subscribe":
                                await HandleMessageSubscribe(evt, packet, ctx);
                                break;
                            case "messages.unsubscribe":
                                await HandleMessageUnsubscribe(evt, packet, ctx);
                                break;
                            case "messages.test":
                                await HandleMessageTest(evt, packet, ctx);
                                break;
                            default:
                                logger.LogWarning("Unhandled websocket packet type: {Type}", packet.Type);
                                var ws = ctx.ServiceProvider.GetRequiredService<RemoteWebSocketService>();
                                await SendErrorResponse(evt, $"Unhandled websocket packet type: {packet.Type}", ws);
                                break;
                        }
                    },
                    opts =>
                    {
                        opts.UseJetStream = false;
                    }
                )
                .AddListener<WebSocketConnectedEvent>(
                    "websocket_connected",
                    async (evt, ctx) =>
                    {
                        await HandleWebSocketPresenceChanged(evt.AccountId, evt.DeviceId, true, evt.Timestamp, ctx);
                    },
                    opts =>
                    {
                        opts.UseJetStream = true;
                        opts.StreamName = "websocket_connections";
                        opts.ConsumerName = "messager_websocket_connected_presence";
                        opts.MaxRetries = 3;
                    }
                )
                .AddListener<WebSocketDisconnectedEvent>(
                    "websocket_disconnected",
                    async (evt, ctx) =>
                    {
                        if (!evt.IsOffline) return;
                        await HandleWebSocketPresenceChanged(evt.AccountId, evt.DeviceId, false, evt.Timestamp, ctx);
                    },
                    opts =>
                    {
                        opts.UseJetStream = true;
                        opts.StreamName = "websocket_connections";
                        opts.ConsumerName = "messager_websocket_disconnected_presence";
                        opts.MaxRetries = 3;
                    }
                )
                .AddListener<AccountPresenceActivitiesUpdatedEvent>(
                    AccountPresenceActivitiesUpdatedEvent.Type,
                    async (evt, ctx) =>
                    {
                        await HandlePresenceActivitiesUpdated(evt.AccountId, evt.Activities, evt.Timestamp, ctx);
                    },
                    opts =>
                    {
                        opts.UseJetStream = true;
                        opts.StreamName = "account_events";
                        opts.ConsumerName = "messager_account_presence_activities";
                        opts.MaxRetries = 3;
                    }
                );

            return services;
        }

        private static async Task HandlePresenceActivitiesUpdated(
            Guid accountId,
            List<SnPresenceActivity> activities,
            Instant timestamp,
            EventContext ctx
        )
        {
            var db = ctx.ServiceProvider.GetRequiredService<AppDatabase>();
            var crs = ctx.ServiceProvider.GetRequiredService<ChatRoomService>();
            var ws = ctx.ServiceProvider.GetRequiredService<RemoteWebSocketService>();
            var logger = ctx.ServiceProvider.GetRequiredService<ILogger<EventBus>>();

            var changedMembers = await db.ChatMembers
                .Where(m => m.AccountId == accountId)
                .Where(m => m.JoinedAt != null && m.LeaveAt == null)
                .Select(m => new { m.ChatRoomId, MemberId = m.Id })
                .ToListAsync(ctx.CancellationToken);
            if (changedMembers.Count == 0) return;

            var packetCount = 0;
            foreach (var changedMember in changedMembers)
            {
                var subscribedMemberIds = await crs.GetSubscribedMembers(changedMember.ChatRoomId);
                if (subscribedMemberIds.Count == 0) continue;

                var roomMembers = await crs.ListRoomMembers(changedMember.ChatRoomId);
                var subscribedAccounts = roomMembers
                    .Where(m => m.AccountId != accountId && subscribedMemberIds.Contains(m.Id))
                    .Select(m => m.AccountId.ToString())
                    .Distinct()
                    .ToList();
                if (subscribedAccounts.Count == 0) continue;

                await ws.PushWebSocketPacketToUsers(
                    subscribedAccounts,
                    WebSocketPacketType.ChatPresenceActivitiesUpdated,
                    InfraObjectCoder.ConvertObjectToByteString(new Dictionary<string, object>
                    {
                        ["room_id"] = changedMember.ChatRoomId,
                        ["member_id"] = changedMember.MemberId,
                        ["account_id"] = accountId,
                        ["activities"] = activities,
                        ["timestamp"] = timestamp
                    }).ToByteArray()
                );
                packetCount++;
            }

            logger.LogDebug(
                "Broadcast chat presence activities update for {AccountId} to subscribers in {RoomCount} rooms",
                accountId,
                packetCount
            );
        }

        private static async Task HandleWebSocketPresenceChanged(
            Guid accountId,
            string deviceId,
            bool isOnline,
            Instant timestamp,
            EventContext ctx
        )
        {
            var db = ctx.ServiceProvider.GetRequiredService<AppDatabase>();
            var crs = ctx.ServiceProvider.GetRequiredService<ChatRoomService>();
            var accounts = ctx.ServiceProvider.GetRequiredService<RemoteAccountService>();
            var ws = ctx.ServiceProvider.GetRequiredService<RemoteWebSocketService>();
            var logger = ctx.ServiceProvider.GetRequiredService<ILogger<EventBus>>();

            var changedMembers = await db.ChatMembers
                .Where(m => m.AccountId == accountId)
                .Where(m => m.JoinedAt != null && m.LeaveAt == null)
                .Select(m => new { m.ChatRoomId, MemberId = m.Id })
                .ToListAsync(ctx.CancellationToken);
            if (changedMembers.Count == 0) return;

            var statuses = await accounts.GetAccountStatusBatch([accountId]);
            var status = statuses.GetValueOrDefault(accountId) ?? new SnAccountStatus
            {
                AccountId = accountId,
                Attitude = StatusAttitude.Neutral,
                IsOnline = isOnline,
                IsCustomized = false,
                Type = StatusType.Default
            };
            status.IsOnline = isOnline && status.Type != StatusType.Invisible;

            var packetCount = 0;
            foreach (var changedMember in changedMembers)
            {
                var subscribedMemberIds = await crs.GetSubscribedMembers(changedMember.ChatRoomId);
                if (subscribedMemberIds.Count == 0) continue;

                var roomMembers = await crs.ListRoomMembers(changedMember.ChatRoomId);
                var subscribedAccounts = roomMembers
                    .Where(m => m.AccountId != accountId && subscribedMemberIds.Contains(m.Id))
                    .Select(m => m.AccountId.ToString())
                    .Distinct()
                    .ToList();
                if (subscribedAccounts.Count == 0) continue;

                await ws.PushWebSocketPacketToUsers(
                    subscribedAccounts,
                    WebSocketPacketType.ChatPresenceUpdated,
                    InfraObjectCoder.ConvertObjectToByteString(new Dictionary<string, object>
                    {
                        ["room_id"] = changedMember.ChatRoomId,
                        ["member_id"] = changedMember.MemberId,
                        ["account_id"] = accountId,
                        ["status"] = status,
                        ["device_id"] = deviceId,
                        ["timestamp"] = timestamp
                    }).ToByteArray()
                );
                packetCount++;
            }

            logger.LogDebug(
                "Broadcast chat presence update for {AccountId} to subscribers in {RoomCount} rooms",
                accountId,
                packetCount
            );
        }

        private static async Task HandleMessageRead(WebSocketPacketEvent evt, WebSocketPacket packet, EventContext ctx)
        {
            var cs = ctx.ServiceProvider.GetRequiredService<ChatService>();
            var crs = ctx.ServiceProvider.GetRequiredService<ChatRoomService>();
            var ws = ctx.ServiceProvider.GetRequiredService<RemoteWebSocketService>();
            var logger = ctx.ServiceProvider.GetRequiredService<ILogger<EventBus>>();

            if (packet.Data == null)
            {
                await SendErrorResponse(evt, "Mark message as read requires you to provide the ChatRoomId", ws);
                return;
            }

            var requestData = packet.GetData<ChatController.MarkMessageReadRequest>();
            var chatRoomId = requestData?.ChatRoomId ?? Guid.Empty;
            if (chatRoomId == Guid.Empty &&
                packet.Data is JsonElement dataElement &&
                dataElement.ValueKind == JsonValueKind.Object)
            {
                if (!TryGetGuidFromJson(dataElement, "chat_room_id", out chatRoomId))
                    _ = TryGetGuidFromJson(dataElement, "chatRoomId", out chatRoomId);
                if (chatRoomId == Guid.Empty)
                    _ = TryGetGuidFromJson(dataElement, "room_id", out chatRoomId);
            }

            if (chatRoomId == Guid.Empty)
            {
                await SendErrorResponse(evt, "Invalid request data", ws);
                return;
            }

            var sender = await crs.GetRoomMember(evt.AccountId, chatRoomId);
            if (sender == null)
            {
                await SendErrorResponse(evt, "User is not a member of the chat room.", ws);
                return;
            }

            await cs.ReadChatRoomAsync(chatRoomId, evt.AccountId);
            logger.LogDebug("Processed messages.read for account {AccountId} room {RoomId}", evt.AccountId, chatRoomId);
        }

        private static bool TryGetGuidFromJson(JsonElement obj, string propertyName, out Guid value)
        {
            value = Guid.Empty;
            if (!obj.TryGetProperty(propertyName, out var property))
                return false;

            if (property.ValueKind == JsonValueKind.String &&
                Guid.TryParse(property.GetString(), out value))
                return true;

            return false;
        }

        private static async Task HandleMessageSend(WebSocketPacketEvent evt, WebSocketPacket packet, EventContext ctx)
        {
            var db = ctx.ServiceProvider.GetRequiredService<AppDatabase>();
            var cs = ctx.ServiceProvider.GetRequiredService<ChatService>();
            var crs = ctx.ServiceProvider.GetRequiredService<ChatRoomService>();
            var files = ctx.ServiceProvider.GetRequiredService<DyFileService.DyFileServiceClient>();
            var accounts = ctx.ServiceProvider.GetRequiredService<DyAccountService.DyAccountServiceClient>();
            var paymentClient = ctx.ServiceProvider.GetRequiredService<DyPaymentService.DyPaymentServiceClient>();
            var pollClient = ctx.ServiceProvider.GetRequiredService<DyPollService.DyPollServiceClient>();
            var ws = ctx.ServiceProvider.GetRequiredService<RemoteWebSocketService>();
            var logger = ctx.ServiceProvider.GetRequiredService<ILogger<EventBus>>();

            if (packet.Data == null)
            {
                await SendErrorResponse(evt, "messages.send requires request payload.", ws);
                return;
            }

            var requestData = packet.GetData<SendMessageWsRequest>();
            if (requestData == null || requestData.ChatRoomId == Guid.Empty)
            {
                await SendErrorResponse(evt, "messages.send requires a valid chat_room_id.", ws);
                return;
            }

            requestData.Content = TextSanitizer.Sanitize(requestData.Content);

            var now = SystemClock.Instance.GetCurrentInstant();
            var member = await crs.GetRoomMember(evt.AccountId, requestData.ChatRoomId);
            if (member == null)
            {
                await SendErrorResponse(evt, "You need to be a member to send messages here.", ws);
                return;
            }

            if (member.TimeoutUntil.HasValue && member.TimeoutUntil.Value > now)
            {
                await SendErrorResponse(evt, "You has been timed out in this chat.", ws);
                return;
            }

            var e2eeMode = member.ChatRoom.EncryptionMode != ChatRoomEncryptionMode.None;
            var mlsMode = member.ChatRoom.EncryptionMode == ChatRoomEncryptionMode.E2eeMls;
            if (e2eeMode)
            {
                if (!requestData.IsEncrypted || !ChatMessageHelpers.HasEncryptedPayload(requestData))
                {
                    await SendErrorResponse(evt, "Encrypted payload is required for E2EE rooms.", ws);
                    return;
                }

                if (mlsMode && !ChatMessageHelpers.IsMlsPayloadValid(requestData))
                {
                    await SendErrorResponse(evt, "MLS rooms require scheme chat.mls.v2 and encryption_epoch.", ws);
                    return;
                }

                if (ChatMessageHelpers.LooksLikePlaintextJson(requestData.Ciphertext))
                {
                    await SendErrorResponse(evt, "Ciphertext appears to be plaintext JSON.", ws);
                    return;
                }

                if (ChatMessageHelpers.HasPlaintextFields(requestData))
                {
                    await SendErrorResponse(evt, "Plaintext fields are forbidden for E2EE rooms.", ws);
                    return;
                }
            }
            else
            {
                if (ChatMessageHelpers.IsEmptyMessage(requestData))
                {
                    await SendErrorResponse(evt, "You cannot send an empty message.", ws);
                    return;
                }
            }

            if (!e2eeMode && requestData.FundId.HasValue)
            {
                try
                {
                    var fundResponse = await paymentClient.GetWalletFundAsync(new DyGetWalletFundRequest
                    {
                        FundId = requestData.FundId.Value.ToString()
                    });

                    if (fundResponse.CreatorAccountId != member.AccountId.ToString())
                    {
                        await SendErrorResponse(evt, "You can only share funds that you created.", ws);
                        return;
                    }
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
                {
                    await SendErrorResponse(evt, "The specified fund does not exist.", ws);
                    return;
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.InvalidArgument)
                {
                    await SendErrorResponse(evt, "Invalid fund ID.", ws);
                    return;
                }
            }

            if (!e2eeMode && requestData.PollId.HasValue)
            {
                try
                {
                    _ = await pollClient.GetPollAsync(new DyGetPollRequest
                        { Id = requestData.PollId.Value.ToString() });
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
                {
                    await SendErrorResponse(evt, "The specified poll does not exist.", ws);
                    return;
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.InvalidArgument)
                {
                    await SendErrorResponse(evt, "Invalid poll ID.", ws);
                    return;
                }
            }

            var message = new SnChatMessage
            {
                Type = "text",
                SenderId = member.Id,
                ChatRoomId = requestData.ChatRoomId,
                Nonce = requestData.Nonce ?? Guid.NewGuid().ToString(),
                Meta = requestData.Meta ?? new Dictionary<string, object>(),
                IsEncrypted = requestData.IsEncrypted,
                Ciphertext = requestData.Ciphertext,
                EncryptionHeader = requestData.EncryptionHeader,
                EncryptionSignature = requestData.EncryptionSignature,
                EncryptionScheme = requestData.EncryptionScheme,
                EncryptionEpoch = requestData.EncryptionEpoch,
                EncryptionMessageType = requestData.IsEncrypted
                    ? ChatMessageHelpers.NormalizeEncryptionMessageType(requestData.EncryptionMessageType, "text")
                    : null,
                ClientMessageId = requestData.ClientMessageId
            };

            if (!e2eeMode && requestData.FundId.HasValue)
            {
                var fundEmbed = new FundEmbed { Id = requestData.FundId.Value };
                ChatMessageHelpers.AddEmbedToMessage(message, fundEmbed);
            }

            if (!e2eeMode && requestData.PollId.HasValue)
            {
                var pollResponse = await pollClient.GetPollAsync(new DyGetPollRequest
                {
                    Id = requestData.PollId.Value.ToString()
                });
                var pollEmbed = new PollEmbed { Id = Guid.Parse(pollResponse.Id) };
                ChatMessageHelpers.AddEmbedToMessage(message, pollEmbed);
            }

            if (!e2eeMode && requestData.MeetId.HasValue)
            {
                var meetEmbed = new MeetEmbed { Id = requestData.MeetId.Value };
                ChatMessageHelpers.AddEmbedToMessage(message, meetEmbed);
            }

            if (!e2eeMode && ChatMessageHelpers.HasLocationPayload(requestData.LocationName, requestData.LocationAddress, requestData.LocationWkt))
            {
                if (!ChatMessageHelpers.TryParseLocation(requestData.LocationWkt, out var location, out var locationError))
                {
                    await SendErrorResponse(evt, locationError ?? "Invalid location WKT.", ws);
                    return;
                }
                var locationEmbed = ChatMessageHelpers.CreateLocationEmbed(requestData.LocationName, requestData.LocationAddress, location);
                ChatMessageHelpers.AddEmbedToMessage(message, locationEmbed);
            }

            if (!e2eeMode && requestData.Content is not null)
                message.Content = requestData.Content;

            if (requestData.AttachmentsId is not null)
            {
                if (e2eeMode)
                {
                    message.Meta ??= new Dictionary<string, object>();
                    message.Meta["attachments_id"] = requestData.AttachmentsId.Distinct().ToList();
                    message.Attachments = [];
                }
                else
                {
                    var queryRequest = new DyGetFileBatchRequest();
                    queryRequest.Ids.AddRange(requestData.AttachmentsId);
                    var queryResponse = await files.GetFileBatchAsync(queryRequest);
                    message.Attachments = queryResponse.Files
                        .OrderBy(f => requestData.AttachmentsId.IndexOf(f.Id))
                        .Select(SnCloudFileReferenceObject.FromProtoValue)
                        .ToList();
                }
            }

            if (requestData.RepliedMessageId.HasValue)
            {
                var repliedMessage = await db.ChatMessages
                    .FirstOrDefaultAsync(m =>
                        m.Id == requestData.RepliedMessageId.Value && m.ChatRoomId == requestData.ChatRoomId);
                if (repliedMessage == null)
                {
                    await SendErrorResponse(evt, "The message you're replying to does not exist.", ws);
                    return;
                }

                message.RepliedMessageId = repliedMessage.Id;
            }

            if (requestData.ForwardedMessageId.HasValue)
            {
                var forwardedMessage = await db.ChatMessages
                    .FirstOrDefaultAsync(m => m.Id == requestData.ForwardedMessageId.Value);
                if (forwardedMessage == null)
                {
                    await SendErrorResponse(evt, "The message you're forwarding does not exist.", ws);
                    return;
                }

                message.ForwardedMessageId = forwardedMessage.Id;
            }

            if (!e2eeMode)
            {
                message.MembersMentioned = await ChatMessageHelpers.ExtractMentionedUsersAsync(
                    requestData.Content,
                    requestData.RepliedMessageId,
                    requestData.ForwardedMessageId,
                    requestData.ChatRoomId,
                    null,
                    db,
                    accounts
                );
            }

            try
            {
                var result = await cs.SendMessageAsync(message, member, member.ChatRoom);

                await ws.PushWebSocketPacketToDevice(
                    evt.DeviceId,
                    "messages.delivered",
                    InfraObjectCoder.ConvertObjectToByteString(result).ToByteArray()
                );
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send websocket message for account {AccountId}", evt.AccountId);
                await SendErrorResponse(evt, "Failed to send message.", ws);
            }
        }

        private static async Task HandleMessageTyping(WebSocketPacketEvent evt, WebSocketPacket packet,
            EventContext ctx)
        {
            var crs = ctx.ServiceProvider.GetRequiredService<ChatRoomService>();
            var ws = ctx.ServiceProvider.GetRequiredService<RemoteWebSocketService>();

            if (packet.Data == null)
            {
                await SendErrorResponse(evt, "messages.typing requires you to provide the ChatRoomId", ws);
                return;
            }

            var requestData = packet.GetData<Chat.ChatController.ChatRoomWsUniversalRequest>();
            if (requestData == null)
            {
                await SendErrorResponse(evt, "Invalid request data", ws);
                return;
            }

            var sender = await crs.GetRoomMember(evt.AccountId, requestData.ChatRoomId);
            if (sender == null)
            {
                await SendErrorResponse(evt, "User is not a member of the chat room.", ws);
                return;
            }

            // Broadcast typing indicator to subscribed room members only
            var subscribedMemberIds = await crs.GetSubscribedMembers(requestData.ChatRoomId);
            var roomMembers = await crs.ListRoomMembers(requestData.ChatRoomId);

            // Filter to subscribed members excluding the current user
            var subscribedMembers = roomMembers
                .Where(m => subscribedMemberIds.Contains(m.Id) && m.AccountId != evt.AccountId)
                .Select(m => m.AccountId.ToString())
                .ToList();

            if (subscribedMembers.Count > 0)
            {
                await ws.PushWebSocketPacketToUsers(
                    subscribedMembers,
                    "messages.typing",
                    InfraObjectCoder.ConvertObjectToByteString(new Dictionary<string, object>
                    {
                        ["room_id"] = sender.ChatRoomId,
                        ["sender_id"] = sender.Id,
                        ["sender"] = sender
                    }).ToByteArray()
                );
            }
        }

        private static async Task HandleMessageSubscribe(WebSocketPacketEvent evt, WebSocketPacket packet,
            EventContext ctx)
        {
            var crs = ctx.ServiceProvider.GetRequiredService<ChatRoomService>();
            var ws = ctx.ServiceProvider.GetRequiredService<RemoteWebSocketService>();

            if (packet.Data == null)
            {
                await SendErrorResponse(evt, "messages.subscribe requires you to provide the ChatRoomId", ws);
                return;
            }

            var requestData = packet.GetData<ChatController.ChatRoomWsUniversalRequest>();
            if (requestData == null)
            {
                await SendErrorResponse(evt, "Invalid request data", ws);
                return;
            }

            var sender = await crs.GetRoomMember(evt.AccountId, requestData.ChatRoomId);
            if (sender == null)
            {
                await SendErrorResponse(evt, "User is not a member of the chat room.", ws);
                return;
            }

            await crs.SubscribeChatRoom(sender, evt.DeviceId);
        }

        private static async Task HandleMessageUnsubscribe(WebSocketPacketEvent evt, WebSocketPacket packet,
            EventContext ctx)
        {
            var crs = ctx.ServiceProvider.GetRequiredService<ChatRoomService>();
            var ws = ctx.ServiceProvider.GetRequiredService<RemoteWebSocketService>();

            if (packet.Data == null)
            {
                await SendErrorResponse(evt, "messages.unsubscribe requires you to provide the ChatRoomId", ws);
                return;
            }

            var requestData = packet.GetData<ChatController.ChatRoomWsUniversalRequest>();
            if (requestData == null)
            {
                await SendErrorResponse(evt, "Invalid request data", ws);
                return;
            }

            var sender = await crs.GetRoomMember(evt.AccountId, requestData.ChatRoomId);
            if (sender == null)
            {
                await SendErrorResponse(evt, "User is not a member of the chat room.", ws);
                return;
            }

            await crs.UnsubscribeChatRoom(sender, evt.DeviceId);
        }

        private static async Task HandleMessageTest(WebSocketPacketEvent evt, WebSocketPacket packet, EventContext ctx)
        {
            var ws = ctx.ServiceProvider.GetRequiredService<RemoteWebSocketService>();
            var logger = ctx.ServiceProvider.GetRequiredService<ILogger<RemoteWebSocketService>>();

            // Send back the original payload to the sender
            var payload = packet.Data ?? new Dictionary<string, object>();
            var payloadBytes = InfraObjectCoder.ConvertObjectToByteString(payload).ToByteArray();
            logger.LogInformation("Received test message: {payload}", payloadBytes);
            
            await ws.PushWebSocketPacketToDevice(
                evt.DeviceId,
                "messages.test",
                payloadBytes
            );
        }

        private static async Task SendErrorResponse(
            WebSocketPacketEvent evt,
            string message,
            RemoteWebSocketService ws
        )
        {
            await ws.PushWebSocketPacketToDevice(evt.DeviceId, "error", System.Text.Encoding.UTF8.GetBytes(message), message);
        }
    }
}

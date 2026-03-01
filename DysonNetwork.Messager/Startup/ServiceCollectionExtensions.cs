using System.Globalization;
using System.Text.RegularExpressions;
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
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Queue;
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
                        }
                    }
                )
                .AddListener<AccountStatusUpdatedEvent>(
                    AccountStatusUpdatedEvent.Type,
                    async (evt, ctx) =>
                    {
                        var logger = ctx.ServiceProvider.GetRequiredService<ILogger<EventBus>>();
                        var db = ctx.ServiceProvider.GetRequiredService<AppDatabase>();
                        var chatRoomService = ctx.ServiceProvider.GetRequiredService<ChatRoomService>();
                        var pusher = ctx.ServiceProvider.GetRequiredService<DyRingService.DyRingServiceClient>();

                        logger.LogInformation("Account status updated: {AccountId}", evt.AccountId);

                        // Get user's joined chat rooms
                        var userRooms = await db.ChatMembers
                            .Where(m => m.AccountId == evt.AccountId && m.JoinedAt != null && m.LeaveAt == null)
                            .Select(m => m.ChatRoomId)
                            .ToListAsync(ctx.CancellationToken);

                        // Send WebSocket packet to subscribed users per room
                        foreach (var roomId in userRooms)
                        {
                            var members = await chatRoomService.ListRoomMembers(roomId);
                            var subscribedMemberIds = await chatRoomService.GetSubscribedMembers(roomId);
                            var subscribedUsers = members
                                .Where(m => subscribedMemberIds.Contains(m.Id))
                                .Select(m => m.AccountId.ToString())
                                .ToList();

                            if (subscribedUsers.Count == 0) continue;

                            var packet = new WebSocketPacket
                            {
                                Type = "accounts.status.update",
                                Data = new Dictionary<string, object>
                                {
                                    ["status"] = evt.Status,
                                    ["chat_room_id"] = roomId
                                }
                            };

                            var request = new DyPushWebSocketPacketToUsersRequest
                            {
                                Packet = packet.ToProtoValue()
                            };
                            request.UserIds.AddRange(subscribedUsers);

                            await pusher.PushWebSocketPacketToUsersAsync(request);

                            logger.LogInformation("Sent status update for room {roomId} to {count} subscribed users",
                                roomId,
                                subscribedUsers.Count);
                        }
                    }
                );

            return services;
        }

        private static async Task HandleMessageRead(WebSocketPacketEvent evt, WebSocketPacket packet, EventContext ctx)
        {
            var cs = ctx.ServiceProvider.GetRequiredService<ChatService>();
            var crs = ctx.ServiceProvider.GetRequiredService<ChatRoomService>();
            var pusher = ctx.ServiceProvider.GetRequiredService<DyRingService.DyRingServiceClient>();

            if (packet.Data == null)
            {
                await SendErrorResponse(evt, "Mark message as read requires you to provide the ChatRoomId", pusher);
                return;
            }

            var requestData = packet.GetData<ChatController.MarkMessageReadRequest>();
            if (requestData == null)
            {
                await SendErrorResponse(evt, "Invalid request data", pusher);
                return;
            }

            var sender = await crs.GetRoomMember(evt.AccountId, requestData.ChatRoomId);
            if (sender == null)
            {
                await SendErrorResponse(evt, "User is not a member of the chat room.", pusher);
                return;
            }

            await cs.ReadChatRoomAsync(requestData.ChatRoomId, evt.AccountId);
        }

        private static bool HasEncryptedPayload(ChatController.SendMessageRequest request)
        {
            return request.Ciphertext is { Length: > 0 } &&
                   !string.IsNullOrWhiteSpace(request.EncryptionScheme) &&
                   !string.IsNullOrWhiteSpace(request.EncryptionMessageType);
        }

        private static bool LooksLikePlaintextJson(byte[]? payload)
        {
            if (payload is not { Length: > 1 }) return false;
            var text = System.Text.Encoding.UTF8.GetString(payload).Trim();
            if (!(text.StartsWith("{") && text.EndsWith("}"))) return false;
            return text.Contains("\"content\"", StringComparison.OrdinalIgnoreCase)
                   || text.Contains("\"attachments_id\"", StringComparison.OrdinalIgnoreCase)
                   || text.Contains("\"nonce\"", StringComparison.OrdinalIgnoreCase);
        }

        private static async Task<List<Guid>> ExtractMentionedUsersAsync(
            string? content,
            Guid? repliedMessageId,
            Guid? forwardedMessageId,
            Guid roomId,
            Guid? excludeSenderId,
            AppDatabase db,
            DyAccountService.DyAccountServiceClient accounts
        )
        {
            var mentionedUsers = new List<Guid>();

            if (repliedMessageId.HasValue)
            {
                var replyingTo = await db.ChatMessages
                    .Where(m => m.Id == repliedMessageId.Value && m.ChatRoomId == roomId)
                    .Include(m => m.Sender)
                    .Select(m => m.Sender)
                    .FirstOrDefaultAsync();
                if (replyingTo != null)
                    mentionedUsers.Add(replyingTo.AccountId);
            }

            if (forwardedMessageId.HasValue)
            {
                var forwardedMessage = await db.ChatMessages
                    .Where(m => m.Id == forwardedMessageId.Value)
                    .Select(m => new { m.SenderId })
                    .FirstOrDefaultAsync();
                if (forwardedMessage != null)
                {
                    mentionedUsers.Add(forwardedMessage.SenderId);
                }
            }

            if (!string.IsNullOrWhiteSpace(content))
            {
                var mentionedNames = Regex
                    .Matches(content, @"@(?:u/)?([A-Za-z0-9_-]+)")
                    .Select(m => m.Groups[1].Value)
                    .Distinct()
                    .ToList();

                if (mentionedNames.Count > 0)
                {
                    var queryRequest = new DyLookupAccountBatchRequest();
                    queryRequest.Names.AddRange(mentionedNames);
                    var queryResponse = (await accounts.LookupAccountBatchAsync(queryRequest)).Accounts;
                    var mentionedIds = queryResponse.Select(a => Guid.Parse(a.Id)).ToList();

                    if (mentionedIds.Count > 0)
                    {
                        var mentionedMembers = await db.ChatMembers
                            .Where(m => m.ChatRoomId == roomId && mentionedIds.Contains(m.AccountId))
                            .Where(m => m.JoinedAt != null && m.LeaveAt == null)
                            .Where(m => excludeSenderId == null || m.AccountId != excludeSenderId.Value)
                            .Select(m => m.AccountId)
                            .ToListAsync();
                        mentionedUsers.AddRange(mentionedMembers);
                    }
                }
            }

            return mentionedUsers.Distinct().ToList();
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
            var pusher = ctx.ServiceProvider.GetRequiredService<DyRingService.DyRingServiceClient>();
            var logger = ctx.ServiceProvider.GetRequiredService<ILogger<EventBus>>();

            if (packet.Data == null)
            {
                await SendErrorResponse(evt, "messages.send requires request payload.", pusher);
                return;
            }

            var requestData = packet.GetData<SendMessageWsRequest>();
            if (requestData == null || requestData.ChatRoomId == Guid.Empty)
            {
                await SendErrorResponse(evt, "messages.send requires a valid chat_room_id.", pusher);
                return;
            }

            requestData.Content = TextSanitizer.Sanitize(requestData.Content);

            var now = SystemClock.Instance.GetCurrentInstant();
            var member = await crs.GetRoomMember(evt.AccountId, requestData.ChatRoomId);
            if (member == null)
            {
                await SendErrorResponse(evt, "You need to be a member to send messages here.", pusher);
                return;
            }

            if (member.TimeoutUntil.HasValue && member.TimeoutUntil.Value > now)
            {
                await SendErrorResponse(evt, "You has been timed out in this chat.", pusher);
                return;
            }

            var e2eeMode = member.ChatRoom.EncryptionMode != ChatRoomEncryptionMode.None;
            if (e2eeMode)
            {
                if (!requestData.IsEncrypted || !HasEncryptedPayload(requestData))
                {
                    await SendErrorResponse(evt, "Encrypted payload is required for E2EE rooms.", pusher);
                    return;
                }

                if (LooksLikePlaintextJson(requestData.Ciphertext))
                {
                    await SendErrorResponse(evt, "Ciphertext appears to be plaintext JSON.", pusher);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(requestData.Content) ||
                    (requestData.AttachmentsId is { Count: > 0 }) ||
                    requestData.FundId.HasValue ||
                    requestData.PollId.HasValue ||
                    requestData.RepliedMessageId.HasValue ||
                    requestData.ForwardedMessageId.HasValue)
                {
                    await SendErrorResponse(evt, "Plaintext fields are forbidden for E2EE rooms.", pusher);
                    return;
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(requestData.Content) &&
                    (requestData.AttachmentsId == null || requestData.AttachmentsId.Count == 0) &&
                    !requestData.FundId.HasValue &&
                    !requestData.PollId.HasValue)
                {
                    await SendErrorResponse(evt, "You cannot send an empty message.", pusher);
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
                        await SendErrorResponse(evt, "You can only share funds that you created.", pusher);
                        return;
                    }
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
                {
                    await SendErrorResponse(evt, "The specified fund does not exist.", pusher);
                    return;
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.InvalidArgument)
                {
                    await SendErrorResponse(evt, "Invalid fund ID.", pusher);
                    return;
                }
            }

            if (!e2eeMode && requestData.PollId.HasValue)
            {
                try
                {
                    _ = await pollClient.GetPollAsync(new DyGetPollRequest { Id = requestData.PollId.Value.ToString() });
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
                {
                    await SendErrorResponse(evt, "The specified poll does not exist.", pusher);
                    return;
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.InvalidArgument)
                {
                    await SendErrorResponse(evt, "Invalid poll ID.", pusher);
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
                EncryptionMessageType = requestData.EncryptionMessageType ??
                                        (requestData.IsEncrypted ? "content.new" : null),
                ClientMessageId = requestData.ClientMessageId
            };

            if (!e2eeMode && requestData.FundId.HasValue)
            {
                var fundEmbed = new FundEmbed { Id = requestData.FundId.Value };
                message.Meta ??= new Dictionary<string, object>();
                if (!message.Meta.TryGetValue("embeds", out var existingEmbeds)
                    || existingEmbeds is not List<EmbeddableBase>)
                    message.Meta["embeds"] = new List<Dictionary<string, object>>();

                var embeds = (List<Dictionary<string, object>>)message.Meta["embeds"];
                embeds.Add(EmbeddableBase.ToDictionary(fundEmbed));
                message.Meta["embeds"] = embeds;
            }

            if (!e2eeMode && requestData.PollId.HasValue)
            {
                var pollResponse = await pollClient.GetPollAsync(new DyGetPollRequest
                {
                    Id = requestData.PollId.Value.ToString()
                });
                var pollEmbed = new PollEmbed { Id = Guid.Parse(pollResponse.Id) };

                message.Meta ??= new Dictionary<string, object>();
                if (!message.Meta.TryGetValue("embeds", out var existingEmbeds)
                    || existingEmbeds is not List<EmbeddableBase>)
                    message.Meta["embeds"] = new List<Dictionary<string, object>>();

                var embeds = (List<Dictionary<string, object>>)message.Meta["embeds"];
                embeds.Add(EmbeddableBase.ToDictionary(pollEmbed));
                message.Meta["embeds"] = embeds;
            }

            if (!e2eeMode && requestData.Content is not null)
                message.Content = requestData.Content;

            if (!e2eeMode && requestData.AttachmentsId is not null)
            {
                var queryRequest = new DyGetFileBatchRequest();
                queryRequest.Ids.AddRange(requestData.AttachmentsId);
                var queryResponse = await files.GetFileBatchAsync(queryRequest);
                message.Attachments = queryResponse.Files
                    .OrderBy(f => requestData.AttachmentsId.IndexOf(f.Id))
                    .Select(SnCloudFileReferenceObject.FromProtoValue)
                    .ToList();
            }

            if (!e2eeMode && requestData.RepliedMessageId.HasValue)
            {
                var repliedMessage = await db.ChatMessages
                    .FirstOrDefaultAsync(m =>
                        m.Id == requestData.RepliedMessageId.Value && m.ChatRoomId == requestData.ChatRoomId);
                if (repliedMessage == null)
                {
                    await SendErrorResponse(evt, "The message you're replying to does not exist.", pusher);
                    return;
                }

                message.RepliedMessageId = repliedMessage.Id;
            }

            if (!e2eeMode && requestData.ForwardedMessageId.HasValue)
            {
                var forwardedMessage = await db.ChatMessages
                    .FirstOrDefaultAsync(m => m.Id == requestData.ForwardedMessageId.Value);
                if (forwardedMessage == null)
                {
                    await SendErrorResponse(evt, "The message you're forwarding does not exist.", pusher);
                    return;
                }

                message.ForwardedMessageId = forwardedMessage.Id;
            }

            if (!e2eeMode)
            {
                message.MembersMentioned = await ExtractMentionedUsersAsync(
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

                await pusher.PushWebSocketPacketToDeviceAsync(new DyPushWebSocketPacketToDeviceRequest
                {
                    DeviceId = evt.DeviceId,
                    Packet = new WebSocketPacket
                    {
                        Type = "messages.delivered",
                        Data = result
                    }.ToProtoValue()
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send websocket message for account {AccountId}", evt.AccountId);
                await SendErrorResponse(evt, "Failed to send message.", pusher);
            }
        }

        private static async Task HandleMessageTyping(WebSocketPacketEvent evt, WebSocketPacket packet,
            EventContext ctx)
        {
            var crs = ctx.ServiceProvider.GetRequiredService<ChatRoomService>();
            var pusher = ctx.ServiceProvider.GetRequiredService<DyRingService.DyRingServiceClient>();

            if (packet.Data == null)
            {
                await SendErrorResponse(evt, "messages.typing requires you to provide the ChatRoomId", pusher);
                return;
            }

            var requestData = packet.GetData<Chat.ChatController.ChatRoomWsUniversalRequest>();
            if (requestData == null)
            {
                await SendErrorResponse(evt, "Invalid request data", pusher);
                return;
            }

            var sender = await crs.GetRoomMember(evt.AccountId, requestData.ChatRoomId);
            if (sender == null)
            {
                await SendErrorResponse(evt, "User is not a member of the chat room.", pusher);
                return;
            }

            var responsePacket = new WebSocketPacket
            {
                Type = "messages.typing",
                Data = new
                {
                    room_id = sender.ChatRoomId,
                    sender_id = sender.Id,
                    sender
                }
            };

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
                var respRequest = new DyPushWebSocketPacketToUsersRequest { Packet = responsePacket.ToProtoValue() };
                respRequest.UserIds.AddRange(subscribedMembers);
                await pusher.PushWebSocketPacketToUsersAsync(respRequest);
            }
        }

        private static async Task HandleMessageSubscribe(WebSocketPacketEvent evt, WebSocketPacket packet,
            EventContext ctx)
        {
            var crs = ctx.ServiceProvider.GetRequiredService<ChatRoomService>();
            var pusher = ctx.ServiceProvider.GetRequiredService<DyRingService.DyRingServiceClient>();

            if (packet.Data == null)
            {
                await SendErrorResponse(evt, "messages.subscribe requires you to provide the ChatRoomId", pusher);
                return;
            }

            var requestData = packet.GetData<Chat.ChatController.ChatRoomWsUniversalRequest>();
            if (requestData == null)
            {
                await SendErrorResponse(evt, "Invalid request data", pusher);
                return;
            }

            var sender = await crs.GetRoomMember(evt.AccountId, requestData.ChatRoomId);
            if (sender == null)
            {
                await SendErrorResponse(evt, "User is not a member of the chat room.", pusher);
                return;
            }

            await crs.SubscribeChatRoom(sender);
        }

        private static async Task HandleMessageUnsubscribe(WebSocketPacketEvent evt, WebSocketPacket packet,
            EventContext ctx)
        {
            var crs = ctx.ServiceProvider.GetRequiredService<ChatRoomService>();
            var pusher = ctx.ServiceProvider.GetRequiredService<DyRingService.DyRingServiceClient>();

            if (packet.Data == null)
            {
                await SendErrorResponse(evt, "messages.unsubscribe requires you to provide the ChatRoomId", pusher);
                return;
            }

            var requestData = packet.GetData<Chat.ChatController.ChatRoomWsUniversalRequest>();
            if (requestData == null)
            {
                await SendErrorResponse(evt, "Invalid request data", pusher);
                return;
            }

            var sender = await crs.GetRoomMember(evt.AccountId, requestData.ChatRoomId);
            if (sender == null)
            {
                await SendErrorResponse(evt, "User is not a member of the chat room.", pusher);
                return;
            }

            await crs.UnsubscribeChatRoom(sender);
        }

        private static async Task SendErrorResponse(WebSocketPacketEvent evt, string message,
            DyRingService.DyRingServiceClient pusher)
        {
            await pusher.PushWebSocketPacketToDeviceAsync(new DyPushWebSocketPacketToDeviceRequest
            {
                DeviceId = evt.DeviceId,
                Packet = new WebSocketPacket
                {
                    Type = "error",
                    ErrorMessage = message
                }.ToProtoValue()
            });
        }
    }
}

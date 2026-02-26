using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using DysonNetwork.Messager.Chat;
using DysonNetwork.Messager.Chat.Realtime;
using DysonNetwork.Shared.EventBus;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Queue;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;
using WebSocketPacket = DysonNetwork.Shared.Models.WebSocketPacket;

namespace DysonNetwork.Messager.Startup;

public static class ServiceCollectionExtensions
{
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
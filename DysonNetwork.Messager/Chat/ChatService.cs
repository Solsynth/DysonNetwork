using System.Text.RegularExpressions;
using System.Text.Json;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Messager.Chat.Realtime;
using DysonNetwork.Messager.Chat.Voice;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Models.Embed;
using DysonNetwork.Shared.Registry;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Messager.Chat;

public partial class ChatService(
    AppDatabase db,
    ChatRoomService crs,
    IServiceScopeFactory scopeFactory,
    IRealtimeService realtime,
    ChatVoiceService voice,
    ILogger<ChatService> logger,
    RemoteWebReaderService webReader
)
{
    private static bool IsUserEncryptedMessage(SnChatMessage message)
    {
        if (!message.IsEncrypted) return false;
        return message.Type is not ("messages.update" or "messages.delete" or "messages.update.links" or
            WebSocketPacketType.MessageReactionAdded or WebSocketPacketType.MessageReactionRemoved) &&
               !message.Type.StartsWith("system.");
    }

    [GeneratedRegex(@"https?://(?!.*\.\w{1,6}(?:[#?]|$))[^\s]+", RegexOptions.IgnoreCase)]
    private static partial Regex GetLinkRegex();

    /// <summary>
    /// Process link previews for a message in the background
    /// This method is designed to be called from a background task
    /// </summary>
    /// <param name="message">The message to process link previews for</param>
    private async Task CreateLinkPreviewBackgroundAsync(SnChatMessage message)
    {
        try
        {
            // Create a new scope for database operations
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDatabase>();

            // Preview the links in the message
            var updatedMessage = await CreateLinkPreviewAsync(message);

            // If embeds were added, update the message in the database
            if (updatedMessage.Meta != null &&
                updatedMessage.Meta.TryGetValue("embeds", out var embeds) &&
                embeds is List<Dictionary<string, object>> { Count: > 0 } embedsList)
            {
                // Get a fresh copy of the message from the database
                var dbMessage = await dbContext.ChatMessages
                    .Where(m => m.Id == message.Id)
                    .Include(m => m.Sender)
                    .Include(m => m.ChatRoom)
                    .FirstOrDefaultAsync();
                if (dbMessage != null)
                {
                    // Update the meta field with the new embeds
                    dbMessage.Meta ??= new Dictionary<string, object>();
                    dbMessage.Meta["embeds"] = embedsList;

                    // Save changes to the database
                    dbContext.Update(dbMessage);
                    await dbContext.SaveChangesAsync();

                    logger.LogDebug($"Updated message {message.Id} with {embedsList.Count} link previews");

                    // Create and store sync message for link preview update
                    var syncMessage = new SnChatMessage
                    {
                        Type = "messages.update.links",
                        ChatRoomId = dbMessage.ChatRoomId,
                        SenderId = dbMessage.SenderId,
                        Nonce = Guid.NewGuid().ToString(),
                        Meta = new Dictionary<string, object>
                        {
                            ["message_id"] = dbMessage.Id,
                            ["embeds"] = embedsList
                        },
                        CreatedAt = dbMessage.UpdatedAt,
                        UpdatedAt = dbMessage.UpdatedAt
                    };

                    dbContext.ChatMessages.Add(syncMessage);
                    await dbContext.SaveChangesAsync();

                    // Send sync message to clients
                    syncMessage.Sender = dbMessage.Sender;
                    syncMessage.ChatRoom = dbMessage.ChatRoom;

                    using var syncScope = scopeFactory.CreateScope();

                    await DeliverMessageAsync(
                        syncMessage,
                        syncMessage.Sender,
                        syncMessage.ChatRoom,
                        notify: false
                    );
                }
            }
        }
        catch (Exception ex)
        {
            // Log errors but don't rethrow - this is a background task
            logger.LogError($"Error processing link previews for message {message.Id}: {ex.Message} {ex.StackTrace}");
        }
    }

    /// <summary>
    /// Processes a message to find and preview links in its content
    /// </summary>
    /// <param name="message">The message to process</param>
    /// <param name="webReader">The web reader service</param>
    /// <returns>The message with link previews added to its meta data</returns>
    public async Task<SnChatMessage> CreateLinkPreviewAsync(SnChatMessage message)
    {
        if (string.IsNullOrEmpty(message.Content))
            return message;

        // Find all URLs in the content
        var matches = GetLinkRegex().Matches(message.Content);

        if (matches.Count == 0)
            return message;

        // Initialize meta dictionary if null
        message.Meta ??= new Dictionary<string, object>();

        // Initialize the embeds' array if it doesn't exist
        if (!message.Meta.TryGetValue("embeds", out var existingEmbeds) ||
            existingEmbeds is not List<Dictionary<string, object>>)
        {
            message.Meta["embeds"] = new List<Dictionary<string, object>>();
        }

        var embeds = (List<Dictionary<string, object>>)message.Meta["embeds"];

        // Process up to 3 links to avoid excessive processing
        var processedLinks = 0;
        foreach (Match match in matches)
        {
            if (processedLinks >= 3)
                break;

            var url = match.Value;

            try
            {
                // Check if this URL is already in the embed list
                var urlAlreadyEmbedded = embeds.Any(e =>
                    e.TryGetValue("Url", out var originalUrl) && (string)originalUrl == url);
                if (urlAlreadyEmbedded)
                    continue;

                // Preview the link
                var linkEmbed = await webReader.GetLinkPreview(url);
                embeds.Add(EmbeddableBase.ToDictionary(linkEmbed));
                processedLinks++;
            }
            catch
            {
                // ignored
            }
        }

        message.Meta["embeds"] = embeds;

        return message;
    }

    private async Task DeliverWebSocketMessage(
        SnChatMessage message,
        string type,
        List<SnChatMember> members,
        IServiceScope scope
    )
    {
        var scopedNty = scope.ServiceProvider.GetRequiredService<DyRingService.DyRingServiceClient>();

        var request = new DyPushWebSocketPacketToUsersRequest
        {
            Packet = new DyWebSocketPacket
            {
                Type = type,
                Data = InfraObjectCoder.ConvertObjectToByteString(message),
            },
        };
        request.UserIds.AddRange(members.Select(a => a.Account).Where(a => a is not null)
            .Select(a => a!.Id.ToString()));
        await scopedNty.PushWebSocketPacketToUsersAsync(request);

        logger.LogInformation($"Delivered message to {request.UserIds.Count} accounts.");
    }

    public async Task<SnChatMessage> SendMessageAsync(SnChatMessage message, SnChatMember sender, SnChatRoom room)
    {
        if (string.IsNullOrWhiteSpace(message.Nonce)) message.Nonce = Guid.NewGuid().ToString();

        // First complete the save operation
        db.ChatMessages.Add(message);
        await db.SaveChangesAsync();


        // Copy the value to ensure the delivery is correct
        message.Sender = sender;
        message.ChatRoom = room;

        // Then start the delivery process
        var localMessage = message;
        var localSender = sender;
        var localRoom = room;
        var localLogger = logger;
        _ = Task.Run(async () =>
        {
            try
            {
                await DeliverMessageAsync(localMessage, localSender, localRoom);
            }
            catch (Exception ex)
            {
                localLogger.LogError($"Error when delivering message: {ex.Message} {ex.StackTrace}");
            }
        });

        // Process link preview in the background to avoid delaying message sending
        if (!message.IsEncrypted && message.Type == "text")
        {
            var localMessageForPreview = message;
            _ = Task.Run(async () => await CreateLinkPreviewBackgroundAsync(localMessageForPreview));
        }

        return message;
    }

    public async Task<SnChatMessage> SendSystemMessageAsync(
        SnChatRoom room,
        SnChatMember sender,
        string type,
        string? content = null,
        Dictionary<string, object>? meta = null
    )
    {
        var systemMessage = new SnChatMessage
        {
            Type = type,
            ChatRoomId = room.Id,
            SenderId = sender.Id,
            Content = content,
            Meta = meta ?? new Dictionary<string, object>(),
            Nonce = Guid.NewGuid().ToString()
        };

        db.ChatMessages.Add(systemMessage);
        await db.SaveChangesAsync();

        systemMessage.Sender = sender;
        systemMessage.ChatRoom = room;

        _ = DeliverMessageAsync(
            systemMessage,
            sender,
            room,
            notify: false
        );

        return systemMessage;
    }

    public async Task<SnChatMessage> SendMemberJoinedSystemMessageAsync(SnChatRoom room, SnChatMember member)
    {
        if (member.Account is null)
            member = await crs.LoadMemberAccount(member);

        var displayName = member.Nick ?? member.Account?.Nick ?? "Someone";
        return await SendSystemMessageAsync(
            room,
            member,
            "system.member.joined",
            $"{displayName} joined the chat.",
            new Dictionary<string, object>
            {
                ["event"] = "member_joined",
                ["member_id"] = member.Id,
                ["account_id"] = member.AccountId
            }
        );
    }

    public async Task<SnChatMessage> SendMemberLeftSystemMessageAsync(
        SnChatRoom room,
        SnChatMember member,
        SnChatMember? operatorMember = null
    )
    {
        if (member.Account is null)
            member = await crs.LoadMemberAccount(member);

        var displayName = member.Nick ?? member.Account?.Nick ?? "Someone";

        string content;
        var meta = new Dictionary<string, object>
        {
            ["event"] = "member_left",
            ["member_id"] = member.Id,
            ["account_id"] = member.AccountId
        };

        if (operatorMember is not null && operatorMember.Id != member.Id)
        {
            if (operatorMember.Account is null)
                operatorMember = await crs.LoadMemberAccount(operatorMember);

            var operatorName = operatorMember.Nick ?? operatorMember.Account?.Nick ?? "Moderator";
            content = $"{displayName} was removed from the chat by {operatorName}.";
            meta["operator_member_id"] = operatorMember.Id;
            meta["operator_account_id"] = operatorMember.AccountId;
            meta["reason"] = "removed";
        }
        else
        {
            content = $"{displayName} left the chat.";
            meta["reason"] = "left";
        }

        return await SendSystemMessageAsync(
            room,
            member,
            "system.member.left",
            content,
            meta
        );
    }

    public async Task<SnChatMessage> SendChatInfoUpdatedSystemMessageAsync(
        SnChatRoom room,
        SnChatMember operatorMember,
        Dictionary<string, object> changes
    )
    {
        if (operatorMember.Account is null)
            operatorMember = await crs.LoadMemberAccount(operatorMember);

        var operatorName = operatorMember.Nick ?? operatorMember.Account?.Nick ?? "Someone";
        var changedKeys = string.Join(", ", changes.Keys.OrderBy(k => k));

        return await SendSystemMessageAsync(
            room,
            operatorMember,
            "system.chat.updated",
            $"{operatorName} updated chat info ({changedKeys}).",
            new Dictionary<string, object>
            {
                ["event"] = "chat_info_updated",
                ["changes"] = changes
            }
        );
    }

    public async Task<SnChatMessage> SendE2eeRotateRequiredSystemMessageAsync(
        SnChatRoom room,
        SnChatMember sender,
        Guid changedMemberId,
        string reason
    )
    {
        return await SendSystemMessageAsync(
            room,
            sender,
            "system.e2ee.rotate_required",
            "E2EE sender key rotation required.",
            new Dictionary<string, object>
            {
                ["event"] = "e2ee_rotate_required",
                ["room_id"] = room.Id,
                ["changed_member_id"] = changedMemberId,
                ["reason"] = reason,
                ["rotation_hint_epoch"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            }
        );
    }

    private async Task DeliverMessageAsync(
        SnChatMessage message,
        SnChatMember sender,
        SnChatRoom room,
        string type = WebSocketPacketType.MessageNew,
        bool notify = true
    )
    {
        message.Sender = sender;
        message.ChatRoom = room;

        using var scope = scopeFactory.CreateScope();
        var scopedCrs = scope.ServiceProvider.GetRequiredService<ChatRoomService>();

        var members = await scopedCrs.ListRoomMembers(room.Id);

        await DeliverWebSocketMessage(message, type, members, scope);

        if (notify)
            await SendPushNotificationsAsync(message, sender, room, type, members, scope);
    }

    private async Task SendPushNotificationsAsync(
        SnChatMessage message,
        SnChatMember sender,
        SnChatRoom room,
        string type,
        List<SnChatMember> members,
        IServiceScope scope
    )
    {
        var scopedCrs = scope.ServiceProvider.GetRequiredService<ChatRoomService>();
        var scopedNty = scope.ServiceProvider.GetRequiredService<DyRingService.DyRingServiceClient>();

        var roomSubject = room is { Type: ChatRoomType.DirectMessage, Name: null } ? "DM" :
            room.Realm is not null ? $"{room.Name ?? "Unknown"}, {room.Realm.Name}" : room.Name ?? "Unknown";

        if (sender.Account is null)
            sender = await scopedCrs.LoadMemberAccount(sender);
        if (sender.Account is null)
            throw new InvalidOperationException(
                "Sender account is null, this should never happen. Sender id: " +
                sender.Id
            );

        var notification = BuildNotification(message, sender, room, roomSubject, type);

        var accountsToNotify = FilterAccountsForNotification(members, message, sender);

        // Filter out subscribed users from push notifications
        var subscribedMemberIds = new List<Guid>();
        foreach (var member in members)
        {
            if (await scopedCrs.IsSubscribedChatRoom(member.ChatRoomId, member.Id))
                subscribedMemberIds.Add(member.AccountId);
        }

        accountsToNotify = accountsToNotify.Where(a => !subscribedMemberIds.Contains(Guid.Parse(a.Id))).ToList();

        logger.LogInformation("Trying to deliver message to {count} accounts...", accountsToNotify.Count);

        if (accountsToNotify.Count > 0)
        {
            var ntyRequest = new DySendPushNotificationToUsersRequest { Notification = notification };
            ntyRequest.UserIds.AddRange(accountsToNotify.Select(a => a.Id.ToString()));
            await scopedNty.SendPushNotificationToUsersAsync(ntyRequest);
        }

        logger.LogInformation("Delivered message to {count} accounts.", accountsToNotify.Count);
    }

    private async Task SendReactionNotificationAsync(
        SnChatMessage message,
        SnChatMember reactor,
        SnChatRoom room,
        bool isAdded
    )
    {
        using var scope = scopeFactory.CreateScope();
        var scopedCrs = scope.ServiceProvider.GetRequiredService<ChatRoomService>();
        var scopedNty = scope.ServiceProvider.GetRequiredService<DyRingService.DyRingServiceClient>();

        var messageAuthor = await db.ChatMembers
            .Where(m => m.Id == message.SenderId && m.ChatRoomId == room.Id)
            .FirstOrDefaultAsync();

        if (messageAuthor is null)
            return;

        if (messageAuthor.AccountId == reactor.AccountId)
            return;

        if (messageAuthor.Notify == ChatMemberNotify.None)
            return;

        var now = SystemClock.Instance.GetCurrentInstant();
        if (messageAuthor.BreakUntil is not null && messageAuthor.BreakUntil > now)
            return;

        messageAuthor = await scopedCrs.LoadMemberAccount(messageAuthor);
        if (messageAuthor.Account is null)
            return;

        var roomSubject = room is { Type: ChatRoomType.DirectMessage, Name: null } ? "DM" :
            room.Realm is not null ? $"{room.Name ?? "Unknown"}, {room.Realm.Name}" : room.Name ?? "Unknown";

        var symbol = message.Meta is not null && message.Meta.TryGetValue("symbol", out var s) ? s?.ToString() : null;
        var notification = new DyPushNotification
        {
            Topic = isAdded ? "messages.reaction.added" : "messages.reaction.removed",
            Title = $"{reactor.Nick ?? reactor.Account?.Nick} reacted to your message ({roomSubject})",
            Meta = InfraObjectCoder.ConvertObjectToByteString(new Dictionary<string, object>
            {
                ["user_id"] = reactor.AccountId,
                ["reactor_id"] = reactor.Id,
                ["reactor_name"] = reactor.Nick ?? reactor.Account?.Nick,
                ["message_id"] = message.Id,
                ["room_id"] = room.Id,
                ["symbol"] = symbol ?? ""
            }),
            ActionUri = $"/chat/{room.Id}",
            IsSavable = false,
            Body = isAdded ? $"Reacted with {symbol}" : "Removed reaction"
        };

        var ntyRequest = new DySendPushNotificationToUsersRequest { Notification = notification };
        ntyRequest.UserIds.Add(messageAuthor.AccountId.ToString());
        await scopedNty.SendPushNotificationToUsersAsync(ntyRequest);

        logger.LogInformation("Sent reaction notification to message author {AuthorId}", messageAuthor.AccountId);
    }

    private DyPushNotification BuildNotification(SnChatMessage message, SnChatMember sender, SnChatRoom room,
        string roomSubject,
        string type)
    {
        var metaDict = new Dictionary<string, object>
        {
            ["sender_name"] = sender.Nick ?? sender.Account!.Nick,
            ["user_id"] = sender.AccountId,
            ["sender_id"] = sender.Id,
            ["message_id"] = message.Id,
            ["room_id"] = room.Id,
        };

        var imageId = message.Attachments
            .Where(a => a.MimeType != null && a.MimeType.StartsWith("image"))
            .Select(a => a.Id).FirstOrDefault();
        if (imageId is not null)
            metaDict["image"] = imageId;

        if (sender.Account!.Profile is not { Picture: null })
            metaDict["pfp"] = sender.Account!.Profile.Picture.Id;
        if (!string.IsNullOrEmpty(room.Name))
            metaDict["room_name"] = room.Name;

        var notification = new DyPushNotification
        {
            Topic = "messages.new",
            Title = $"{sender.Nick ?? sender.Account.Nick} ({roomSubject})",
            Meta = InfraObjectCoder.ConvertObjectToByteString(metaDict),
            ActionUri = $"/chat/{room.Id}",
            IsSavable = false,
            Body = BuildNotificationBody(message, type)
        };

        return notification;
    }

    private string BuildNotificationBody(SnChatMessage message, string type)
    {
        if (IsUserEncryptedMessage(message))
            return "Encrypted message";

        if (message.DeletedAt is not null)
            return "Deleted a message";

        switch (message.Type)
        {
            case "call.ended":
                return "Call ended";
            case "call.start":
                return "Call begun";
            case "voice":
                return "Voice message";
            default:
                var attachmentWord = message.Attachments.Count == 1 ? "attachment" : "attachments";
                var body = !string.IsNullOrEmpty(message.Content)
                    ? message.Content[..Math.Min(message.Content.Length, 100)]
                    : $"<{message.Attachments.Count} {attachmentWord}>";

                switch (type)
                {
                    case WebSocketPacketType.MessageUpdate:
                        body += " (edited)";
                        break;
                    case WebSocketPacketType.MessageDelete:
                        body = "Deleted a message";
                        break;
                }

                return body;
        }
    }

    private static List<DyAccount> FilterAccountsForNotification(
        List<SnChatMember> members,
        SnChatMessage message,
        SnChatMember sender
    )
    {
        var now = SystemClock.Instance.GetCurrentInstant();

        var accountsToNotify = new List<DyAccount>();
        foreach (var member in members.Where(member => member.Notify != ChatMemberNotify.None))
        {
            // Skip if mentioned but not in mentions-only mode or if break is active
            if (message.MembersMentioned is null || !message.MembersMentioned.Contains(member.AccountId))
            {
                if (member.BreakUntil is not null && member.BreakUntil > now) continue;
                if (member.Notify == ChatMemberNotify.Mentions) continue;
            }

            if (member.Account is not null)
                accountsToNotify.Add(member.Account.ToProtoValue());
        }

        return accountsToNotify.Where(a => a.Id != sender.AccountId.ToString()).ToList();
    }

    /// <summary>
    /// This method will instant update the LastReadAt field for chat member,
    /// for better performance, using the flush buffer one instead
    /// </summary>
    /// <param name="roomId">The user chat room</param>
    /// <param name="userId">The user id</param>
    /// <exception cref="ArgumentException"></exception>
    public async Task ReadChatRoomAsync(Guid roomId, Guid userId)
    {
        var sender = await db.ChatMembers
            .Where(m => m.AccountId == userId && m.ChatRoomId == roomId && m.JoinedAt != null && m.LeaveAt == null)
            .FirstOrDefaultAsync();
        if (sender is null) throw new ArgumentException("User is not a member of the chat room.");

        sender.LastReadAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync();
    }

    public async Task<int> CountUnreadMessage(Guid userId, Guid chatRoomId)
    {
        var sender = await db.ChatMembers
            .Where(m => m.AccountId == userId && m.ChatRoomId == chatRoomId && m.JoinedAt != null && m.LeaveAt == null)
            .Select(m => new { m.LastReadAt })
            .FirstOrDefaultAsync();
        if (sender?.LastReadAt is null) return 0;

        return await db.ChatMessages
            .Where(m => m.ChatRoomId == chatRoomId)
            .Where(m => m.CreatedAt > sender.LastReadAt)
            .CountAsync();
    }

    public async Task<Dictionary<Guid, int>> CountUnreadMessageForUser(Guid userId)
    {
        var members = await db.ChatMembers
            .Where(m => m.LeaveAt == null && m.JoinedAt != null)
            .Where(m => m.AccountId == userId)
            .Select(m => new { m.ChatRoomId, m.LastReadAt })
            .ToListAsync();

        var lastReadAt = members.ToDictionary(m => m.ChatRoomId, m => m.LastReadAt);
        var roomsId = lastReadAt.Keys.ToList();

        return await db.ChatMessages
            .Where(m => roomsId.Contains(m.ChatRoomId))
            .GroupBy(m => m.ChatRoomId)
            .ToDictionaryAsync(
                g => g.Key,
                g => g.Count(m => lastReadAt[g.Key] == null || m.CreatedAt > lastReadAt[g.Key])
            );
    }

    public async Task<Dictionary<Guid, SnChatMessage?>> ListLastMessageForUser(Guid userId)
    {
        var userRooms = await db.ChatMembers
            .Where(m => m.LeaveAt == null && m.JoinedAt != null)
            .Where(m => m.AccountId == userId)
            .Select(m => m.ChatRoomId)
            .ToListAsync();

        var messages = await db.ChatMessages
            .IgnoreQueryFilters()
            .Include(m => m.Sender)
            .Where(m => userRooms.Contains(m.ChatRoomId))
            .GroupBy(m => m.ChatRoomId)
            .Select(g => g.OrderByDescending(m => m.CreatedAt).FirstOrDefault())
            .ToDictionaryAsync(
                m => m!.ChatRoomId,
                m => m
            );

        var messageSenders = messages
            .Select(m => m.Value!.Sender)
            .DistinctBy(x => x.Id)
            .ToList();
        messageSenders = await crs.LoadMemberAccounts(messageSenders);
        messageSenders = messageSenders.Where(x => x.Account is not null).ToList();

        // Get keys of messages to remove (where sender is not found)
        var messagesToRemove = messages
            .Where(m => messageSenders.All(s => s.Id != m.Value!.SenderId))
            .Select(m => m.Key)
            .ToList();

        // Remove messages with no sender
        foreach (var key in messagesToRemove)
            messages.Remove(key);

        // Update remaining messages with their senders
        foreach (var message in messages)
            message.Value!.Sender = messageSenders.First(x => x.Id == message.Value.SenderId);

        await HydrateMessageReactionsAsync(messages.Values.OfType<SnChatMessage>().ToList(), userId);

        return messages;
    }

    public async Task<SnRealtimeCall> CreateCallAsync(SnChatRoom room, SnChatMember sender)
    {
        var call = new SnRealtimeCall
        {
            RoomId = room.Id,
            SenderId = sender.Id,
            ProviderName = realtime.ProviderName
        };

        try
        {
            var sessionConfig = await realtime.CreateSessionAsync(room.Id, new Dictionary<string, object>
            {
                { "room_id", room.Id },
                { "user_id", sender.AccountId },
            });

            // Store session details
            call.SessionId = sessionConfig.SessionId;
            call.UpstreamConfig = sessionConfig.Parameters;
        }
        catch (Exception ex)
        {
            // Log the exception but continue with call creation
            throw new InvalidOperationException($"Failed to create {realtime.ProviderName} session: {ex.Message}");
        }

        db.ChatRealtimeCall.Add(call);
        await db.SaveChangesAsync();

        await SendMessageAsync(new SnChatMessage
        {
            Type = "call.start",
            ChatRoomId = room.Id,
            SenderId = sender.Id,
            Meta = new Dictionary<string, object>
            {
                { "call_id", call.Id },
            }
        }, sender, room);

        return call;
    }

    public async Task EndCallAsync(Guid roomId, SnChatMember sender)
    {
        var call = await GetCallOngoingAsync(roomId);
        if (call is null) throw new InvalidOperationException("No ongoing call was not found.");
        if (sender.AccountId != call.Room.AccountId && call.SenderId != sender.Id)
            throw new InvalidOperationException("You are not the call initiator either the chat room moderator.");

        // End the realtime session if it exists
        if (!string.IsNullOrEmpty(call.SessionId) && !string.IsNullOrEmpty(call.ProviderName))
        {
            try
            {
                var config = new RealtimeSessionConfig
                {
                    SessionId = call.SessionId,
                    Parameters = call.UpstreamConfig
                };

                await realtime.EndSessionAsync(call.SessionId, config);
            }
            catch (Exception ex)
            {
                // Log the exception but continue with call ending
                throw new InvalidOperationException($"Failed to end {call.ProviderName} session: {ex.Message}");
            }
        }

        call.EndedAt = SystemClock.Instance.GetCurrentInstant();
        db.ChatRealtimeCall.Update(call);
        await db.SaveChangesAsync();

        await SendMessageAsync(new SnChatMessage
        {
            Type = "call.ended",
            ChatRoomId = call.RoomId,
            SenderId = sender.Id,
            Meta = new Dictionary<string, object>
            {
                { "call_id", call.Id },
                { "duration", (call.EndedAt!.Value - call.CreatedAt).TotalSeconds }
            }
        }, call.Sender, call.Room);
    }

    public async Task<SnRealtimeCall?> GetCallOngoingAsync(Guid roomId)
    {
        return await db.ChatRealtimeCall
            .Where(c => c.RoomId == roomId)
            .Where(c => c.EndedAt == null)
            .Include(c => c.Room)
            .Include(c => c.Sender)
            .FirstOrDefaultAsync();
    }

    public async Task<SyncResponse> GetSyncDataAsync(Guid roomId, Guid? accountId, long lastSyncTimestamp, int limit = 500)
    {
        var lastSyncInstant = Instant.FromUnixTimeMilliseconds(lastSyncTimestamp);

        // Count total newer messages
        var totalCount = await db.ChatMessages
            .Where(m => m.ChatRoomId == roomId && m.CreatedAt > lastSyncInstant)
            .CountAsync();

        // Get up to limit messages that have been created since the last sync
        var syncMessages = await db.ChatMessages
            .Where(m => m.ChatRoomId == roomId)
            .Where(m => m.CreatedAt > lastSyncInstant)
            .OrderBy(m => m.CreatedAt)
            .Take(limit)
            .Include(m => m.Sender)
            .ToListAsync();

        // Load member accounts for messages that need them
        if (syncMessages.Count > 0)
        {
            var senders = syncMessages
                .Select(m => m.Sender)
                .DistinctBy(s => s.Id)
                .ToList();

            senders = await crs.LoadMemberAccounts(senders);

            // Update sender information
            foreach (var message in syncMessages)
            {
                var sender = senders.FirstOrDefault(s => s.Id == message.SenderId);
                if (sender != null)
                {
                    message.Sender = sender;
                }
            }
        }

        await HydrateMessageReactionsAsync(syncMessages, accountId);

        var latestTimestamp = syncMessages.Count > 0
            ? syncMessages.Last().CreatedAt
            : SystemClock.Instance.GetCurrentInstant();

        return new SyncResponse
        {
            Messages = syncMessages,
            CurrentTimestamp = latestTimestamp,
            TotalCount = totalCount
        };
    }


    public async Task<SnChatMessage> UpdateMessageAsync(
        SnChatMessage message,
        Dictionary<string, object>? meta = null,
        string? content = null,
        Guid? repliedMessageId = null,
        Guid? forwardedMessageId = null,
        List<string>? attachmentsId = null,
        bool? isEncrypted = null,
        byte[]? ciphertext = null,
        byte[]? encryptionHeader = null,
        byte[]? encryptionSignature = null,
        string? encryptionScheme = null,
        long? encryptionEpoch = null,
        string? encryptionMessageType = null,
        string? clientMessageId = null
    )
    {
        // Only allow editing regular text messages
        if (message.Type != "text")
            throw new InvalidOperationException("Only regular messages can be edited.");

        var prevIsEncrypted = message.IsEncrypted;
        var isContentChanged = content is not null && content != message.Content;
        var isAttachmentsChanged = attachmentsId is not null;
        var isCiphertextChanged = ciphertext is not null;
        var isEncryptedFlagChanged = isEncrypted.HasValue && isEncrypted.Value != prevIsEncrypted;

        string? prevContent = null;
        if (isContentChanged && !prevIsEncrypted)
            prevContent = message.Content;

        if (content is not null)
            message.Content = content;
        if (isEncrypted.HasValue)
            message.IsEncrypted = isEncrypted.Value;
        if (ciphertext is not null)
            message.Ciphertext = ciphertext;
        if (encryptionHeader is not null)
            message.EncryptionHeader = encryptionHeader;
        if (encryptionSignature is not null)
            message.EncryptionSignature = encryptionSignature;
        if (encryptionScheme is not null)
            message.EncryptionScheme = encryptionScheme;
        if (encryptionEpoch.HasValue)
            message.EncryptionEpoch = encryptionEpoch.Value;
        if (encryptionMessageType is not null)
            message.EncryptionMessageType = encryptionMessageType;
        if (!string.IsNullOrWhiteSpace(clientMessageId))
            message.ClientMessageId = clientMessageId;

        // Update do not override meta, replies to and forwarded to

        // Mark as edited if content or attachments changed
        if (isContentChanged || isAttachmentsChanged || isCiphertextChanged || isEncryptedFlagChanged)
            message.EditedAt = SystemClock.Instance.GetCurrentInstant();


        db.Update(message);
        await db.SaveChangesAsync();

        // Create and store sync message for the update
        var syncMessage = new SnChatMessage
        {
            Type = "messages.update",
            ChatRoomId = message.ChatRoomId,
            SenderId = message.SenderId,
            Content = message.Content,
            IsEncrypted = message.IsEncrypted,
            Ciphertext = message.Ciphertext,
            EncryptionHeader = message.EncryptionHeader,
            EncryptionSignature = message.EncryptionSignature,
            EncryptionScheme = message.EncryptionScheme,
            EncryptionEpoch = message.EncryptionEpoch,
            EncryptionMessageType = message.EncryptionMessageType ?? "content.edit",
            ClientMessageId = message.ClientMessageId,
            Attachments = message.Attachments,
            Nonce = Guid.NewGuid().ToString(),
            Meta = message.Meta != null
                ? new Dictionary<string, object>(message.Meta) { ["message_id"] = message.Id }
                : new Dictionary<string, object> { ["message_id"] = message.Id },
            CreatedAt = message.UpdatedAt,
            UpdatedAt = message.UpdatedAt
        };

        if (!message.IsEncrypted && isContentChanged && prevContent is not null)
            syncMessage.Meta["previous_content"] = prevContent;

        db.ChatMessages.Add(syncMessage);
        await db.SaveChangesAsync();

        // Process link preview in the background if content was updated
        if (!message.IsEncrypted && isContentChanged)
            _ = Task.Run(async () => await CreateLinkPreviewBackgroundAsync(message));

        if (message.Sender.Account is null)
            message.Sender = await crs.LoadMemberAccount(message.Sender);

        // Send sync message to clients
        syncMessage.Sender = message.Sender;
        syncMessage.ChatRoom = message.ChatRoom;

        _ = DeliverMessageAsync(
            syncMessage,
            syncMessage.Sender,
            syncMessage.ChatRoom,
            notify: false
        );

        return message;
    }

    /// <summary>
    /// Soft deletes a message and notifies other chat members
    /// </summary>
    /// <param name="message">The message to delete</param>
    public async Task DeleteMessageAsync(
        SnChatMessage message,
        byte[]? ciphertext = null,
        byte[]? encryptionHeader = null,
        byte[]? encryptionSignature = null,
        string? encryptionScheme = null,
        long? encryptionEpoch = null,
        string? encryptionMessageType = null,
        string? clientMessageId = null
    )
    {
        // Allow deleting user-authored text and voice messages only
        if (message.Type is not ("text" or "voice"))
        {
            throw new InvalidOperationException("Only regular text/voice messages can be deleted.");
        }

        // Soft delete by setting DeletedAt timestamp
        message.DeletedAt = SystemClock.Instance.GetCurrentInstant();
        message.UpdatedAt = message.DeletedAt.Value;

        db.Update(message);
        await db.SaveChangesAsync();

        // Best effort cleanup for voice messages.
        // Missing objects or storage failures should not break message deletion.
        if (message.Type == "voice")
            await CleanupVoiceAssetsForDeletedMessageAsync(message);

        // Create and store sync message for the deletion
        var syncMessage = new SnChatMessage
        {
            Type = "messages.delete",
            ChatRoomId = message.ChatRoomId,
            SenderId = message.SenderId,
            IsEncrypted = message.IsEncrypted,
            Ciphertext = ciphertext ?? message.Ciphertext,
            EncryptionHeader = encryptionHeader ?? message.EncryptionHeader,
            EncryptionSignature = encryptionSignature ?? message.EncryptionSignature,
            EncryptionScheme = encryptionScheme ?? message.EncryptionScheme,
            EncryptionEpoch = encryptionEpoch ?? message.EncryptionEpoch,
            EncryptionMessageType = encryptionMessageType ?? (message.IsEncrypted ? "content.delete" : null),
            ClientMessageId = clientMessageId ?? message.ClientMessageId,
            Nonce = Guid.NewGuid().ToString(),
            Meta = new Dictionary<string, object>
            {
                ["message_id"] = message.Id
            },
            CreatedAt = message.DeletedAt.Value,
            UpdatedAt = message.DeletedAt.Value
        };

        db.ChatMessages.Add(syncMessage);
        await db.SaveChangesAsync();

        // Send sync message to clients
        if (message.Sender.Account is null)
            message.Sender = await crs.LoadMemberAccount(message.Sender);

        syncMessage.Sender = message.Sender;
        syncMessage.ChatRoom = message.ChatRoom;

        await DeliverMessageAsync(
            syncMessage,
            syncMessage.Sender,
            syncMessage.ChatRoom,
            notify: false
        );
    }

    private async Task CleanupVoiceAssetsForDeletedMessageAsync(SnChatMessage message)
    {
        try
        {
            if (!TryGetMetaGuid(message.Meta, "voice_clip_id", out var clipId))
                return;

            var clip = await db.ChatVoiceClips
                .Where(v => v.Id == clipId && v.ChatRoomId == message.ChatRoomId)
                .FirstOrDefaultAsync();
            if (clip is null)
                return;

            try
            {
                await voice.DeleteVoiceObjectByKeyAsync(clip.StoragePath);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed deleting voice object for clip {ClipId}", clip.Id);
            }

            db.ChatVoiceClips.Remove(clip);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed voice asset cleanup for deleted message {MessageId}", message.Id);
        }
    }

    private static bool TryGetMetaGuid(Dictionary<string, object>? meta, string key, out Guid value)
    {
        value = Guid.Empty;
        if (meta is null || !meta.TryGetValue(key, out var raw) || raw is null)
            return false;

        if (raw is Guid guid)
        {
            value = guid;
            return true;
        }

        if (raw is string s && Guid.TryParse(s, out var fromString))
        {
            value = fromString;
            return true;
        }

        if (raw is JsonElement je &&
            je.ValueKind == JsonValueKind.String &&
            Guid.TryParse(je.GetString(), out var fromJson))
        {
            value = fromJson;
            return true;
        }

        return false;
    }

    public async Task<SnChatReaction> AddReactionAsync(
        SnChatRoom room,
        SnChatMessage message,
        SnChatReaction reaction,
        SnChatMember sender
    )
    {
        reaction.MessageId = message.Id;
        reaction.SenderId = sender.Id;

        db.ChatReactions.Add(reaction);
        
        if (message.ReactionsCount.TryGetValue(reaction.Symbol, out var count))
            message.ReactionsCount[reaction.Symbol] = count + 1;
        else
            message.ReactionsCount[reaction.Symbol] = 1;

        await db.SaveChangesAsync();
        
        var syncMessage = new SnChatMessage
        {
            Type = WebSocketPacketType.MessageReactionAdded,
            ChatRoomId = message.ChatRoomId,
            SenderId = sender.Id,
            Nonce = Guid.NewGuid().ToString(),
            Meta = new Dictionary<string, object>
            {
                ["message_id"] = message.Id,
                ["symbol"] = reaction.Symbol
            },
        };

        db.ChatMessages.Add(syncMessage);
        await db.SaveChangesAsync();

        if (sender.Account is null)
            sender = await crs.LoadMemberAccount(sender);

        syncMessage.Sender = sender;
        syncMessage.ChatRoom = room;

        await DeliverMessageAsync(
            syncMessage,
            syncMessage.Sender,
            syncMessage.ChatRoom,
            notify: false
        );

        await HydrateMessageReactionsAsync([message], sender.AccountId);

        message.Sender = sender;
        message.ChatRoom = room;
        await DeliverMessageAsync(
            message,
            sender,
            room,
            type: WebSocketPacketType.MessageUpdate,
            notify: false
        );

        _ = SendReactionNotificationAsync(message, sender, room, isAdded: true);

        return reaction;
    }

    public async Task RemoveReactionAsync(
        SnChatRoom room,
        SnChatMessage message,
        string symbol,
        SnChatMember sender
    )
    {
        var reaction = await db.ChatReactions
            .Where(r => r.MessageId == message.Id && r.SenderId == sender.Id && r.Symbol == symbol)
            .FirstOrDefaultAsync();

        if (reaction is null)
            return;

        db.ChatReactions.Remove(reaction);

        if (message.ReactionsCount.TryGetValue(symbol, out var count))
        {
            if (count > 1)
                message.ReactionsCount[symbol] = count - 1;
            else
                message.ReactionsCount.Remove(symbol);
        }

        await db.SaveChangesAsync();

        var syncMessage = new SnChatMessage
        {
            Type = WebSocketPacketType.MessageReactionRemoved,
            ChatRoomId = message.ChatRoomId,
            SenderId = sender.Id,
            Nonce = Guid.NewGuid().ToString(),
            Meta = new Dictionary<string, object>
            {
                ["message_id"] = message.Id,
                ["symbol"] = symbol
            },
        };

        db.ChatMessages.Add(syncMessage);
        await db.SaveChangesAsync();
        
        if (sender.Account is null)
            sender = await crs.LoadMemberAccount(sender);

        syncMessage.Sender = sender;
        syncMessage.ChatRoom = room;

        // Ensure both sender and chat room are not null before delivering
        if (syncMessage.Sender == null || syncMessage.ChatRoom == null)
        {
            logger.LogWarning("Cannot deliver reaction removal message: sender or chat room is null for message {MessageId}", message.Id);
            return;
        }

        await DeliverMessageAsync(
            syncMessage,
            syncMessage.Sender,
            syncMessage.ChatRoom,
            notify: false
        );

        await HydrateMessageReactionsAsync([message], sender.AccountId);

        message.Sender = sender;
        message.ChatRoom = room;
        await DeliverMessageAsync(
            message,
            sender,
            room,
            type: WebSocketPacketType.MessageUpdate,
            notify: false
        );

        _ = SendReactionNotificationAsync(message, sender, room, isAdded: false);
    }

    public async Task HydrateMessageReactionsAsync(List<SnChatMessage> messages, Guid? accountId = null)
    {
        if (messages.Count == 0)
            return;

        var messageIds = messages.Select(m => m.Id).Distinct().ToList();

        var reactionMaps = await db.ChatReactions
            .Where(r => messageIds.Contains(r.MessageId))
            .GroupBy(r => r.MessageId)
            .ToDictionaryAsync(
                g => g.Key,
                g => g.GroupBy(r => r.Symbol).ToDictionary(sg => sg.Key, sg => sg.Count())
            );

        Dictionary<Guid, Dictionary<string, bool>> reactionMadeMap = new();
        if (accountId.HasValue)
        {
            var reactionsMade = await db.ChatReactions
                .Where(r => messageIds.Contains(r.MessageId) && r.Sender.AccountId == accountId.Value)
                .Select(r => new { r.MessageId, r.Symbol })
                .ToListAsync();

            reactionMadeMap = reactionsMade
                .GroupBy(r => r.MessageId)
                .ToDictionary(
                    g => g.Key,
                    g => g.ToDictionary(r => r.Symbol, _ => true)
                );
        }

        foreach (var message in messages)
        {
            message.ReactionsCount = reactionMaps.TryGetValue(message.Id, out var reactionsCount)
                ? reactionsCount
                : new Dictionary<string, int>();

            message.ReactionsMade = accountId.HasValue
                ? reactionMadeMap.GetValueOrDefault(message.Id, [])
                : null;
        }
    }
}

public class SyncResponse
{
    public List<SnChatMessage> Messages { get; set; } = [];
    public Instant CurrentTimestamp { get; set; }
    public int TotalCount { get; set; } = 0;
}

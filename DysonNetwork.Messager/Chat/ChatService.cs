using System.Text.RegularExpressions;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Messager.Chat.Realtime;
using DysonNetwork.Shared.Models.Embed;
using DysonNetwork.Shared.Registry;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using WebSocketPacket = DysonNetwork.Shared.Proto.WebSocketPacket;

namespace DysonNetwork.Messager.Chat;

public partial class ChatService(
    AppDatabase db,
    ChatRoomService crs,
    FileService.FileServiceClient filesClient,
    FileReferenceService.FileReferenceServiceClient fileRefs,
    IServiceScopeFactory scopeFactory,
    IRealtimeService realtime,
    ILogger<ChatService> logger,
    RemoteWebReaderService webReader
)
{
    private const string ChatFileUsageIdentifier = "chat";

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
        var scopedNty = scope.ServiceProvider.GetRequiredService<RingService.RingServiceClient>();

        var request = new PushWebSocketPacketToUsersRequest
        {
            Packet = new WebSocketPacket
            {
                Type = type,
                Data = GrpcTypeHelper.ConvertObjectToByteString(message),
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

        // Create file references if message has attachments
        await CreateFileReferencesForMessageAsync(message);

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
        var localMessageForPreview = message;
        _ = Task.Run(async () => await CreateLinkPreviewBackgroundAsync(localMessageForPreview));

        return message;
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
        var scopedNty = scope.ServiceProvider.GetRequiredService<RingService.RingServiceClient>();

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
            var ntyRequest = new SendPushNotificationToUsersRequest { Notification = notification };
            ntyRequest.UserIds.AddRange(accountsToNotify.Select(a => a.Id.ToString()));
            await scopedNty.SendPushNotificationToUsersAsync(ntyRequest);
        }

        logger.LogInformation("Delivered message to {count} accounts.", accountsToNotify.Count);
    }

    private PushNotification BuildNotification(SnChatMessage message, SnChatMember sender, SnChatRoom room,
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

        var notification = new PushNotification
        {
            Topic = "messages.new",
            Title = $"{sender.Nick ?? sender.Account.Nick} ({roomSubject})",
            Meta = GrpcTypeHelper.ConvertObjectToByteString(metaDict),
            ActionUri = $"/chat/{room.Id}",
            IsSavable = false,
            Body = BuildNotificationBody(message, type)
        };

        return notification;
    }

    private string BuildNotificationBody(SnChatMessage message, string type)
    {
        if (message.DeletedAt is not null)
            return "Deleted a message";

        switch (message.Type)
        {
            case "call.ended":
                return "Call ended";
            case "call.start":
                return "Call begun";
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

    private List<Account> FilterAccountsForNotification(List<SnChatMember> members, SnChatMessage message,
        SnChatMember sender)
    {
        var now = SystemClock.Instance.GetCurrentInstant();

        var accountsToNotify = new List<Account>();
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

    private async Task CreateFileReferencesForMessageAsync(SnChatMessage message)
    {
        var files = message.Attachments.Distinct().ToList();
        if (files.Count == 0) return;

        var request = new CreateReferenceBatchRequest
        {
            Usage = ChatFileUsageIdentifier,
            ResourceId = message.ResourceIdentifier,
        };
        request.FilesId.AddRange(message.Attachments.Select(a => a.Id));
        await fileRefs.CreateReferenceBatchAsync(request);
    }

    private async Task UpdateFileReferencesForMessageAsync(SnChatMessage message, List<string> attachmentsId)
    {
        // Delete existing references for this message
        await fileRefs.DeleteResourceReferencesAsync(
            new DeleteResourceReferencesRequest { ResourceId = message.ResourceIdentifier }
        );

        // Create new references for each attachment
        var createRequest = new CreateReferenceBatchRequest
        {
            Usage = ChatFileUsageIdentifier,
            ResourceId = message.ResourceIdentifier,
        };
        createRequest.FilesId.AddRange(attachmentsId);
        await fileRefs.CreateReferenceBatchAsync(createRequest);

        // Update message attachments by getting files from database
        var queryRequest = new GetFileBatchRequest();
        queryRequest.Ids.AddRange(attachmentsId);
        var queryResult = await filesClient.GetFileBatchAsync(queryRequest);
        message.Attachments = queryResult.Files.Select(SnCloudFileReferenceObject.FromProtoValue).ToList();
    }

    private async Task DeleteFileReferencesForMessageAsync(SnChatMessage message)
    {
        var messageResourceId = $"message:{message.Id}";
        await fileRefs.DeleteResourceReferencesAsync(
            new DeleteResourceReferencesRequest { ResourceId = messageResourceId }
        );
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

    public async Task<SyncResponse> GetSyncDataAsync(Guid roomId, long lastSyncTimestamp, int limit = 500)
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
        List<string>? attachmentsId = null
    )
    {
        // Only allow editing regular text messages
        if (message.Type != "text")
        {
            throw new InvalidOperationException("Only regular messages can be edited.");
        }

        var isContentChanged = content is not null && content != message.Content;
        var isAttachmentsChanged = attachmentsId is not null;

        string? prevContent = null;
        if (isContentChanged)
            prevContent = message.Content;

        if (content is not null)
            message.Content = content;

        // Update do not override meta, replies to and forwarded to

        if (attachmentsId is not null)
            await UpdateFileReferencesForMessageAsync(message, attachmentsId);

        // Mark as edited if content or attachments changed
        if (isContentChanged || isAttachmentsChanged)
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
            Attachments = message.Attachments,
            Nonce = Guid.NewGuid().ToString(),
            Meta = message.Meta != null
                ? new Dictionary<string, object>(message.Meta) { ["message_id"] = message.Id }
                : new Dictionary<string, object> { ["message_id"] = message.Id },
            CreatedAt = message.UpdatedAt,
            UpdatedAt = message.UpdatedAt
        };

        if (isContentChanged && prevContent is not null)
            syncMessage.Meta["previous_content"] = prevContent;

        db.ChatMessages.Add(syncMessage);
        await db.SaveChangesAsync();

        // Process link preview in the background if content was updated
        if (isContentChanged)
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
    public async Task DeleteMessageAsync(SnChatMessage message)
    {
        // Only allow deleting regular text messages
        if (message.Type != "text")
        {
            throw new InvalidOperationException("Only regular messages can be deleted.");
        }

        // Remove all file references for this message
        await DeleteFileReferencesForMessageAsync(message);

        // Soft delete by setting DeletedAt timestamp
        message.DeletedAt = SystemClock.Instance.GetCurrentInstant();
        message.UpdatedAt = message.DeletedAt.Value;

        db.Update(message);
        await db.SaveChangesAsync();

        // Create and store sync message for the deletion
        var syncMessage = new SnChatMessage
        {
            Type = "messages.delete",
            ChatRoomId = message.ChatRoomId,
            SenderId = message.SenderId,
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
}

public class SyncResponse
{
    public List<SnChatMessage> Messages { get; set; } = [];
    public Instant CurrentTimestamp { get; set; }
    public int TotalCount { get; set; } = 0;
}
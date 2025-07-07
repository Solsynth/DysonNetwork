using System.Text.RegularExpressions;
using DysonNetwork.Sphere.Account;
using DysonNetwork.Sphere.Chat.Realtime;
using DysonNetwork.Sphere.Connection;
using DysonNetwork.Sphere.Storage;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Chat;

public partial class ChatService(
    AppDatabase db,
    FileReferenceService fileRefService,
    IServiceScopeFactory scopeFactory,
    IRealtimeService realtime,
    ILogger<ChatService> logger
)
{
    private const string ChatFileUsageIdentifier = "chat";

    [GeneratedRegex(@"https?://[-A-Za-z0-9+&@#/%?=~_|!:,.;]*[-A-Za-z0-9+&@#/%=~_|]")]
    private static partial Regex GetLinkRegex();

    /// <summary>
    /// Process link previews for a message in the background
    /// This method is designed to be called from a background task
    /// </summary>
    /// <param name="message">The message to process link previews for</param>
    private async Task ProcessMessageLinkPreviewAsync(Message message)
    {
        try
        {
            // Create a new scope for database operations
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDatabase>();
            var webReader = scope.ServiceProvider.GetRequiredService<Connection.WebReader.WebReaderService>();
            var newChat = scope.ServiceProvider.GetRequiredService<ChatService>();

            // Preview the links in the message
            var updatedMessage = await PreviewMessageLinkAsync(message, webReader);

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

                    // Notify clients of the updated message
                    await newChat.DeliverMessageAsync(
                        dbMessage,
                        dbMessage.Sender,
                        dbMessage.ChatRoom,
                        WebSocketPacketType.MessageUpdate
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
    public async Task<Message> PreviewMessageLinkAsync(Message message,
        Connection.WebReader.WebReaderService? webReader = null)
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
        webReader ??= scopeFactory.CreateScope().ServiceProvider
            .GetRequiredService<Connection.WebReader.WebReaderService>();

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
                var linkEmbed = await webReader.GetLinkPreviewAsync(url);
                embeds.Add(linkEmbed.ToDictionary());
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

    public async Task<Message> SendMessageAsync(Message message, ChatMember sender, ChatRoom room)
    {
        if (string.IsNullOrWhiteSpace(message.Nonce)) message.Nonce = Guid.NewGuid().ToString();
        message.CreatedAt = SystemClock.Instance.GetCurrentInstant();
        message.UpdatedAt = message.CreatedAt;

        // First complete the save operation
        db.ChatMessages.Add(message);
        await db.SaveChangesAsync();

        var files = message.Attachments.Distinct().ToList();
        if (files.Count != 0)
        {
            var messageResourceId = $"message:{message.Id}";
            foreach (var file in files)
            {
                await fileRefService.CreateReferenceAsync(
                    file.Id,
                    ChatFileUsageIdentifier,
                    messageResourceId,
                    duration: Duration.FromDays(30)
                );
            }
        }

        // Then start the delivery process
        _ = Task.Run(async () =>
        {
            try
            {
                await DeliverMessageAsync(message, sender, room);
            }
            catch (Exception ex)
            {
                // Log the exception properly
                // Consider using ILogger or your logging framework
                logger.LogError($"Error when delivering message: {ex.Message} {ex.StackTrace}");
            }
        });

        // Process link preview in the background to avoid delaying message sending
        _ = Task.Run(async () => await ProcessMessageLinkPreviewAsync(message));

        message.Sender = sender;
        message.ChatRoom = room;
        return message;
    }

    private async Task DeliverMessageAsync(
        Message message,
        ChatMember sender,
        ChatRoom room,
        string type = WebSocketPacketType.MessageNew
    )
    {
        message.Sender = sender;
        message.ChatRoom = room;

        using var scope = scopeFactory.CreateScope();
        var scopedWs = scope.ServiceProvider.GetRequiredService<WebSocketService>();
        var scopedNty = scope.ServiceProvider.GetRequiredService<NotificationService>();
        var scopedCrs = scope.ServiceProvider.GetRequiredService<ChatRoomService>();

        var roomSubject = room is { Type: ChatRoomType.DirectMessage, Name: null } ? "DM" :
            room.Realm is not null ? $"{room.Name}, {room.Realm.Name}" : room.Name;

        var members = await scopedCrs.ListRoomMembers(room.Id);

        var metaDict =
            new Dictionary<string, object>
            {
                ["sender_name"] = sender.Nick ?? sender.Account.Nick,
                ["user_id"] = sender.AccountId,
                ["sender_id"] = sender.Id,
                ["message_id"] = message.Id,
                ["room_id"] = room.Id,
                ["images"] = message.Attachments
                    .Where(a => a.MimeType != null && a.MimeType.StartsWith("image"))
                    .Select(a => a.Id).ToList(),
                ["action_uri"] = $"/chat/{room.Id}"
            };
        if (sender.Account.Profile is not { Picture: null })
            metaDict["pfp"] = sender.Account.Profile.Picture.Id;
        if (!string.IsNullOrEmpty(room.Name))
            metaDict["room_name"] = room.Name;

        var notification = new Notification
        {
            Topic = "messages.new",
            Title = $"{sender.Nick ?? sender.Account.Nick} ({roomSubject})",
            Content = !string.IsNullOrEmpty(message.Content)
                ? message.Content[..Math.Min(message.Content.Length, 100)]
                : "<no content>",
            Meta = metaDict,
            Priority = 10,
        };

        List<Account.Account> accountsToNotify = [];
        foreach (var member in members)
        {
            scopedWs.SendPacketToAccount(member.AccountId, new WebSocketPacket
            {
                Type = type,
                Data = message
            });

            if (member.Account.Id == sender.AccountId) continue;
            if (member.Notify == ChatMemberNotify.None) continue;
            // if (scopedWs.IsUserSubscribedToChatRoom(member.AccountId, room.Id.ToString())) continue;
            if (message.MembersMentioned is null || !message.MembersMentioned.Contains(member.Account.Id))
            {
                var now = SystemClock.Instance.GetCurrentInstant();
                if (member.BreakUntil is not null && member.BreakUntil > now) continue;
            }
            else if (member.Notify == ChatMemberNotify.Mentions) continue;

            accountsToNotify.Add(member.Account);
        }

        logger.LogInformation($"Trying to deliver message to {accountsToNotify.Count} accounts...");
        // Only send notifications if there are accounts to notify
        if (accountsToNotify.Count > 0)
            await scopedNty.SendNotificationBatch(notification, accountsToNotify, save: false);

        logger.LogInformation($"Delivered message to {accountsToNotify.Count} accounts.");
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
            .Where(m => m.AccountId == userId && m.ChatRoomId == roomId)
            .FirstOrDefaultAsync();
        if (sender is null) throw new ArgumentException("User is not a member of the chat room.");

        sender.LastReadAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync();
    }

    public async Task<int> CountUnreadMessage(Guid userId, Guid chatRoomId)
    {
        var sender = await db.ChatMembers
            .Where(m => m.AccountId == userId && m.ChatRoomId == chatRoomId)
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

    public async Task<Dictionary<Guid, Message?>> ListLastMessageForUser(Guid userId)
    {
        var userRooms = await db.ChatMembers
            .Where(m => m.AccountId == userId)
            .Select(m => m.ChatRoomId)
            .ToListAsync();

        var messages = await db.ChatMessages
            .IgnoreQueryFilters()
            .Include(m => m.Sender)
            .Include(m => m.Sender.Account)
            .Include(m => m.Sender.Account.Profile)
            .Where(m => userRooms.Contains(m.ChatRoomId))
            .GroupBy(m => m.ChatRoomId)
            .Select(g => g.OrderByDescending(m => m.CreatedAt).FirstOrDefault())
            .ToDictionaryAsync(
                m => m!.ChatRoomId,
                m => m
            );

        return messages;
    }

    public async Task<RealtimeCall> CreateCallAsync(ChatRoom room, ChatMember sender)
    {
        var call = new RealtimeCall
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

        await SendMessageAsync(new Message
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

    public async Task EndCallAsync(Guid roomId, ChatMember sender)
    {
        var call = await GetCallOngoingAsync(roomId);
        if (call is null) throw new InvalidOperationException("No ongoing call was not found.");
        if (sender.Role < ChatMemberRole.Moderator && call.SenderId != sender.Id)
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

        await SendMessageAsync(new Message
        {
            Type = "call.ended",
            ChatRoomId = call.RoomId,
            SenderId = call.SenderId,
            Meta = new Dictionary<string, object>
            {
                { "call_id", call.Id },
                { "duration", (call.EndedAt!.Value - call.CreatedAt).TotalSeconds }
            }
        }, call.Sender, call.Room);
    }

    public async Task<RealtimeCall?> GetCallOngoingAsync(Guid roomId)
    {
        return await db.ChatRealtimeCall
            .Where(c => c.RoomId == roomId)
            .Where(c => c.EndedAt == null)
            .Include(c => c.Room)
            .Include(c => c.Sender)
            .FirstOrDefaultAsync();
    }

    public async Task<SyncResponse> GetSyncDataAsync(Guid roomId, long lastSyncTimestamp)
    {
        var timestamp = Instant.FromUnixTimeMilliseconds(lastSyncTimestamp);
        var changes = await db.ChatMessages
            .IgnoreQueryFilters()
            .Include(e => e.Sender)
            .Include(e => e.Sender.Account)
            .Include(e => e.Sender.Account.Profile)
            .Where(m => m.ChatRoomId == roomId)
            .Where(m => m.UpdatedAt > timestamp || m.DeletedAt > timestamp)
            .Select(m => new MessageChange
            {
                MessageId = m.Id,
                Action = m.DeletedAt != null ? "delete" : (m.UpdatedAt == m.CreatedAt ? "create" : "update"),
                Message = m.DeletedAt != null ? null : m,
                Timestamp = m.DeletedAt ?? m.UpdatedAt
            })
            .ToListAsync();

        return new SyncResponse
        {
            Changes = changes,
            CurrentTimestamp = SystemClock.Instance.GetCurrentInstant()
        };
    }

    public async Task<Message> UpdateMessageAsync(
        Message message,
        Dictionary<string, object>? meta = null,
        string? content = null,
        Guid? repliedMessageId = null,
        Guid? forwardedMessageId = null,
        List<string>? attachmentsId = null
    )
    {
        if (content is not null)
            message.Content = content;

        if (meta is not null)
            message.Meta = meta;

        if (repliedMessageId.HasValue)
            message.RepliedMessageId = repliedMessageId;

        if (forwardedMessageId.HasValue)
            message.ForwardedMessageId = forwardedMessageId;

        if (attachmentsId is not null)
        {
            var messageResourceId = $"message:{message.Id}";

            // Delete existing references for this message
            await fileRefService.DeleteResourceReferencesAsync(messageResourceId);

            // Create new references for each attachment
            foreach (var fileId in attachmentsId)
            {
                await fileRefService.CreateReferenceAsync(
                    fileId,
                    ChatFileUsageIdentifier,
                    messageResourceId,
                    duration: Duration.FromDays(30)
                );
            }

            // Update message attachments by getting files from database
            var files = await db.Files
                .Where(f => attachmentsId.Contains(f.Id))
                .ToListAsync();

            message.Attachments = files.Select(x => x.ToReferenceObject()).ToList();
        }

        message.EditedAt = SystemClock.Instance.GetCurrentInstant();
        db.Update(message);
        await db.SaveChangesAsync();

        // Process link preview in the background if content was updated
        if (content is not null)
            _ = Task.Run(async () => await ProcessMessageLinkPreviewAsync(message));

        _ = DeliverMessageAsync(
            message,
            message.Sender,
            message.ChatRoom,
            WebSocketPacketType.MessageUpdate
        );

        return message;
    }

    /// <summary>
    /// Deletes a message and notifies other chat members
    /// </summary>
    /// <param name="message">The message to delete</param>
    public async Task DeleteMessageAsync(Message message)
    {
        // Remove all file references for this message
        var messageResourceId = $"message:{message.Id}";
        await fileRefService.DeleteResourceReferencesAsync(messageResourceId);

        db.ChatMessages.Remove(message);
        await db.SaveChangesAsync();

        _ = DeliverMessageAsync(
            message,
            message.Sender,
            message.ChatRoom,
            WebSocketPacketType.MessageDelete
        );
    }
}

public class MessageChangeAction
{
    public const string Create = "create";
    public const string Update = "update";
    public const string Delete = "delete";
}

public class MessageChange
{
    public Guid MessageId { get; set; }
    public string Action { get; set; } = null!;
    public Message? Message { get; set; }
    public Instant Timestamp { get; set; }
}

public class SyncResponse
{
    public List<MessageChange> Changes { get; set; } = [];
    public Instant CurrentTimestamp { get; set; }
}

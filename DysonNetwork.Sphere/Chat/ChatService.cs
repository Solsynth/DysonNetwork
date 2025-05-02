using DysonNetwork.Sphere;
using DysonNetwork.Sphere.Chat;
using Microsoft.EntityFrameworkCore;

public class ChatService(AppDatabase db)
{
    public async Task MarkMessageAsReadAsync(Guid messageId, long roomId, long userId)
    {
        var existingStatus = await db.ChatStatuses
            .FirstOrDefaultAsync(x => x.MessageId == messageId && x.Sender.AccountId == userId);
        var sender = await db.ChatMembers
            .Where(m => m.AccountId == userId && m.ChatRoomId == roomId)
            .FirstOrDefaultAsync();
        if (sender is null) throw new ArgumentException("User is not a member of the chat room.");

        if (existingStatus == null)
        {
            existingStatus = new MessageStatus
            {
                MessageId = messageId,
                SenderId = sender.Id,
            };
            db.ChatStatuses.Add(existingStatus);
        }

        await db.SaveChangesAsync();
    }

    public async Task<bool> GetMessageReadStatus(Guid messageId, long userId)
    {
        return await db.ChatStatuses
            .AnyAsync(x => x.MessageId == messageId && x.Sender.AccountId == userId);
    }

    public async Task<int> CountUnreadMessage(long userId, long chatRoomId)
    {
        var messages = await db.ChatMessages
            .Where(m => m.ChatRoomId == chatRoomId)
            .Select(m => new MessageStatusResponse
            {
                MessageId = m.Id,
                IsRead = m.Statuses.Any(rs => rs.Sender.AccountId == userId)
            })
            .ToListAsync();

        return messages.Count(m => !m.IsRead);
    }
}
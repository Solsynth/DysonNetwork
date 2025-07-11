using DysonNetwork.Sphere.Connection;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Chat.Realtime;

public class ParticipantInfoItem
{
    public string Identity { get; set; } = null!;
    public string Name { get; set; } = null!;
    public Guid? AccountId { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
    public DateTime JoinedAt { get; set; }
}

public class RealtimeStatusService(AppDatabase db, WebSocketService ws, ILogger<RealtimeStatusService> logger)
{
    // Broadcast participant update to all participants in a room
    public async Task BroadcastParticipantUpdate(string roomName, List<ParticipantInfoItem> participantsInfo)
    {
        try
        {
            // Get the room ID from the session name
            var roomInfo = await db.ChatRealtimeCall
                .Where(c => c.SessionId == roomName && c.EndedAt == null)
                .Select(c => new { c.RoomId, c.Id })
                .FirstOrDefaultAsync();

            if (roomInfo == null)
            {
                logger.LogWarning("Could not find room info for session: {SessionName}", roomName);
                return;
            }

            // Get all room members who should receive this update
            var roomMembers = await db.ChatMembers
                .Where(m => m.ChatRoomId == roomInfo.RoomId && m.LeaveAt == null)
                .Select(m => m.AccountId)
                .ToListAsync();

            // Get member profiles for participants who have account IDs
            var accountIds = participantsInfo
                .Where(p => p.AccountId.HasValue)
                .Select(p => p.AccountId!.Value)
                .ToList();

            var memberProfiles = new Dictionary<Guid, ChatMember>();
            if (accountIds.Count != 0)
            {
                memberProfiles = await db.ChatMembers
                    .Where(m => m.ChatRoomId == roomInfo.RoomId && accountIds.Contains(m.AccountId))
                    .Include(m => m.Account)
                    .ThenInclude(m => m.Profile)
                    .ToDictionaryAsync(m => m.AccountId, m => m);
            }

            // Convert to CallParticipant objects
            var participants = participantsInfo.Select(p => new CallParticipant
            {
                Identity = p.Identity,
                Name = p.Name,
                AccountId = p.AccountId,
                JoinedAt = p.JoinedAt,
                Profile = p.AccountId.HasValue && memberProfiles.TryGetValue(p.AccountId.Value, out var profile)
                    ? profile
                    : null
            }).ToList();

            // Create the update packet with CallParticipant objects
            var updatePacket = new WebSocketPacket
            {
                Type = WebSocketPacketType.CallParticipantsUpdate,
                Data = new Dictionary<string, object>
                {
                    { "room_id", roomInfo.RoomId },
                    { "call_id", roomInfo.Id },
                    { "participants", participants }
                }
            };

            // Send the update to all members
            foreach (var accountId in roomMembers)
            {
                ws.SendPacketToAccount(accountId, updatePacket);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error broadcasting participant update for room {RoomName}", roomName);
        }
    }
}
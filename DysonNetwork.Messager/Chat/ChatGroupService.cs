using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Messager.Chat;

public class ChatGroupService(AppDatabase db)
{
    public async Task<SnChatGroup> CreateGroupAsync(
        Guid accountId,
        string name,
        string? color = null,
        string? icon = null,
        int? order = null
    )
    {
        var maxOrder = order ?? await db.ChatGroups
            .Where(g => g.AccountId == accountId && g.DeletedAt == null)
            .MaxAsync(g => (int?)g.Order) ?? -1;

        var group = new SnChatGroup
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Name = name,
            Color = color,
            Icon = icon,
            Order = order ?? maxOrder + 1,
        };

        db.ChatGroups.Add(group);
        await db.SaveChangesAsync();
        return group;
    }

    public async Task<SnChatGroup?> UpdateGroupAsync(
        Guid groupId,
        Guid accountId,
        string? name = null,
        string? color = null,
        string? icon = null,
        int? order = null
    )
    {
        var group = await db.ChatGroups
            .FirstOrDefaultAsync(g => g.Id == groupId && g.AccountId == accountId && g.DeletedAt == null);
        if (group is null) return null;

        if (name is not null) group.Name = name;
        if (color is not null) group.Color = color;
        if (icon is not null) group.Icon = icon;
        if (order.HasValue) group.Order = order.Value;

        await db.SaveChangesAsync();
        return group;
    }

    public async Task<bool> DeleteGroupAsync(Guid groupId, Guid accountId)
    {
        var group = await db.ChatGroups
            .FirstOrDefaultAsync(g => g.Id == groupId && g.AccountId == accountId && g.DeletedAt == null);
        if (group is null) return false;

        group.DeletedAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<List<SnChatGroup>> ListGroupsAsync(Guid accountId)
    {
        var groups = await db.ChatGroups
            .Where(g => g.AccountId == accountId && g.DeletedAt == null)
            .OrderBy(g => g.Order)
            .ToListAsync();

        var groupIds = groups.Select(g => g.Id).ToList();
        var memberRoomMap = await db.ChatMembers
            .Where(m => m.ChatGroupId.HasValue && groupIds.Contains(m.ChatGroupId.Value)
                        && m.JoinedAt != null && m.LeaveAt == null)
            .GroupBy(m => m.ChatGroupId!.Value)
            .Select(g => new { GroupId = g.Key, RoomIds = g.Select(m => m.ChatRoomId).ToList() })
            .ToDictionaryAsync(x => x.GroupId, x => x.RoomIds);

        foreach (var group in groups)
        {
            if (memberRoomMap.TryGetValue(group.Id, out var roomIds))
                group.RoomIds = roomIds;
        }

        return groups;
    }

    public async Task<SnChatGroup?> GetGroupAsync(Guid groupId, Guid accountId)
    {
        var group = await db.ChatGroups
            .FirstOrDefaultAsync(g => g.Id == groupId && g.AccountId == accountId && g.DeletedAt == null);
        if (group is null) return null;

        group.RoomIds = await db.ChatMembers
            .Where(m => m.ChatGroupId == groupId && m.JoinedAt != null && m.LeaveAt == null)
            .Select(m => m.ChatRoomId)
            .ToListAsync();

        return group;
    }

    public async Task<bool> MoveToGroupAsync(Guid accountId, Guid roomId, Guid? groupId)
    {
        var member = await db.ChatMembers
            .FirstOrDefaultAsync(m => m.AccountId == accountId && m.ChatRoomId == roomId && m.JoinedAt != null && m.LeaveAt == null);
        if (member is null) return false;

        if (groupId.HasValue)
        {
            var group = await db.ChatGroups
                .AnyAsync(g => g.Id == groupId.Value && g.AccountId == accountId && g.DeletedAt == null);
            if (!group) return false;
        }

        member.ChatGroupId = groupId;
        await db.SaveChangesAsync();
        return true;
    }
}

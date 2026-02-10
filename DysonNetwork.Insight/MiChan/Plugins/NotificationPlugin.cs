using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace DysonNetwork.Insight.MiChan.Plugins;

public class NotificationPlugin(SolarNetworkApiClient apiClient, ILogger<NotificationPlugin> logger)
{
    [KernelFunction("get_notifications")]
    [Description("Get the bot's notifications.")]
    public async Task<List<object>?> GetNotifications(
        [Description("Maximum number of notifications to retrieve")] int limit = 50
    )
    {
        try
        {
            var url = $"/notifications?take={limit}";
                
            var notifications = await apiClient.GetAsync<List<object>>("messager", url);
            return notifications ?? new List<object>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get notifications");
            return null;
        }
    }

    [KernelFunction("approve_chat_request")]
    [Description("Approve a chat room invitation or request.")]
    public async Task<object> ApproveChatRequest(
        [Description("The ID of the chat room to approve")] string chatRoomId
    )
    {
        try
        {
            await apiClient.PostAsync("messager", $"/chat/rooms/{chatRoomId}/approve", new { });
            
            logger.LogInformation("Approved chat request for room {ChatRoomId}", chatRoomId);
            return new { success = true, message = "Chat request approved" };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to approve chat request for room {ChatRoomId}", chatRoomId);
            return new { success = false, error = ex.Message };
        }
    }

    [KernelFunction("decline_chat_request")]
    [Description("Decline a chat room invitation or request.")]
    public async Task<object> DeclineChatRequest(
        [Description("The ID of the chat room to decline")] string chatRoomId
    )
    {
        try
        {
            await apiClient.PostAsync("messager", $"/chat/rooms/{chatRoomId}/decline", new { });
            
            logger.LogInformation("Declined chat request for room {ChatRoomId}", chatRoomId);
            return new { success = true, message = "Chat request declined" };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to decline chat request for room {ChatRoomId}", chatRoomId);
            return new { success = false, error = ex.Message };
        }
    }

    [KernelFunction("mark_notifications_read")]
    [Description("Mark all or specific notifications as read.")]
    public async Task<object> MarkNotificationsRead(
        [Description("Optional ID of specific notification to mark as read")] string? notificationId = null
    )
    {
        try
        {
            if (string.IsNullOrEmpty(notificationId))
            {
                await apiClient.PostAsync("messager", "/notifications/mark-all-read", new { });
                logger.LogInformation("Marked all notifications as read");
            }
            else
            {
                await apiClient.PostAsync("messager", $"/notifications/{notificationId}/read", new { });
                logger.LogInformation("Marked notification {NotificationId} as read", notificationId);
            }
            
            return new { success = true, message = "Notifications marked as read" };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to mark notifications as read");
            return new { success = false, error = ex.Message };
        }
    }

    [KernelFunction("get_unread_notification_count")]
    [Description("Get the count of unread notifications.")]
    public async Task<int?> GetUnreadNotificationCount()
    {
        try
        {
            var result = await apiClient.GetAsync<dynamic>("messager", "/notifications/count");
            return result?.unread_count;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get unread notification count");
            return null;
        }
    }
}

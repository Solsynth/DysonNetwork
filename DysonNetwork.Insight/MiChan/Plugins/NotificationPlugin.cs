using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace DysonNetwork.Insight.MiChan.Plugins;

public class NotificationPlugin
{
    private readonly SolarNetworkApiClient _apiClient;
    private readonly ILogger<NotificationPlugin> _logger;

    public NotificationPlugin(SolarNetworkApiClient apiClient, ILogger<NotificationPlugin> logger)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    [KernelFunction("get_notifications")]
    [Description("Get the bot's notifications.")]
    public async Task<List<object>?> GetNotifications(
        [Description("Maximum number of notifications to retrieve")] int limit = 50,
        [Description("Filter by type: all, mention, like, follow, chat_request, etc.")]
 string type = "all"
    )
    {
        try
        {
            var url = type == "all" 
                ? $"/notifications?take={limit}"
                : $"/notifications?take={limit}&type={type}";
                
            var notifications = await _apiClient.GetAsync<List<object>>("messager", url);
            return notifications ?? new List<object>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get notifications");
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
            await _apiClient.PostAsync("messager", $"/chat/rooms/{chatRoomId}/approve", new { });
            
            _logger.LogInformation("Approved chat request for room {ChatRoomId}", chatRoomId);
            return new { success = true, message = "Chat request approved" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to approve chat request for room {ChatRoomId}", chatRoomId);
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
            await _apiClient.PostAsync("messager", $"/chat/rooms/{chatRoomId}/decline", new { });
            
            _logger.LogInformation("Declined chat request for room {ChatRoomId}", chatRoomId);
            return new { success = true, message = "Chat request declined" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decline chat request for room {ChatRoomId}", chatRoomId);
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
                await _apiClient.PostAsync("messager", "/notifications/mark-all-read", new { });
                _logger.LogInformation("Marked all notifications as read");
            }
            else
            {
                await _apiClient.PostAsync("messager", $"/notifications/{notificationId}/read", new { });
                _logger.LogInformation("Marked notification {NotificationId} as read", notificationId);
            }
            
            return new { success = true, message = "Notifications marked as read" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark notifications as read");
            return new { success = false, error = ex.Message };
        }
    }

    [KernelFunction("get_unread_notification_count")]
    [Description("Get the count of unread notifications.")]
    public async Task<int?> GetUnreadNotificationCount()
    {
        try
        {
            var result = await _apiClient.GetAsync<dynamic>("messager", "/notifications/count");
            return result?.unread_count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get unread notification count");
            return null;
        }
    }
}

using System.ComponentModel;
using DysonNetwork.Shared.Models;
using Microsoft.SemanticKernel;

namespace DysonNetwork.Insight.MiChan.Plugins;

public class ChatPlugin
{
    private readonly SolarNetworkApiClient _apiClient;
    private readonly ILogger<ChatPlugin> _logger;

    public ChatPlugin(SolarNetworkApiClient apiClient, ILogger<ChatPlugin> logger)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    [KernelFunction("send_message")]
    [Description("Send a message to a chat room or user.")]
    public async Task<object> SendMessage(
        [Description("The ID of the chat room to send the message to")] string chatRoomId,
        [Description("The content of the message")] string content,
        [Description("Optional ID of the message being replied to")] string? replyToMessageId = null
    )
    {
        try
        {
            var request = new
            {
                content = content,
                replied_message_id = string.IsNullOrEmpty(replyToMessageId) ? (Guid?)null : Guid.Parse(replyToMessageId),
                nonce = Guid.NewGuid().ToString("N")
            };

            await _apiClient.PostAsync("messager", $"/chat/{chatRoomId}/messages", request);
            
            _logger.LogInformation("Sent message to chat room {ChatRoomId}", chatRoomId);
            return new { success = true, message = "Message sent successfully" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message to chat room {ChatRoomId}", chatRoomId);
            return new { success = false, error = ex.Message };
        }
    }

    [KernelFunction("get_chat_history")]
    [Description("Get the message history for a chat room.")]
    public async Task<List<SnChatMessage>?> GetChatHistory(
        [Description("The ID of the chat room")] string chatRoomId,
        [Description("Number of messages to retrieve")] int count = 20,
        [Description("Offset for pagination")] int offset = 0
    )
    {
        try
        {
            var messages = await _apiClient.GetAsync<List<SnChatMessage>>(
                "messager", 
                $"/chat/{chatRoomId}/messages?take={count}&offset={offset}"
            );
            
            return messages ?? new List<SnChatMessage>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get chat history for room {ChatRoomId}", chatRoomId);
            return null;
        }
    }

    [KernelFunction("list_chat_rooms")]
    [Description("List all chat rooms where the bot is a member.")]
    public async Task<object?> ListChatRooms(
        [Description("Maximum number of rooms to retrieve")] int limit = 50
    )
    {
        try
        {
            var rooms = await _apiClient.GetAsync<List<object>>(
                "messager", 
                $"/chat/rooms?take={limit}"
            );
            
            return rooms;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list chat rooms");
            return null;
        }
    }

    [KernelFunction("get_chat_room_info")]
    [Description("Get information about a specific chat room.")]
    public async Task<object?> GetChatRoomInfo(
        [Description("The ID of the chat room")] string chatRoomId
    )
    {
        try
        {
            var roomInfo = await _apiClient.GetAsync<object>(
                "messager", 
                $"/chat/rooms/{chatRoomId}"
            );
            
            return roomInfo;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get info for chat room {ChatRoomId}", chatRoomId);
            return null;
        }
    }
}

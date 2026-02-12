using System.ComponentModel;
using DysonNetwork.Insight.Thought;
using DysonNetwork.Shared.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;

namespace DysonNetwork.Insight.MiChan.Plugins;

/// <summary>
/// Plugin for managing conversations and starting agent-initiated discussions
/// </summary>
public class ConversationPlugin(
    IServiceProvider serviceProvider,
    RemoteAccountService remoteAccountService,
    ILogger<ConversationPlugin> logger
)
{
    /// <summary>
    /// Start a conversation with a user by creating an agent-initiated thought sequence.
    /// This allows you to proactively reach out to a user with a message.
    /// </summary>
    [KernelFunction("start_conversation")]
    [Description("Start a new conversation with a user by creating an agent-initiated message. " +
                 "Use this when you want to proactively reach out to a user, share something interesting, " +
                 "follow up on a previous conversation, or start a new discussion. " +
                 "Available ONLY in scheduled task context.")]
    public async Task<string> StartConversationAsync(
        [Description("The account ID (Guid) of the user you want to talk to")]
        Guid accountId,
        [Description("The initial message you want to send to the user")]
        string message,
        [Description("Optional: A topic/title for this conversation (e.g., 'Daily Check-in', 'Interesting Discovery')")]
        string? topic = null)
    {
        using var scope = serviceProvider.CreateScope();
        var thoughtService = scope.ServiceProvider.GetRequiredService<ThoughtService>();

        try
        {
            if (string.IsNullOrWhiteSpace(message))
                return "Error: Message cannot be empty.";

            logger.LogInformation("Starting agent-initiated conversation with account {AccountId}", accountId);
            
            var accountInfo = await remoteAccountService.GetAccount(accountId);

            // Create the agent-initiated sequence
            var sequence = await thoughtService.CreateAgentInitiatedSequenceAsync(
                accountId: accountId,
                locale: accountInfo.Language,
                initialMessage: message,
                topic: topic
            );

            if (sequence == null)
            {
                return "Error: Failed to create conversation. The user may not exist or there was a system error.";
            }

            return $"Successfully started a conversation with the user! " +
                   $"Conversation ID: {sequence.Id}\n" +
                   $"Topic: {sequence.Topic}\n" +
                   $"Message: {message}";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error starting conversation with account {AccountId}", accountId);
            return $"Error starting conversation: {ex.Message}";
        }
    }
}
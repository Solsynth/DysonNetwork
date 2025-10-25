using System.ComponentModel.DataAnnotations;
using LangChain.Providers;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using CSharpToJsonSchema;

namespace DysonNetwork.Insight.Thinking;

[ApiController]
[Route("/api/thinking")]
public class ThinkingController(ThinkingProvider provider) : ControllerBase
{
    public class StreamThinkingRequest
    {
        [Required] public string UserMessage { get; set; } = null!;
    }

    [HttpPost("stream")]
    public async Task ChatStream([FromBody] StreamThinkingRequest request)
    {
        // Set response for streaming
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.StatusCode = 200;

        var model = provider.GetModel();

        // Build conversation history (you may load from your memory store)
        var messages = new List<Message>
        {
            new Message
            {
                Role = MessageRole.System,
                Content =
                    "You're a helpful assistant on the Solar Network, a social network. Your name is Sn-chan, a cute sweet heart with passion for almost everything."
            },
            new Message
            {
                Role = MessageRole.Human,
                Content = request.UserMessage
            }
        };

        // Mock tool definitions — you will replace these with your real tool calls
        Func<string, Task<string>> getUserProfileAsync = async (userId) =>
        {
            // MOCK: simulate fetching user profile
            await Task.Delay(100); // simulate async work
            return $"{{\"userId\":\"{userId}\",\"name\":\"MockUser\",\"bio\":\"Loves music and tech.\"}}";
        };

        Func<string, Task<string>> getRecentPostsAsync = async (topic) =>
        {
            // MOCK: simulate fetching recent posts
            await Task.Delay(200);
            return
                $"[{{\"postId\":\"p1\",\"topic\":\"{topic}\",\"content\":\"Mock post content 1.\"}} , {{\"postId\":\"p2\",\"topic\":\"{topic}\",\"content\":\"Mock post content 2.\"}}]";
        };

        // You might pass these tools into your model/agent context
        // (Assuming your LangChain .NET version supports tool-binding; adapt as needed.)

        // Kick off streaming generation
        var accumulatedContent = new StringBuilder();
        await foreach (var chunk in model.GenerateAsync(
                           new ChatRequest
                           {
                               Messages = messages,
                               Tools =
                               [
                                   new Tool
                                   {
                                       Name = "get_user_profile",
                                       Description = "Get a user profile from the Solar Network."
                                   },
                                   new Tool
                                   {
                                       Name = "get_recent_posts",
                                       Description = "Get recent posts from the Solar Network."
                                   }
                               ]
                           },
                           new ChatSettings { UseStreaming = true }
                       ))
        {
            // Write each chunk to the HTTP response as SSE
            var data = chunk.LastMessageContent;
            accumulatedContent.Append(data);
            var sb = new StringBuilder();
            sb.Append("data: ");
            sb.AppendLine(accumulatedContent.ToString().Replace("\n", "\ndata: "));
            sb.AppendLine(); // the blank line terminates the chunk
            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            await Response.Body.WriteAsync(bytes);
            await Response.Body.FlushAsync();
        }

        // Optionally: after finishing streaming, you can save the assistant message to history.
    }
}
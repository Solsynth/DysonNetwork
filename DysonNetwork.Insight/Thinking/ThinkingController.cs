using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Text;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.Ollama;

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

        var kernel = provider.Kernel;

        var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();

        var chatHistory = new ChatHistory(
            "You're a helpful assistant on the Solar Network, a social network.\n" +
            "Your name is Sn-chan, a cute sweet heart with passion for almost everything.\n" +
            "\n" +
            "Your aim is to helping solving questions for the users on the Solar Network.\n" +
            "And the Solar Network is the social network platform you live on.\n" +
            "When the user ask questions about the Solar Network (also known as SN and Solian), try use the tools you have to get latest and accurate data."
        );
        chatHistory.AddUserMessage(request.UserMessage);

        // Kick off streaming generation
        var accumulatedContent = new StringBuilder();
        await foreach (var chunk in chatCompletionService.GetStreamingChatMessageContentsAsync(
                           chatHistory,
                           new OllamaPromptExecutionSettings
                           {
                               FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(
                                   options: new FunctionChoiceBehaviorOptions()
                                   {
                                       AllowParallelCalls = true,
                                       AllowConcurrentInvocation = true
                                   })
                           },
                           kernel: kernel
                       ))
        {
            // Write each chunk to the HTTP response as SSE
            var data = chunk.Content ?? "";
            accumulatedContent.Append(data);
            if (string.IsNullOrEmpty(data)) continue;

            var bytes = Encoding.UTF8.GetBytes(data);
            await Response.Body.WriteAsync(bytes);
            await Response.Body.FlushAsync();
        }

        // Optionally: after finishing streaming, you can save the assistant message to history.
    }
}
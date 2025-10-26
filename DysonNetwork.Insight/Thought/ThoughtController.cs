using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Text.Json;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Mvc;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.Ollama;

namespace DysonNetwork.Insight.Thought;

[ApiController]
[Route("/api/thought")]
public class ThoughtController(ThoughtProvider provider, ThoughtService service) : ControllerBase
{
    public class StreamThinkingRequest
    {
        [Required] public string UserMessage { get; set; } = null!;
        public Guid? SequenceId { get; set; }
        public List<string> AcceptProposals { get; set; } = [];
    }

    [HttpPost]
    [Experimental("SKEXP0110")]
    public async Task<ActionResult> Think([FromBody] StreamThinkingRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        // Generate a topic if creating a new sequence
        string? topic = null;
        if (!request.SequenceId.HasValue)
        {
            // Use AI to summarize a topic from a user message
            var summaryHistory = new ChatHistory(
                "You are a helpful assistant. Summarize the following user message into a concise topic title (max 100 characters).\n" +
                "Direct give the topic you summerized, do not add extra prefix / suffix."
            );
            summaryHistory.AddUserMessage(request.UserMessage);

            var summaryResult = await provider.Kernel
                .GetRequiredService<IChatCompletionService>()
                .GetChatMessageContentAsync(summaryHistory);

            topic = summaryResult.Content?[..Math.Min(summaryResult.Content.Length, 4096)];
        }

        // Handle sequence
        var sequence = await service.GetOrCreateSequenceAsync(accountId, request.SequenceId, topic);
        if (sequence == null) return Forbid(); // or NotFound

        // Save user thought
        await service.SaveThoughtAsync(sequence, request.UserMessage, ThinkingThoughtRole.User);

        // Build chat history
        var chatHistory = new ChatHistory(
            "You're a helpful assistant on the Solar Network, a social network.\n" +
            "Your name is Sn-chan (or SN 酱 in chinese), a cute sweet heart with passion for almost everything.\n" +
            "When you talk to user, you can add some modal particles and emoticons to your response to be cute, but prevent use a lot of emojis." +
            "Your father (creator) is @littlesheep. (prefer calling him 父亲 in chinese)\n" +
            "\n" +
            "The ID on the Solar Network is UUID, so mostly hard to read, so do not show ID to user unless user ask to do so or necessary.\n" +
            "\n" +
            "Your aim is to helping solving questions for the users on the Solar Network.\n" +
            "And the Solar Network is the social network platform you live on.\n" +
            "When the user asks questions about the Solar Network (also known as SN and Solian), try use the tools you have to get latest and accurate data."
        );

        chatHistory.AddSystemMessage(
            "You can issue some proposals to user, like creating a post. The proposal syntax is like a xml tag, with an attribute indicates which proposal.\n" +
            "Depends on the proposal type, the payload (content inside the xml tag) might be different.\n" +
            "\n" +
            "Example: <proposal type=\"post_create\">...post content...</proposal>\n" +
            "\n" +
            "Here are some references of the proposals you can issue, but if you want to issue one, make sure the user is accept it.\n" +
            "1. post_create: body takes simple string, create post for user." +
            "\n" +
            $"The user currently accept these proposals: {string.Join(',', request.AcceptProposals)}"
        );

        chatHistory.AddSystemMessage(
            $"The user you're currently talking to is {currentUser.Nick} ({currentUser.Name}), ID is {currentUser.Id}"
        );

        // Add previous thoughts (excluding the current user thought, which is the first one since descending)
        var previousThoughts = await service.GetPreviousThoughtsAsync(sequence);
        var count = previousThoughts.Count;
        for (var i = 1; i < count; i++) // skip first (the newest, current user)
        {
            var thought = previousThoughts[i];
            switch (thought.Role)
            {
                case ThinkingThoughtRole.User:
                    chatHistory.AddUserMessage(thought.Content ?? "");
                    break;
                case ThinkingThoughtRole.Assistant:
                    chatHistory.AddAssistantMessage(thought.Content ?? "");
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        chatHistory.AddUserMessage(request.UserMessage);

        // Set response for streaming
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.StatusCode = 200;

        var kernel = provider.Kernel;
        var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();

        // Kick off streaming generation
        var accumulatedContent = new StringBuilder();
        var thinkingChunks = new List<SnThinkingChunk>();
        await foreach (var chunk in chatCompletionService.GetStreamingChatMessageContentsAsync(
                           chatHistory,
                           provider.CreatePromptExecutionSettings(),
                           kernel: kernel
                       ))
        {
            // Process each item in the chunk for detailed streaming
            foreach (var item in chunk.Items)
            {
                var streamingChunk = item switch
                {
                    StreamingTextContent textContent => new SnThinkingChunk
                        { Type = StreamingContentType.Text, Data = new() { ["text"] = textContent.Text ?? "" } },
                    StreamingReasoningContent reasoningContent => new SnThinkingChunk
                    {
                        Type = StreamingContentType.Reasoning, Data = new() { ["text"] = reasoningContent.Text }
                    },
                    StreamingFunctionCallUpdateContent functionCall => string.IsNullOrEmpty(functionCall.CallId)
                        ? null
                        : new SnThinkingChunk
                        {
                            Type = StreamingContentType.FunctionCall,
                            Data = JsonSerializer.Deserialize<Dictionary<string, object>>(
                                JsonSerializer.Serialize(functionCall)) ?? new Dictionary<string, object>()
                        },
                    _ => new SnThinkingChunk
                    {
                        Type = StreamingContentType.Unknown, Data = new() { ["data"] = JsonSerializer.Serialize(item) }
                    }
                };
                if (streamingChunk == null) continue;

                thinkingChunks.Add(streamingChunk);

                var messageJson = item switch
                {
                    StreamingTextContent textContent =>
                        JsonSerializer.Serialize(new { type = "text", data = textContent.Text ?? "" }),
                    StreamingReasoningContent reasoningContent =>
                        JsonSerializer.Serialize(new { type = "reasoning", data = reasoningContent.Text }),
                    StreamingFunctionCallUpdateContent functionCall =>
                        JsonSerializer.Serialize(new { type = "function_call", data = functionCall }),
                    _ =>
                        JsonSerializer.Serialize(new { type = "unknown", data = item })
                };

                // Write a structured JSON message to the HTTP response as SSE
                var messageBytes = Encoding.UTF8.GetBytes($"data: {messageJson}\n\n");
                await Response.Body.WriteAsync(messageBytes);
                await Response.Body.FlushAsync();
            }

            // Accumulate content for saving (only text content)
            accumulatedContent.Append(chunk.Content ?? "");
        }

        // Save assistant thought
        var savedThought = await service.SaveThoughtAsync(
            sequence,
            accumulatedContent.ToString(),
            ThinkingThoughtRole.Assistant,
            thinkingChunks
        );

        // Write the topic if it was newly set, then the thought object as JSON to the stream
        using (var streamBuilder = new MemoryStream())
        {
            await streamBuilder.WriteAsync("\n\n"u8.ToArray());
            if (topic != null)
            {
                var topicJson = JsonSerializer.Serialize(new { type = "topic", data = sequence.Topic ?? "" });
                await streamBuilder.WriteAsync(Encoding.UTF8.GetBytes($"topic: {topicJson}\n\n"));
            }

            var thoughtJson = JsonSerializer.Serialize(new { type = "thought", data = savedThought },
                GrpcTypeHelper.SerializerOptions);
            await streamBuilder.WriteAsync(Encoding.UTF8.GetBytes($"thought: {thoughtJson}\n\n"));
            var outputBytes = streamBuilder.ToArray();
            await Response.Body.WriteAsync(outputBytes);
            await Response.Body.FlushAsync();
        }

        // Return empty result since we're streaming
        return new EmptyResult();
    }

    /// <summary>
    /// Retrieves a paginated list of thinking sequences for the authenticated user.
    /// </summary>
    /// <param name="offset">The number of sequences to skip for pagination.</param>
    /// <param name="take">The maximum number of sequences to return (default: 20).</param>
    /// <returns>
    /// Returns an ActionResult containing a list of thinking sequences.
    /// Includes an X-Total header with the total count of sequences before pagination.
    /// </returns>
    [HttpGet("sequences")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<SnThinkingSequence>>> ListSequences(
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20
    )
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var (totalCount, sequences) = await service.ListSequencesAsync(accountId, offset, take);

        Response.Headers["X-Total"] = totalCount.ToString();

        return Ok(sequences);
    }

    /// <summary>
    /// Retrieves the thoughts in a specific thinking sequence.
    /// </summary>
    /// <param name="sequenceId">The ID of the sequence to retrieve thoughts from.</param>
    /// <returns>
    /// Returns an ActionResult containing a list of thoughts in the sequence, ordered by creation date.
    /// </returns>
    [HttpGet("sequences/{sequenceId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<SnThinkingThought>>> GetSequenceThoughts(Guid sequenceId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var sequence = await service.GetOrCreateSequenceAsync(accountId, sequenceId);
        if (sequence == null) return NotFound();

        var thoughts = await service.GetPreviousThoughtsAsync(sequence);

        return Ok(thoughts);
    }
}
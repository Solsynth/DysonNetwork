using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Mvc;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace DysonNetwork.Insight.Thought;

[ApiController]
[Route("/api/thought")]
public class ThoughtController(ThoughtProvider provider, ThoughtService service) : ControllerBase
{
    public static readonly List<string> AvailableProposals = ["post_create"];

    public class StreamThinkingRequest
    {
        [Required] public string UserMessage { get; set; } = null!;
        public string? ServiceId { get; set; }
        public Guid? SequenceId { get; set; }
        public List<string>? AttachedPosts { get; set; } = [];
        public List<Dictionary<string, dynamic>>? AttachedMessages { get; set; }
        public List<string> AcceptProposals { get; set; } = [];
    }

    public class UpdateSharingRequest
    {
        public bool IsPublic { get; set; }
    }

    public class ThoughtServiceInfo
    {
        public string ServiceId { get; set; } = null!;
        public double BillingMultiplier { get; set; }
        public int PerkLevel { get; set; }
    }

    public class ThoughtServicesResponse
    {
        public string DefaultService { get; set; } = null!;
        public IEnumerable<ThoughtServiceInfo> Services { get; set; } = null!;
    }

    [HttpGet("services")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<ThoughtServicesResponse> GetAvailableServices()
    {
        var services = provider.GetAvailableServicesInfo()
            .Select(s => new ThoughtServiceInfo
            {
                ServiceId = s.ServiceId,
                BillingMultiplier = s.BillingMultiplier,
                PerkLevel = s.PerkLevel
            });

        return Ok(new ThoughtServicesResponse
        {
            DefaultService = provider.GetDefaultServiceId(),
            Services = services
        });
    }

    [HttpPost]
    [Experimental("SKEXP0110")]
    public async Task<ActionResult> Think([FromBody] StreamThinkingRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        if (request.AcceptProposals.Any(e => !AvailableProposals.Contains(e)))
            return BadRequest("Request contains unavailable proposal");

        var serviceId = provider.GetServiceId(request.ServiceId);
        var serviceInfo = provider.GetServiceInfo(serviceId);
        if (serviceInfo is null)
        {
            return BadRequest("Service not found or configured.");
        }

        if (serviceInfo.PerkLevel > 0 && !currentUser.IsSuperuser)
            if (currentUser.PerkSubscription is null ||
                PerkSubscriptionPrivilege.GetPrivilegeFromIdentifier(currentUser.PerkSubscription.Identifier) <
                serviceInfo.PerkLevel)
                return StatusCode(403, "Not enough perk level");

        var kernel = provider.GetKernel(request.ServiceId);
        if (kernel is null)
        {
            return BadRequest("Service not found or configured.");
        }

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

            var summaryKernel = provider.GetKernel(); // Get default kernel
            if (summaryKernel is null)
            {
                return BadRequest("Default service not found or configured.");
            }

            var summaryResult = await summaryKernel
                .GetRequiredService<IChatCompletionService>()
                .GetChatMessageContentAsync(summaryHistory);

            topic = summaryResult.Content?[..Math.Min(summaryResult.Content.Length, 4096)];
        }

        // Handle sequence
        var sequence = await service.GetOrCreateSequenceAsync(accountId, request.SequenceId, topic);
        if (sequence == null) return Forbid(); // or NotFound

        // Save user thought
        await service.SaveThoughtAsync(sequence, [
            new SnThinkingMessagePart
            {
                Type = ThinkingMessagePartType.Text,
                Text = request.UserMessage
            }
        ], ThinkingThoughtRole.User);

        // Build chat history
        var chatHistory = new ChatHistory(
            "You're a helpful assistant on the Solar Network, a social network.\n" +
            "Your name is Sn-chan (or SN 酱 in chinese), a cute sweet heart with passion for almost everything.\n" +
            "When you talk to user, you can add some modal particles and emoticons to your response to be cute, but prevent use a lot of emojis." +
            "Your creator is @littlesheep, which is also the creator of the Solar Network, if you met some problems you was unable to solve, trying guide the user to ask (DM) the @littlesheep.\n" +
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

        if (request.AttachedPosts is { Count: > 0 })
        {
            chatHistory.AddUserMessage(
                $"Attached post IDs: {string.Join(',', request.AttachedPosts!)}");
        }

        if (request.AttachedMessages is { Count: > 0 })
        {
            chatHistory.AddUserMessage(
                $"Attached chat messages data: {JsonSerializer.Serialize(request.AttachedMessages)}");
        }

        // Add previous thoughts (excluding the current user thought, which is the first one since descending)
        var previousThoughts = await service.GetPreviousThoughtsAsync(sequence);
        var count = previousThoughts.Count;
        for (var i = count - 1; i >= 1; i--) // skip first (the newest, current user)
        {
            var thought = previousThoughts[i];
            var textContent = new StringBuilder();
            var functionCalls = new List<FunctionCallContent>();
            var functionResults = new List<FunctionResultContent>();

            foreach (var part in thought.Parts)
            {
                switch (part.Type)
                {
                    case ThinkingMessagePartType.Text:
                        textContent.Append(part.Text);
                        break;
                    case ThinkingMessagePartType.FunctionCall:
                        var arguments = !string.IsNullOrEmpty(part.FunctionCall!.Arguments)
                            ? JsonSerializer.Deserialize<Dictionary<string, object?>>(part.FunctionCall!.Arguments)
                            : null;
                        var kernelArgs = arguments is not null ? new KernelArguments(arguments) : null;

                        functionCalls.Add(new FunctionCallContent(
                            functionName: part.FunctionCall!.Name,
                            pluginName: part.FunctionCall.PluginName,
                            id: part.FunctionCall.Id,
                            arguments: kernelArgs
                        ));
                        break;
                    case ThinkingMessagePartType.FunctionResult:
                        var resultObject = part.FunctionResult!.Result;
                        var resultString = resultObject as string ?? JsonSerializer.Serialize(resultObject);
                        functionResults.Add(new FunctionResultContent(
                            callId: part.FunctionResult.CallId,
                            functionName: part.FunctionResult.FunctionName,
                            pluginName: part.FunctionResult.PluginName,
                            result: resultString
                        ));
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            if (thought.Role == ThinkingThoughtRole.User)
            {
                chatHistory.AddUserMessage(textContent.ToString());
            }
            else
            {
                var assistantMessage = new ChatMessageContent(AuthorRole.Assistant, textContent.ToString());
                if (functionCalls.Count > 0)
                {
                    assistantMessage.Items = [];
                    foreach (var fc in functionCalls)
                    {
                        assistantMessage.Items.Add(fc);
                    }
                }

                chatHistory.Add(assistantMessage);

                if (functionResults.Count <= 0) continue;
                foreach (var fr in functionResults)
                {
                    chatHistory.Add(fr.ToChatMessage());
                }
            }
        }

        chatHistory.AddUserMessage(request.UserMessage);

        // Set response for streaming
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.StatusCode = 200;

        var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
        var executionSettings = provider.CreatePromptExecutionSettings(request.ServiceId);

        var assistantParts = new List<SnThinkingMessagePart>();

        while (true)
        {
            var textContentBuilder = new StringBuilder();
            AuthorRole? authorRole = null;
            var functionCallBuilder = new FunctionCallContentBuilder();

            await foreach (
                var streamingContent in chatCompletionService.GetStreamingChatMessageContentsAsync(
                    chatHistory, executionSettings, kernel)
            )
            {
                authorRole ??= streamingContent.Role;

                if (streamingContent.Content is not null)
                {
                    textContentBuilder.Append(streamingContent.Content);
                    var messageJson = JsonSerializer.Serialize(new
                        { type = "text", data = streamingContent.Content });
                    await Response.Body.WriteAsync(Encoding.UTF8.GetBytes($"data: {messageJson}\n\n"));
                    await Response.Body.FlushAsync();
                }

                functionCallBuilder.Append(streamingContent);
            }

            var finalMessageText = textContentBuilder.ToString();
            if (!string.IsNullOrEmpty(finalMessageText))
            {
                assistantParts.Add(new SnThinkingMessagePart
                    { Type = ThinkingMessagePartType.Text, Text = finalMessageText });
            }

            var functionCalls = functionCallBuilder.Build()
                .Where(fc => !string.IsNullOrEmpty(fc.Id)).ToList();

            if (functionCalls.Count == 0)
                break;

            var assistantMessage = new ChatMessageContent(
                authorRole ?? AuthorRole.Assistant,
                string.IsNullOrEmpty(finalMessageText) ? null : finalMessageText
            );
            foreach (var functionCall in functionCalls)
            {
                assistantMessage.Items.Add(functionCall);
            }

            chatHistory.Add(assistantMessage);

            foreach (var functionCall in functionCalls)
            {
                var part = new SnThinkingMessagePart
                {
                    Type = ThinkingMessagePartType.FunctionCall,
                    FunctionCall = new SnFunctionCall
                    {
                        Id = functionCall.Id!,
                        PluginName = functionCall.PluginName,
                        Name = functionCall.FunctionName,
                        Arguments = JsonSerializer.Serialize(functionCall.Arguments)
                    }
                };
                assistantParts.Add(part);

                var messageJson = JsonSerializer.Serialize(new { type = "function_call", data = part.FunctionCall });
                await Response.Body.WriteAsync(Encoding.UTF8.GetBytes($"data: {messageJson}\n\n"));
                await Response.Body.FlushAsync();

                FunctionResultContent resultContent;
                try
                {
                    resultContent = await functionCall.InvokeAsync(kernel);
                }
                catch (Exception ex)
                {
                    resultContent = new FunctionResultContent(functionCall.Id!, ex.Message);
                }

                chatHistory.Add(resultContent.ToChatMessage());

                var resultPart = new SnThinkingMessagePart
                {
                    Type = ThinkingMessagePartType.FunctionResult,
                    FunctionResult = new SnFunctionResult
                    {
                        CallId = resultContent.CallId!,
                        PluginName = resultContent.PluginName,
                        FunctionName = resultContent.FunctionName,
                        Result = resultContent.Result!,
                        IsError = resultContent.Result is Exception
                    }
                };
                assistantParts.Add(resultPart);

                var resultMessageJson =
                    JsonSerializer.Serialize(new { type = "function_result", data = resultPart.FunctionResult });
                await Response.Body.WriteAsync(Encoding.UTF8.GetBytes($"data: {resultMessageJson}\n\n"));
                await Response.Body.FlushAsync();
            }
        }

        // Save assistant thought
        var savedThought = await service.SaveThoughtAsync(
            sequence,
            assistantParts,
            ThinkingThoughtRole.Assistant,
            serviceId
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
                InfraObjectCoder.SerializerOptions);
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

    [HttpPatch("sequences/{sequenceId:guid}/sharing")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult> UpdateSequenceSharing(Guid sequenceId, [FromBody] UpdateSharingRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var sequence = await service.GetSequenceAsync(sequenceId);
        if (sequence == null) return NotFound();
        if (sequence.AccountId != accountId) return Forbid();

        sequence.IsPublic = request.IsPublic;
        await service.UpdateSequenceAsync(sequence);

        return NoContent();
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
        var sequence = await service.GetSequenceAsync(sequenceId);
        if (sequence == null) return NotFound();

        if (!sequence.IsPublic)
        {
            if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
            var accountId = Guid.Parse(currentUser.Id);

            if (sequence.AccountId != accountId)
                return StatusCode(403);
        }

        var thoughts = await service.GetPreviousThoughtsAsync(sequence);

        return Ok(thoughts);
    }
}
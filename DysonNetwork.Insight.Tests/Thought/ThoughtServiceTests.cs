using DysonNetwork.Insight.Services;
using DysonNetwork.Insight.Thought;
using DysonNetwork.Shared.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NodaTime;
using Xunit;

namespace DysonNetwork.Insight.Tests.Thought;

public class ThoughtServiceTests
{
    [Fact]
    public void IsMiChanCompactionThought_ReturnsTrueOnlyForMarkedMiChanThoughts()
    {
        var service = CreateService();
        var coveredThought = CreateTextThought(ThinkingThoughtRole.User, "michan", "older");
        var summaryThought = CreateSummaryThought("- summary", coveredThought.Id);
        var regularThought = CreateTextThought(ThinkingThoughtRole.Assistant, "michan", "normal reply");

        Assert.True(service.IsMiChanCompactionThought(summaryThought));
        Assert.False(service.IsMiChanCompactionThought(regularThought));
    }

    [Fact]
    public void FilterVisibleThoughts_HidesMiChanCompactionSummaries()
    {
        var service = CreateService();
        var coveredThought = CreateTextThought(ThinkingThoughtRole.User, "michan", "older");
        var summaryThought = CreateSummaryThought("- summary", coveredThought.Id);
        var visibleThought = CreateTextThought(ThinkingThoughtRole.Assistant, "michan", "visible");

        var visibleThoughts = service.FilterVisibleThoughts([coveredThought, summaryThought, visibleThought]);

        Assert.Equal(2, visibleThoughts.Count);
        Assert.DoesNotContain(visibleThoughts, thought => thought.Id == summaryThought.Id);
    }

    [Fact]
    public void MiChanCompaction_DoesNotCompactShortHistory()
    {
        var service = CreateService();
        var thoughts = Enumerable.Range(0, 3)
            .Select(index => CreateTextThought(ThinkingThoughtRole.User, "michan", $"short-{index}"))
            .ToList();

        Assert.False(service.ShouldCompactMiChanHistoryForTests(thoughts));
        Assert.Empty(service.SelectCompactionPrefixForTests(thoughts));
    }

    [Fact]
    public void MiChanCompaction_SelectsPrefixAndKeepsRecentTail()
    {
        var service = CreateService();
        var thoughts = Enumerable.Range(0, 12)
            .Select(index => CreateTextThought(
                index % 2 == 0 ? ThinkingThoughtRole.User : ThinkingThoughtRole.Assistant,
                "michan",
                new string((char)('a' + index), 5000)))
            .ToList();

        var prefix = service.SelectCompactionPrefixForTests(thoughts);

        Assert.True(service.ShouldCompactMiChanHistoryForTests(thoughts));
        Assert.NotEmpty(prefix);
        Assert.True(prefix.Count < thoughts.Count);
        Assert.DoesNotContain(thoughts[^1], prefix);
    }

    [Fact]
    public void MiChanCompaction_UsesLatestSummaryAndPreservesRecentToolThoughts()
    {
        var service = CreateService();
        var oldThought1 = CreateTextThought(ThinkingThoughtRole.User, "michan", "old-1");
        var oldThought2 = CreateTextThought(ThinkingThoughtRole.Assistant, "michan", "old-2");
        var summaryThought = CreateSummaryThought("- merged summary", oldThought2.Id);
        var recentToolThought = CreateToolThought();

        var (summary, recentThoughts) = service.ProjectMiChanHistoryWindowForTests([
            oldThought1,
            oldThought2,
            summaryThought,
            recentToolThought
        ]);

        Assert.Equal("- merged summary", summary);
        Assert.Single(recentThoughts);
        Assert.Equal(recentToolThought.Id, recentThoughts[0].Id);
        Assert.Contains(recentThoughts[0].Parts, part => part.Type == ThinkingMessagePartType.FunctionCall);
        Assert.Contains(recentThoughts[0].Parts, part => part.Type == ThinkingMessagePartType.FunctionResult);
    }

    [Fact]
    public void SliceVisibleThoughtsForTests_PaginatesAcrossHiddenSummaryThoughts()
    {
        var service = CreateService();
        var first = CreateTextThought(ThinkingThoughtRole.Assistant, "michan", "first");
        var covered = CreateTextThought(ThinkingThoughtRole.User, "michan", "covered");
        var summary = CreateSummaryThought("- summary", covered.Id);
        var second = CreateTextThought(ThinkingThoughtRole.Assistant, "michan", "second");
        var third = CreateTextThought(ThinkingThoughtRole.User, "michan", "third");

        var (page, hasMore) = service.SliceVisibleThoughtsForTests(
            [first, covered, summary, second, third],
            offset: 1,
            take: 2
        );

        Assert.True(hasMore);
        Assert.Equal([covered.Id, second.Id], page.Select(t => t.Id).ToList());
    }

    private static ThoughtService CreateService()
    {
        return new ThoughtService(
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            new ConfigurationBuilder().AddInMemoryCollection().Build(),
            null!,
            null!,
            new ConfigurationBuilder().AddInMemoryCollection().Build(),
            null!,
            new TokenCountingService(NullLogger<TokenCountingService>.Instance),
            NullLogger<ThoughtService>.Instance,
            null!
        );
    }

    private static SnThinkingThought CreateTextThought(
        ThinkingThoughtRole role,
        string botName,
        string text)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        return new SnThinkingThought
        {
            Id = Guid.NewGuid(),
            Role = role,
            BotName = botName,
            TokenCount = text.Length,
            CreatedAt = now,
            UpdatedAt = now,
            Parts =
            [
                new SnThinkingMessagePart
                {
                    Type = ThinkingMessagePartType.Text,
                    Text = text
                }
            ]
        };
    }

    private static SnThinkingThought CreateSummaryThought(string text, Guid coveredThoughtId)
    {
        var thought = CreateTextThought(ThinkingThoughtRole.Assistant, "michan", text);
        thought.Parts[0].Metadata = new Dictionary<string, object>
        {
            ["summary_kind"] = "compaction",
            ["covered_through_thought_id"] = coveredThoughtId.ToString()
        };
        return thought;
    }

    private static SnThinkingThought CreateToolThought()
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        return new SnThinkingThought
        {
            Id = Guid.NewGuid(),
            Role = ThinkingThoughtRole.Assistant,
            BotName = "michan",
            CreatedAt = now,
            UpdatedAt = now,
            Parts =
            [
                new SnThinkingMessagePart
                {
                    Type = ThinkingMessagePartType.FunctionCall,
                    FunctionCall = new SnFunctionCall
                    {
                        Id = Guid.NewGuid().ToString(),
                        PluginName = "memory",
                        Name = "store_memory",
                        Arguments = "{\"content\":\"remember\"}"
                    }
                },
                new SnThinkingMessagePart
                {
                    Type = ThinkingMessagePartType.FunctionResult,
                    FunctionResult = new SnFunctionResult
                    {
                        CallId = Guid.NewGuid().ToString(),
                        PluginName = "memory",
                        FunctionName = "store_memory",
                        Result = "ok",
                        IsError = false
                    }
                }
            ]
        };
    }
}

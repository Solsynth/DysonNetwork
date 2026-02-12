using System.Diagnostics.CodeAnalysis;
using System.Text;
using DysonNetwork.Insight.MiChan.Plugins;
using DysonNetwork.Insight.Thought.Memory;
using Microsoft.SemanticKernel;
using Quartz;

namespace DysonNetwork.Insight.MiChan;

[DisallowConcurrentExecution]
public class ScheduledTaskJob(
    ScheduledTaskService taskService,
    MiChanKernelProvider kernelProvider,
    MemoryService memoryService,
    MiChanConfig config,
    IServiceProvider serviceProvider,
    ILogger<ScheduledTaskJob> logger
) : IJob
{
    [Experimental("SKEXP0050")]
    public async Task Execute(IJobExecutionContext context)
    {
        logger.LogInformation("Starting scheduled task execution job.");

        var pendingTasks = await taskService.GetPendingTasksAsync(context.CancellationToken);

        if (pendingTasks.Count == 0)
        {
            logger.LogDebug("No pending scheduled tasks found.");
            return;
        }

        logger.LogInformation("Found {Count} pending scheduled tasks.", pendingTasks.Count);

        foreach (var task in pendingTasks)
        {
            if (context.CancellationToken.IsCancellationRequested)
                break;

            await ExecuteTaskDirectlyAsync(task, context.CancellationToken);
        }

        logger.LogInformation("Scheduled task execution job finished.");
    }

    [Experimental("SKEXP0050")]
    public async Task ExecuteTaskDirectlyAsync(MiChanScheduledTask task, CancellationToken cancellationToken)
    {
        logger.LogInformation("Executing scheduled task {TaskId} for account {AccountId}",
            task.Id, task.AccountId);

        try
        {
            await taskService.MarkAsRunningAsync(task, cancellationToken);

            var kernel = kernelProvider.GetKernel();
            RegisterPlugins(kernel);

            var prompt = await BuildPromptAsync(task, cancellationToken);

            var settings = kernelProvider.CreatePromptExecutionSettings(0.7);
            var result = await kernel.InvokePromptAsync<string>(
                prompt,
                new KernelArguments(settings),
                cancellationToken: cancellationToken
            );

            logger.LogInformation("Task {TaskId} completed with result: {Result}",
                task.Id, result?[..Math.Min(result.Length, 200)]);

            await taskService.MarkAsCompletedAsync(task, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to execute scheduled task {TaskId}", task.Id);
            await taskService.MarkAsFailedAsync(task, ex.Message, cancellationToken);
        }
    }

    private void RegisterPlugins(Kernel kernel)
    {
        if (!kernel.Plugins.Contains("post"))
        {
            var postPlugin = serviceProvider.GetRequiredService<PostPlugin>();
            kernel.Plugins.AddFromObject(postPlugin, "post");
        }

        if (!kernel.Plugins.Contains("account"))
        {
            var accountPlugin = serviceProvider.GetRequiredService<AccountPlugin>();
            kernel.Plugins.AddFromObject(accountPlugin, "account");
        }

        if (!kernel.Plugins.Contains("memory"))
        {
            var memoryPlugin = serviceProvider.GetRequiredService<MemoryPlugin>();
            kernel.Plugins.AddFromObject(memoryPlugin, "memory");
        }

        if (!kernel.Plugins.Contains("scheduledTasks"))
        {
            var taskPlugin = serviceProvider.GetRequiredService<ScheduledTaskPlugin>();
            kernel.Plugins.AddFromObject(taskPlugin, "scheduledTasks");
        }

        if (!kernel.Plugins.Contains("conversation"))
        {
            var conversationPlugin = serviceProvider.GetRequiredService<ConversationPlugin>();
            kernel.Plugins.AddFromObject(conversationPlugin, "conversation");
        }
    }

    private async Task<string> BuildPromptAsync(MiChanScheduledTask task, CancellationToken cancellationToken)
    {
        var personality = PersonalityLoader.LoadPersonality(config.PersonalityFile, config.Personality, logger);
        var mood = config.AutonomousBehavior.PersonalityMood;

        var builder = new StringBuilder();
        builder.AppendLine(personality);
        builder.AppendLine();
        builder.AppendLine($"当前心情: {mood}");
        builder.AppendLine();

        var hotMemories = await memoryService.GetHotMemory(
            accountId: task.AccountId,
            prompt: task.Prompt,
            limit: 5,
            cancellationToken: cancellationToken);

        if (hotMemories.Count > 0)
        {
            builder.AppendLine("相关记忆:");
            foreach (var memory in hotMemories)
                builder.AppendLine(memory.ToPrompt());
            builder.AppendLine();
        }

        var relevantMemories = await memoryService.SearchAsync(
            query: task.Prompt,
            accountId: task.AccountId,
            limit: 3,
            minSimilarity: 0.6,
            cancellationToken: cancellationToken);

        if (relevantMemories.Count > 0)
        {
            builder.AppendLine("上下文记忆:");
            foreach (var memory in relevantMemories)
                builder.AppendLine(memory.ToPrompt());
            builder.AppendLine();
        }

        if (!string.IsNullOrEmpty(task.Context))
        {
            builder.AppendLine("任务上下文:");
            builder.AppendLine(task.Context);
            builder.AppendLine();
        }

        builder.AppendLine("执行任务:");
        builder.AppendLine(task.Prompt);
        builder.AppendLine();
        builder.AppendLine("请执行上述任务。如果需要保存重要信息，请使用 store_memory 工具。");

        return builder.ToString();
    }
}

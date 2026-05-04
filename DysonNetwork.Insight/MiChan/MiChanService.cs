using DysonNetwork.Insight.MiChan.Plugins;
using DysonNetwork.Insight.Thought.Memory;
using DysonNetwork.Shared.Proto;

namespace DysonNetwork.Insight.MiChan;

public class MiChanService(
    MiChanConfig config,
    ILogger<MiChanService> logger,
    IServiceProvider serviceProvider,
    Thought.ThoughtService thoughtService
)
    : BackgroundService
{
    private MemoryService? _memoryService;
    private MiChanAutonomousBehavior? _autonomousBehavior;
    private SolarNetworkApiClient? _apiClient;
    private string? _cachedPersonality;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.Enabled)
        {
            logger.LogInformation("MiChan is disabled. Skipping initialization.");
            return;
        }

        if (string.IsNullOrEmpty(config.AccessToken) ||
            (string.IsNullOrEmpty(config.BotAccountId) && string.IsNullOrEmpty(config.BotAccountName)))
        {
            logger.LogWarning("MiChan is enabled but AccessToken or bot account identity is not configured.");
            return;
        }

        logger.LogInformation("Starting MiChan service...");

        // Wait 1 minute for other services to start
        logger.LogInformation("MiChan waiting 60 seconds for other services to initialize...");
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        // Initialize services
        await InitializeAsync(stoppingToken);

        // Start autonomous behavior loop
        if (config.AutonomousBehavior.Enabled)
        {
            _ = Task.Run(async () => await AutonomousLoopAsync(stoppingToken), stoppingToken);
        }

        // Keep the service running
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in MiChan service loop");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        logger.LogInformation("MiChan service stopped");
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ResolveBotAccountIdAsync(cancellationToken);

            // Create API client
            _apiClient = serviceProvider.GetRequiredService<SolarNetworkApiClient>();

            // Create memory service
            _memoryService = serviceProvider.GetRequiredService<MemoryService>();

            // Create autonomous behavior (includes post checking)
            _autonomousBehavior = serviceProvider.GetRequiredService<MiChanAutonomousBehavior>();
            await _autonomousBehavior.InitializeAsync();

            // Load personality from file if configured
            _cachedPersonality = PersonalityLoader.LoadPersonality(config.PersonalityFile, config.Personality, logger);

            logger.LogInformation("MiChan initialized successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize MiChan");
            throw;
        }
    }

    private async Task ResolveBotAccountIdAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(config.BotAccountId))
            return;

        if (string.IsNullOrEmpty(config.BotAccountName))
            throw new InvalidOperationException("MiChan BotAccountId or BotAccountName must be configured.");

        var accountClient = serviceProvider.GetRequiredService<DyProfileService.DyProfileServiceClient>();
        var request = new DyLookupAccountBatchRequest();
        request.Names.Add(config.BotAccountName);

        var response = await accountClient.LookupAccountBatchAsync(request, cancellationToken: cancellationToken);
        var account = response.Accounts.FirstOrDefault(a =>
            a.Name.Equals(config.BotAccountName, StringComparison.OrdinalIgnoreCase));

        if (account is null || string.IsNullOrEmpty(account.Id))
        {
            throw new InvalidOperationException(
                $"MiChan bot account '{config.BotAccountName}' was not found.");
        }

        config.BotAccountId = account.Id;
        logger.LogInformation("Resolved MiChan bot account name {BotAccountName} to account ID {BotAccountId}",
            config.BotAccountName, config.BotAccountId);
    }

    private async Task AutonomousLoopAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Starting autonomous behavior loop...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var executed = await _autonomousBehavior!.TryExecuteAutonomousActionAsync();

                if (executed)
                {
                    logger.LogInformation("Autonomous action executed successfully");
                }

                // Wait before checking again (5 minute interval check)
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in autonomous behavior loop");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }

        logger.LogInformation("Autonomous behavior loop stopped");
    }

    /// <summary>
    /// Gets or creates a thought sequence and memorizes it using the embedding service.
    /// </summary>
    /// <param name="accountId">The account ID for the thought sequence</param>
    /// <param name="sequenceId">Optional existing sequence ID to retrieve</param>
    /// <param name="topic">Optional topic for new sequences</param>
    /// <param name="contextId">The context ID for memory storage (e.g., chat room ID)</param>
    /// <param name="additionalContext">Optional additional context to store with the memory</param>
    /// <returns>The thought sequence ID if successful, null otherwise</returns>
    public async Task<Guid?> GetAndMemorizeThoughtSequenceAsync(
        Guid accountId,
        Guid? sequenceId = null,
        string? topic = null,
        string? contextId = null,
        Dictionary<string, object>? additionalContext = null)
    {
        try
        {
            // Get or create the thought sequence
            var sequence = await thoughtService.GetOrCreateSequenceAsync(accountId, sequenceId, topic);
            if (sequence == null)
            {
                logger.LogWarning("Failed to get or create thought sequence for account {AccountId}", accountId);
                return null;
            }

            // Prepare context for memory storage
            var memoryContext = new Dictionary<string, object>
            {
                ["sequence_id"] = sequence.Id,
                ["account_id"] = accountId,
                ["topic"] = topic ?? "No topic",
                ["total_tokens"] = sequence.TotalToken,
                ["created_at"] = sequence.CreatedAt,
                ["timestamp"] = DateTime.UtcNow
            };

            // Add any additional context provided
            if (additionalContext != null)
            {
                foreach (var kvp in additionalContext)
                {
                    memoryContext[kvp.Key] = kvp.Value;
                }
            }

            // Store in memory service
            await _memoryService!.StoreMemoryAsync(
                "thought_sequence",
                $"Sequence {sequence.Id}: {topic ?? "No topic"} - {memoryContext}",
                hot: false
            );

            logger.LogInformation(
                "Successfully memorized thought sequence {SequenceId} for account {AccountId} with topic: {Topic}",
                sequence.Id,
                accountId,
                topic ?? "No topic"
            );

            return sequence.Id;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting and memorizing thought sequence for account {AccountId}", accountId);
            return null;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("MiChan service stopping...");

        await base.StopAsync(cancellationToken);
    }
}

#pragma warning disable SKEXP0050
using DysonNetwork.Insight.MiChan.Plugins;
using DysonNetwork.Insight.Thought.Memory;
using Microsoft.SemanticKernel;

namespace DysonNetwork.Insight.MiChan;

public class MiChanService(
    MiChanConfig config,
    ILogger<MiChanService> logger,
    IServiceProvider serviceProvider,
    Thought.ThoughtService thoughtService
)
    : BackgroundService
{
    private MiChanKernelProvider? _kernelProvider;
    private MemoryService? _memoryService;
    private MiChanAutonomousBehavior? _autonomousBehavior;
    private SolarNetworkApiClient? _apiClient;
    private Kernel? _kernel;
    private string? _cachedPersonality;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.Enabled)
        {
            logger.LogInformation("MiChan is disabled. Skipping initialization.");
            return;
        }

        if (string.IsNullOrEmpty(config.AccessToken) || string.IsNullOrEmpty(config.BotAccountId))
        {
            logger.LogWarning("MiChan is enabled but AccessToken or BotAccountId is not configured.");
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
            // Create API client
            _apiClient = serviceProvider.GetRequiredService<SolarNetworkApiClient>();

            // Create memory service
            _memoryService = serviceProvider.GetRequiredService<MemoryService>();

            // Create kernel provider and get kernel
            _kernelProvider = serviceProvider.GetRequiredService<MiChanKernelProvider>();
            _kernel = _kernelProvider.GetKernel();

            // Register plugins using centralized extension method
            _kernel.AddMiChanPlugins(serviceProvider);

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
#pragma warning restore SKEXP0050
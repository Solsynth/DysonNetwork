#pragma warning disable SKEXP0050

using System.Diagnostics.CodeAnalysis;
using DysonNetwork.Insight.Agent.Models;
using DysonNetwork.Insight.MiChan.Plugins;
using DysonNetwork.Insight.Thought.Plugins;
using DysonNetwork.Shared.Proto;
using Microsoft.SemanticKernel;

namespace DysonNetwork.Insight.MiChan.KernelBuilding;

/// <summary>
/// Extension methods for IKernelBuilder that provide MiChan-specific functionality.
/// These extend the global AI kernel builder with MiChan-specific configurations.
/// </summary>
public static class MiChanKernelBuilderExtensions
{
    /// <summary>
    /// Sets the model from MiChanConfig (uses ThinkingModel)
    /// </summary>
    public static Agent.KernelBuilding.IKernelBuilder WithMiChanModel(this Agent.KernelBuilding.IKernelBuilder builder, MiChanConfig config)
    {
        return builder.WithModel(config.ThinkingModel);
    }

    /// <summary>
    /// Sets the autonomous model from MiChanConfig
    /// </summary>
    public static Agent.KernelBuilding.IKernelBuilder WithMiChanAutonomousModel(this Agent.KernelBuilding.IKernelBuilder builder, MiChanConfig config)
    {
        return builder.WithModel(config.GetAutonomousModel());
    }

    /// <summary>
    /// Sets the vision model from MiChanConfig
    /// </summary>
    public static Agent.KernelBuilding.IKernelBuilder WithMiChanVisionModel(this Agent.KernelBuilding.IKernelBuilder builder, MiChanConfig config)
    {
        return builder.WithModel(new ModelConfiguration
        {
            ModelId = config.Vision.VisionThinkingService,
            Temperature = 0.7,
            EnableFunctions = false
        });
    }

    /// <summary>
    /// Adds MiChan-specific plugins (memory, user profile, etc.)
    /// </summary>
    [Experimental("SKEXP0050")]
    public static Agent.KernelBuilding.IKernelBuilder WithMiChanPlugins(this Agent.KernelBuilding.IKernelBuilder builder, IServiceProvider serviceProvider)
    {
        return builder.WithPlugins(kernel =>
        {
            kernel.AddMiChanPlugins(serviceProvider);
        });
    }

    /// <summary>
    /// Adds SN-chan-specific plugins (account, post, etc.)
    /// </summary>
    [Experimental("SKEXP0050")]
    public static Agent.KernelBuilding.IKernelBuilder WithSnChanPlugins(this Agent.KernelBuilding.IKernelBuilder builder, IServiceProvider serviceProvider)
    {
        return builder.WithPlugins(kernel =>
        {
            // Add SN-chan specific plugins
            var accountClient = serviceProvider.GetRequiredService<DyAccountService.DyAccountServiceClient>();
            var postClient = serviceProvider.GetRequiredService<DyPostService.DyPostServiceClient>();
            var publisherClient = serviceProvider.GetRequiredService<DyPublisherService.DyPublisherServiceClient>();

            kernel.Plugins.AddFromObject(new SnAccountKernelPlugin(accountClient));
            kernel.Plugins.AddFromObject(new SnPostKernelPlugin(postClient, publisherClient));
        });
    }

    /// <summary>
    /// Creates a kernel for MiChan chat conversations
    /// </summary>
    public static Agent.KernelBuilding.IKernelBuilder ForMiChanChat(this Agent.KernelBuilding.IKernelBuilder builder, MiChanConfig config, IServiceProvider serviceProvider)
    {
        return builder
            .WithMiChanModel(config)
            .WithEmbeddings(true)
            .WithWebSearch(true)
            .WithMiChanPlugins(serviceProvider)
            .WithServiceProvider(serviceProvider);
    }

    /// <summary>
    /// Creates a kernel for MiChan autonomous behavior
    /// </summary>
    public static Agent.KernelBuilding.IKernelBuilder ForMiChanAutonomous(this Agent.KernelBuilding.IKernelBuilder builder, MiChanConfig config, IServiceProvider serviceProvider)
    {
        return builder
            .WithMiChanAutonomousModel(config)
            .WithEmbeddings(true)
            .WithWebSearch(true)
            .WithMiChanPlugins(serviceProvider)
            .WithServiceProvider(serviceProvider);
    }

    /// <summary>
    /// Creates a kernel for MiChan vision analysis
    /// </summary>
    public static Agent.KernelBuilding.IKernelBuilder ForMiChanVision(this Agent.KernelBuilding.IKernelBuilder builder, MiChanConfig config)
    {
        return builder
            .WithMiChanVisionModel(config)
            .WithEmbeddings(false)
            .WithWebSearch(false);
    }

    /// <summary>
    /// Creates a kernel for SN-chan conversations
    /// </summary>
    public static Agent.KernelBuilding.IKernelBuilder ForSnChanChat(this Agent.KernelBuilding.IKernelBuilder builder, ModelConfiguration model, IServiceProvider serviceProvider)
    {
        return builder
            .WithModel(model)
            .WithEmbeddings(false)
            .WithWebSearch(true)
            .WithSnChanPlugins(serviceProvider)
            .WithServiceProvider(serviceProvider);
    }

    /// <summary>
    /// Creates a kernel for topic generation
    /// </summary>
    public static Agent.KernelBuilding.IKernelBuilder ForTopicGeneration(this Agent.KernelBuilding.IKernelBuilder builder, MiChanConfig config)
    {
        return builder
            .WithModel(config.GetTopicGenerationModel())
            .WithEmbeddings(false)
            .WithWebSearch(false);
    }

    /// <summary>
    /// Creates a kernel for conversation compaction
    /// </summary>
    public static Agent.KernelBuilding.IKernelBuilder ForCompaction(this Agent.KernelBuilding.IKernelBuilder builder, MiChanConfig config)
    {
        return builder
            .WithModel(config.GetCompactionModel())
            .WithEmbeddings(false)
            .WithWebSearch(false)
            .WithTemperature(0.5);
    }
}

#pragma warning restore SKEXP0050

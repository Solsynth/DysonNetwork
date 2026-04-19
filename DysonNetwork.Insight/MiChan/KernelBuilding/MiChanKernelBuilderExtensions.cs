#pragma warning disable SKEXP0050

using System.Diagnostics.CodeAnalysis;
using DysonNetwork.Insight.Agent.Models;
using DysonNetwork.Insight.MiChan.Plugins;
using DysonNetwork.Insight.SnChan.Plugins;
using DysonNetwork.Insight.SnDoc;
using DysonNetwork.Insight.Thought.Memory;
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
            // Add MiChan plugins only if not already added
            if (!kernel.Plugins.Any(p => p.Name == "MiChan"))
            {
                kernel.AddMiChanPlugins(serviceProvider);
            }

            // Add SnDoc plugin for documentation search
            if (!kernel.Plugins.Any(p => p.Name == "SnDoc"))
            {
                var snDocPlugin = serviceProvider.GetRequiredService<SnDocPlugin>();
                kernel.Plugins.AddFromObject(snDocPlugin, "SnDoc");
            }
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
            // Add SN-chan specific plugins only if not already added
            var accountClient = serviceProvider.GetRequiredService<DyAccountService.DyAccountServiceClient>();
            var postClient = serviceProvider.GetRequiredService<DyPostService.DyPostServiceClient>();
            var publisherClient = serviceProvider.GetRequiredService<DyPublisherService.DyPublisherServiceClient>();

            // Check if plugins already exist to avoid duplicates
            if (!kernel.Plugins.Any(p => p.Name == "SnAccountKernel"))
            {
                kernel.Plugins.AddFromObject(new SnAccountKernelPlugin(accountClient), "SnAccountKernel");
            }
            if (!kernel.Plugins.Any(p => p.Name == "SnPostKernel"))
            {
                kernel.Plugins.AddFromObject(new SnPostKernelPlugin(postClient, publisherClient), "SnPostKernel");
            }

            // Add SnChan bot-specific plugins for post creation (uses separate bot authentication)
            if (!kernel.Plugins.Any(p => p.Name == "SnChanPost"))
            {
                var snChanApiClient = serviceProvider.GetRequiredService<SnChan.SnChanApiClient>();
                var snChanMoodService = serviceProvider.GetService<SnChan.SnChanMoodService>();
                var snChanPublisherService = serviceProvider.GetRequiredService<SnChan.SnChanPublisherService>();
                var logger = serviceProvider.GetRequiredService<ILogger<SnChan.Plugins.SnChanPostPlugin>>();
                kernel.Plugins.AddFromObject(new SnChan.Plugins.SnChanPostPlugin(snChanApiClient, snChanMoodService, snChanPublisherService, logger), "SnChanPost");
            }

            // Add SnChan Swagger plugin for API documentation
            if (!kernel.Plugins.Any(p => p.Name == "SnChanSwagger"))
            {
                var snChanApiClient = serviceProvider.GetRequiredService<SnChan.SnChanApiClient>();
                var logger = serviceProvider.GetRequiredService<ILogger<SnChan.Plugins.SnChanSwaggerPlugin>>();
                kernel.Plugins.AddFromObject(new SnChan.Plugins.SnChanSwaggerPlugin(snChanApiClient, logger), "SnChanSwagger");
            }

            // Add SnChan mood plugin
            if (!kernel.Plugins.Any(p => p.Name == "SnChanMood"))
            {
                var snChanMoodService = serviceProvider.GetRequiredService<SnChan.SnChanMoodService>();
                var logger = serviceProvider.GetRequiredService<ILogger<SnChan.Plugins.SnChanMoodPlugin>>();
                kernel.Plugins.AddFromObject(new SnChan.Plugins.SnChanMoodPlugin(snChanMoodService, logger), "SnChanMood");
            }

            // Add SnChan user profile plugin
            if (!kernel.Plugins.Any(p => p.Name == "SnChanUserProfile"))
            {
                var userProfileService = serviceProvider.GetRequiredService<UserProfileService>();
                var logger = serviceProvider.GetRequiredService<ILogger<SnChan.Plugins.SnChanUserProfilePlugin>>();
                kernel.Plugins.AddFromObject(new SnChan.Plugins.SnChanUserProfilePlugin(userProfileService, logger), "SnChanUserProfile");
            }

            // Add SnDoc plugin for documentation search
            if (!kernel.Plugins.Any(p => p.Name == "SnDoc"))
            {
                var snDocPlugin = serviceProvider.GetRequiredService<SnDocPlugin>();
                kernel.Plugins.AddFromObject(snDocPlugin, "SnDoc");
            }
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

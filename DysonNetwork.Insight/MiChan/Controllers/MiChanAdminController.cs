#pragma warning disable SKEXP0050
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DysonNetwork.Insight.MiChan.Plugins;
using DysonNetwork.Insight.Thought.Memory;
using DysonNetwork.Shared.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace DysonNetwork.Insight.MiChan.Controllers;

[ApiController]
[Route("/api/michan")]
public class MiChanAdminController(
    MiChanConfig config,
    ILogger<MiChanAdminController> logger,
    MemoryService memoryService,
    MiChanKernelProvider kernelProvider,
    IServiceProvider serviceProvider,
    MiChanAutonomousBehavior autonomousBehavior)
    : ControllerBase
{
    /// <summary>
    /// Test a personality prompt without changing the config
    /// </summary>
    [HttpGet("personality")]
    [Experimental("SKEXP0050")]
    [AskPermission("michan.admin")]
    public async Task<ActionResult> GetPersonality()
    {
        var personality = !string.IsNullOrWhiteSpace(config.PersonalityFile)
            ? PersonalityLoader.LoadPersonality(config.PersonalityFile, config.Personality, logger)
            : config.Personality;

        return Ok(new
        {
            personality
        });
    }

    /// <summary>
    /// Trigger MiChan's autonomous behavior immediately
    /// </summary>
    [HttpPost("trigger")]
    [AskPermission("michan.admin")]
    public async Task<ActionResult> TriggerAutonomousBehavior()
    {
        if (!config.Enabled)
        {
            return BadRequest(new { error = "MiChan is currently disabled" });
        }

        if (!config.AutonomousBehavior.Enabled)
        {
            return BadRequest(new { error = "Autonomous behavior is currently disabled" });
        }

        try
        {
            logger.LogInformation("Admin triggered autonomous behavior");
            var executed = await autonomousBehavior.TryExecuteAutonomousActionAsync();

            if (executed)
            {
                return Ok(new
                {
                    success = true,
                    message = "Autonomous behavior executed successfully",
                    timestamp = DateTime.UtcNow
                });
            }
            else
            {
                return Ok(new
                {
                    success = true,
                    message = "Autonomous behavior check completed (no action taken - may be on cooldown)",
                    timestamp = DateTime.UtcNow
                });
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error triggering autonomous behavior");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

#pragma warning restore SKEXP0050
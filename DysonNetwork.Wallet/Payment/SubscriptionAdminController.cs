using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using DysonNetwork.Wallet.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Wallet.Payment;

[ApiController]
[Route("/api/admin/subscriptions")]
[Authorize]
public class SubscriptionAdminController(
    AppDatabase db,
    SubscriptionService subscriptions,
    SubscriptionCatalogService catalog
) : ControllerBase
{
    public class UpsertSubscriptionDefinitionRequest
    {
        [Required] public string Identifier { get; set; } = null!;
        public string? GroupIdentifier { get; set; }
        [Required] public string DisplayName { get; set; } = null!;
        [Required] public string Currency { get; set; } = null!;
        public decimal BasePrice { get; set; }
        public int PerkLevel { get; set; }
        public int? MinimumAccountLevel { get; set; }
        public decimal? ExperienceMultiplier { get; set; }
        public int? GoldenPointReward { get; set; }
        public SubscriptionDisplayConfig? DisplayConfig { get; set; }
        public SubscriptionPaymentPolicy PaymentPolicy { get; set; } = new();
        public SubscriptionGiftPolicy? GiftPolicy { get; set; }
        public Dictionary<string, List<string>> ProviderMappings { get; set; } = [];
        public string? AppIdentifier { get; set; }
    }

    public class MaintenanceJobRequest
    {
        public int BatchSize { get; set; } = 100;
    }

    public class MaintenanceJobResponse
    {
        public int AffectedCount { get; set; }
    }

    [HttpGet]
    [AskPermission(PermissionKeys.SubscriptionsOrderManage)]
    public async Task<ActionResult<List<SnWalletSubscription>>> ListSubscriptions(
        [FromQuery] Guid? accountId = null,
        [FromQuery] string? identifier = null,
        [FromQuery] SubscriptionStatus? status = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] bool? isTesting = null,
        [FromQuery] int take = 50,
        [FromQuery] int offset = 0
    )
    {
        take = Math.Clamp(take, 1, 200);
        offset = Math.Max(0, offset);

        var query = db.WalletSubscriptions
            .AsNoTracking()
            .Include(x => x.Coupon)
            .AsQueryable();

        if (accountId.HasValue)
            query = query.Where(x => x.AccountId == accountId.Value);
        if (!string.IsNullOrWhiteSpace(identifier))
            query = query.Where(x => x.Identifier == identifier);
        if (status.HasValue)
            query = query.Where(x => x.Status == status.Value);
        if (isActive.HasValue)
            query = query.Where(x => x.IsActive == isActive.Value);
        if (isTesting.HasValue)
            query = query.Where(x => x.IsTesting == isTesting.Value);

        var total = await query.CountAsync(HttpContext.RequestAborted);
        Response.Headers.Append("X-Total", total.ToString());

        var items = await query
            .OrderByDescending(x => x.BegunAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync(HttpContext.RequestAborted);

        return Ok(items);
    }

    [HttpGet("catalog")]
    [AskPermission(PermissionKeys.SubscriptionsGroupsManage)]
    public async Task<ActionResult<List<SnWalletSubscriptionDefinition>>> ListCatalog()
    {
        return Ok(await catalog.ListDefinitionsAsync(HttpContext.RequestAborted));
    }

    [HttpGet("catalog/{identifier}")]
    [AskPermission(PermissionKeys.SubscriptionsGroupsManage)]
    public async Task<ActionResult<SnWalletSubscriptionDefinition>> GetCatalogItem(string identifier)
    {
        var definition = await catalog.GetDefinitionAsync(identifier, HttpContext.RequestAborted);
        if (definition is null)
            return NotFound(new ApiError { Code = "WALLET_SUBSCRIPTION_DEFINITION_NOT_FOUND", Message = "Subscription definition was not found.", Status = 404 });

        return Ok(definition);
    }

    [HttpPost("catalog")]
    [AskPermission(PermissionKeys.SubscriptionsGroupsManage)]
    public async Task<ActionResult<SnWalletSubscriptionDefinition>> UpsertCatalogItem(
        [FromBody] UpsertSubscriptionDefinitionRequest request
    )
    {
        var (definition, created) = await catalog.UpsertDefinitionAsync(
            new SnWalletSubscriptionDefinition
            {
                Identifier = request.Identifier,
                GroupIdentifier = request.GroupIdentifier,
                DisplayName = request.DisplayName,
                Currency = request.Currency,
                BasePrice = request.BasePrice,
                PerkLevel = request.PerkLevel,
                MinimumAccountLevel = request.MinimumAccountLevel,
                ExperienceMultiplier = request.ExperienceMultiplier,
                GoldenPointReward = request.GoldenPointReward,
                DisplayConfig = request.DisplayConfig,
                PaymentPolicy = request.PaymentPolicy,
                GiftPolicy = request.GiftPolicy,
                ProviderMappings = request.ProviderMappings,
                AppIdentifier = request.AppIdentifier
            },
            HttpContext.RequestAborted
        );

        if (created)
            return Created($"/api/admin/subscriptions/catalog/{definition.Identifier}", definition);

        return Ok(definition);
    }

    [HttpDelete("catalog/{identifier}")]
    [AskPermission(PermissionKeys.SubscriptionsGroupsManage)]
    public async Task<IActionResult> DeleteCatalogItem(string identifier)
    {
        var deleted = await catalog.DeleteDefinitionAsync(identifier, HttpContext.RequestAborted);
        if (!deleted)
            return NotFound(new ApiError { Code = "WALLET_SUBSCRIPTION_DEFINITION_NOT_FOUND", Message = "Subscription definition was not found.", Status = 404 });

        return NoContent();
    }

    [HttpPost("maintenance/update-expired")]
    [AskPermission(PermissionKeys.SubscriptionsOrderManage)]
    public async Task<ActionResult<MaintenanceJobResponse>> UpdateExpiredSubscriptions(
        [FromBody] MaintenanceJobRequest? request = null
    )
    {
        var affected = await subscriptions.UpdateExpiredSubscriptionsAsync(
            Math.Clamp(request?.BatchSize ?? 100, 1, 1000)
        );

        return Ok(new MaintenanceJobResponse { AffectedCount = affected });
    }

    [HttpPost("maintenance/activate-pending")]
    [AskPermission(PermissionKeys.SubscriptionsOrderManage)]
    public async Task<ActionResult<MaintenanceJobResponse>> ActivatePendingSubscriptions(
        [FromBody] MaintenanceJobRequest? request = null
    )
    {
        var affected = await subscriptions.ActivatePendingSubscriptionsAsync(
            Math.Clamp(request?.BatchSize ?? 100, 1, 1000),
            HttpContext.RequestAborted
        );

        return Ok(new MaintenanceJobResponse { AffectedCount = affected });
    }

    [HttpPost("maintenance/cancel-unavailable-in-app-wallet")]
    [AskPermission(PermissionKeys.SubscriptionsOrderManage)]
    public async Task<ActionResult<MaintenanceJobResponse>> CancelUnavailableInAppWalletSubscriptions()
    {
        var affected = await subscriptions.CancelUnavailableInAppWalletSubscriptionsAsync(HttpContext.RequestAborted);
        return Ok(new MaintenanceJobResponse { AffectedCount = affected });
    }
}

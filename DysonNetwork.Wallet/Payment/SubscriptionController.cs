using Microsoft.AspNetCore.Authorization;
using DysonNetwork.Wallet.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using System.Security.Cryptography;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using DysonNetwork.Wallet.Payment.PaymentHandlers;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Capabilities;
using DysonNetwork.Shared.Extensions;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;

namespace DysonNetwork.Wallet.Payment;

[ApiController]
[Route("/api/subscriptions")]
[ApiFeature("subscriptions", Revision = 1)]
public class SubscriptionController(
    SubscriptionService subscriptions,
    WalletProductService walletProducts,
    SubscriptionCatalogService catalog,
    AfdianPaymentHandler afdian,
    AppleStorePaymentHandler appleStore,
    PaddlePaymentHandler paddle,
    AppDatabase db,
    RemoteActionLogService als
)
    : ControllerBase
{
    public class SubscriptionCatalogItem
    {
        public string Identifier { get; set; } = null!;
        public string? GroupIdentifier { get; set; }
        public string DisplayName { get; set; } = null!;
        public string Currency { get; set; } = null!;
        public decimal BasePrice { get; set; }
        public int PerkLevel { get; set; }
        public int? MinimumAccountLevel { get; set; }
        public decimal? ExperienceMultiplier { get; set; }
        public int? GoldenPointReward { get; set; }
        public SubscriptionDisplayConfig? DisplayConfig { get; set; }
        public List<string> AllowedPaymentMethods { get; set; } = [];
        public Dictionary<string, List<string>> ProviderMappings { get; set; } = [];
    }

    public class SubscriptionGroupCatalogItem
    {
        public string GroupIdentifier { get; set; } = null!;
        public string DisplayName { get; set; } = null!;
        public int MaxPerkLevel { get; set; }
        public SubscriptionDisplayConfig? DisplayConfig { get; set; }
        public List<SubscriptionCatalogItem> Items { get; set; } = [];
    }

    public class SubscriptionGroupStateItem
    {
        public SnSubscriptionReferenceObject Subscription { get; set; } = null!;
        public SubscriptionCatalogItem? Definition { get; set; }
    }

    public class SubscriptionGroupStateResponse
    {
        public string GroupIdentifier { get; set; } = null!;
        public SubscriptionGroupCatalogItem Catalog { get; set; } = null!;
        public SubscriptionGroupStateItem? Current { get; set; }
        public SubscriptionGroupStateItem? Next { get; set; }
        public List<SubscriptionGroupStateItem> Subscriptions { get; set; } = [];
    }

    public class PendingActivationListResponse
    {
        public int TotalCount { get; set; }
        public Instant? NextActivationAt { get; set; }
        public List<SnWalletSubscription> Subscriptions { get; set; } = [];
    }

    public class ActivateSubscriptionRequest
    {
        [Required] public Guid SubscriptionId { get; set; }
    }

    [HttpGet("catalog")]
    public async Task<ActionResult<List<SubscriptionCatalogItem>>> ListCatalog()
    {
        var definitions = await catalog.ListDefinitionsAsync(HttpContext.RequestAborted);
        return Ok(definitions.Select(MapCatalogItem).ToList());
    }

    [HttpGet("groups")]
    public async Task<ActionResult<List<SubscriptionGroupCatalogItem>>> ListCatalogGroups()
    {
        var groups = await catalog.ListDefinitionGroupsAsync(HttpContext.RequestAborted);
        return Ok(groups.Select(MapCatalogGroup).ToList());
    }

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<SnWalletSubscription>>> ListSubscriptions(
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var query = db.WalletSubscriptions.AsQueryable()
            .Where(s => s.AccountId == Guid.Parse(currentUser.Id))
            .Include(s => s.Coupon)
            .OrderByDescending(s => s.BegunAt);

        var totalCount = await query.CountAsync();

        var subscriptionsList = await query
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        Response.Headers["X-Total"] = totalCount.ToString();

        return subscriptionsList;
    }

    [HttpGet("pending-activations")]
    [Authorize]
    public async Task<ActionResult<PendingActivationListResponse>> ListPendingActivations(
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var (totalCount, items) = await subscriptions.GetPendingActivationsAsync(
            Guid.Parse(currentUser.Id),
            offset,
            take,
            HttpContext.RequestAborted
        );

        return Ok(new PendingActivationListResponse
        {
            TotalCount = totalCount,
            NextActivationAt = items.Count == 0 ? null : items.Min(s => s.BegunAt),
            Subscriptions = items
        });
    }

    [HttpGet("fuzzy/{prefix}")]
    [Authorize]
    public async Task<ActionResult<SubscriptionGroupStateResponse>> GetSubscriptionFuzzy(string prefix)
    {
        return await GetSubscriptionGroup(prefix);
    }

    [HttpGet("groups/{groupIdentifier}")]
    [Authorize]
    public async Task<ActionResult<SubscriptionGroupStateResponse>> GetSubscriptionGroup(string groupIdentifier)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var definitions = await catalog.ListDefinitionsByGroupAsync(groupIdentifier, HttpContext.RequestAborted);
        if (definitions.Count == 0) return NotFound(new ApiError { Code = "WALLET_SUBSCRIPTION_GROUP_NOT_FOUND", Message = $"Subscription group {groupIdentifier} was not found.", Status = 404 });

        var accountId = Guid.Parse(currentUser.Id);
        var identifiers = definitions.Select(x => x.Identifier).ToList();
        var definitionMap = definitions.ToDictionary(x => x.Identifier, StringComparer.OrdinalIgnoreCase);
        var subscriptionsInGroup = await db.WalletSubscriptions
            .Where(s => s.AccountId == accountId)
            .Where(s => identifiers.Contains(s.Identifier))
            .Include(s => s.Coupon)
            .OrderBy(s => s.BegunAt)
            .ToListAsync();
        await HydrateSubscriptionMetadataAsync(subscriptionsInGroup, definitionMap);

        var now = SystemClock.Instance.GetCurrentInstant();
        var current = subscriptionsInGroup
            .Where(s => s.IsAvailableAt(now))
            .OrderByDescending(s => s.PerkLevel)
            .ThenByDescending(s => s.BegunAt)
            .FirstOrDefault();
        var next = subscriptionsInGroup
            .Where(s => s.IsActive)
            .Where(s => s.Status == SubscriptionStatus.Active)
            .Where(s => s.BegunAt > now)
            .OrderBy(s => s.BegunAt)
            .FirstOrDefault();

        return Ok(new SubscriptionGroupStateResponse
        {
            GroupIdentifier = string.IsNullOrWhiteSpace(definitions[0].GroupIdentifier)
                ? definitions[0].Identifier
                : definitions[0].GroupIdentifier!,
            Catalog = MapCatalogGroup(definitions),
            Current = current is null ? null : MapGroupStateItem(current, definitionMap),
            Next = next is null ? null : MapGroupStateItem(next, definitionMap),
            Subscriptions = subscriptionsInGroup
                .OrderByDescending(s => s.BegunAt)
                .Select(s => MapGroupStateItem(s, definitionMap))
                .ToList()
        });
    }

    [HttpPost("groups/{groupIdentifier}/activate")]
    [Authorize]
    [AskPermission(PermissionKeys.SubscriptionsGroupsManage)]
    public async Task<ActionResult<SubscriptionGroupStateResponse>> ActivateSubscriptionInGroup(
        string groupIdentifier,
        [FromBody] ActivateSubscriptionRequest request
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        try
        {
            var accountId = Guid.Parse(currentUser.Id);
            var target = await subscriptions.SwitchSubscriptionActivationAsync(
                accountId,
                groupIdentifier,
                request.SubscriptionId,
                HttpContext.RequestAborted
            );

            als.CreateActionLog(
                accountId,
                "subscriptions.activate_switch",
                new Dictionary<string, object>
                {
                    { "group_identifier", groupIdentifier },
                    { "subscription_id", target.Id.ToString() },
                    { "subscription_identifier", target.Identifier }
                },
                userAgent: Request.Headers.UserAgent,
                ipAddress: Request.GetClientIpAddress()
            );

            return await GetSubscriptionGroup(groupIdentifier);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiError { Code = "WALLET_SUBSCRIPTION_ACTIVATE_FAILED", Message = ex.Message, Status = 400 });
        }
    }

    [HttpGet("groups/{groupIdentifier}/active")]
    [Authorize]
    public async Task<ActionResult<SnWalletSubscription>> GetActiveSubscriptionInGroup(string groupIdentifier)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var definitions = await catalog.ListDefinitionsByGroupAsync(groupIdentifier, HttpContext.RequestAborted);
        if (definitions.Count == 0) return NotFound(new ApiError { Code = "WALLET_SUBSCRIPTION_GROUP_NOT_FOUND", Message = $"Subscription group {groupIdentifier} was not found.", Status = 404 });

        var accountId = Guid.Parse(currentUser.Id);
        var identifiers = definitions.Select(x => x.Identifier).ToList();
        var definitionMap = definitions.ToDictionary(x => x.Identifier, StringComparer.OrdinalIgnoreCase);
        var subscriptionsInGroup = await db.WalletSubscriptions
            .Where(s => s.AccountId == accountId)
            .Where(s => identifiers.Contains(s.Identifier))
            .Include(s => s.Coupon)
            .ToListAsync();
        await HydrateSubscriptionMetadataAsync(subscriptionsInGroup, definitionMap);

        var now = SystemClock.Instance.GetCurrentInstant();
        var active = subscriptionsInGroup
            .Where(s => s.IsAvailableAt(now))
            .OrderByDescending(s => s.PerkLevel)
            .ThenByDescending(s => s.BegunAt)
            .FirstOrDefault();
        if (active is null) return NotFound(new ApiError { Code = "WALLET_SUBSCRIPTION_NOT_FOUND", Message = "No active subscription found in this group.", Status = 404 });

        return Ok(active);
    }

    [HttpGet("{identifier}")]
    [Authorize]
    public async Task<ActionResult<SnWalletSubscription>> GetSubscription(string identifier)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var subscription = await subscriptions.GetSubscriptionAsync(Guid.Parse(currentUser.Id), identifier);
        if (subscription is null) return NotFound(new ApiError { Code = "WALLET_SUBSCRIPTION_NOT_FOUND", Message = $"Subscription with identifier {identifier} was not found.", Status = 404 });

        return subscription;
    }

    private static SubscriptionCatalogItem MapCatalogItem(SnWalletSubscriptionDefinition def)
    {
        return new SubscriptionCatalogItem
        {
            Identifier = def.Identifier,
            GroupIdentifier = def.GroupIdentifier,
            DisplayName = def.DisplayName,
            Currency = def.Currency,
            BasePrice = def.BasePrice,
            PerkLevel = def.PerkLevel,
            MinimumAccountLevel = def.MinimumAccountLevel,
            ExperienceMultiplier = def.ExperienceMultiplier,
            GoldenPointReward = def.GoldenPointReward,
            DisplayConfig = def.DisplayConfig?.Clone(),
            AllowedPaymentMethods = def.PaymentPolicy.AllowedMethods.ToList(),
            ProviderMappings = def.ProviderMappings.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.ToList(),
                StringComparer.OrdinalIgnoreCase
            )
        };
    }

    private static SubscriptionGroupCatalogItem MapCatalogGroup(
        IGrouping<string, SnWalletSubscriptionDefinition> group
    )
    {
        return MapCatalogGroup(group.ToList());
    }

    private static SubscriptionGroupCatalogItem MapCatalogGroup(
        IReadOnlyList<SnWalletSubscriptionDefinition> definitions
    )
    {
        var ordered = definitions
            .OrderBy(x => x.PerkLevel)
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var primary = ordered.First();
        var groupIdentifier = string.IsNullOrWhiteSpace(primary.GroupIdentifier)
            ? primary.Identifier
            : primary.GroupIdentifier!;

        return new SubscriptionGroupCatalogItem
        {
            GroupIdentifier = groupIdentifier,
            DisplayName = primary.DisplayName,
            MaxPerkLevel = ordered.Max(x => x.PerkLevel),
            DisplayConfig = primary.DisplayConfig?.Clone(),
            Items = ordered.Select(MapCatalogItem).ToList()
        };
    }

    private static SubscriptionGroupStateItem MapGroupStateItem(
        SnWalletSubscription subscription,
        IReadOnlyDictionary<string, SnWalletSubscriptionDefinition> definitionMap
    )
    {
        definitionMap.TryGetValue(subscription.Identifier, out var definition);
        return new SubscriptionGroupStateItem
        {
            Subscription = subscription.ToReference(),
            Definition = definition is null ? null : MapCatalogItem(definition)
        };
    }

    private async Task HydrateSubscriptionMetadataAsync(
        IEnumerable<SnWalletSubscription> subscriptionsInGroup,
        IReadOnlyDictionary<string, SnWalletSubscriptionDefinition> definitionMap
    )
    {
        var changed = false;
        foreach (var subscription in subscriptionsInGroup)
        {
            if (!definitionMap.TryGetValue(subscription.Identifier, out var definition)) continue;

            if (string.IsNullOrWhiteSpace(subscription.GroupIdentifier) && !string.IsNullOrWhiteSpace(definition.GroupIdentifier))
            {
                subscription.GroupIdentifier = definition.GroupIdentifier;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(subscription.DisplayName))
            {
                subscription.DisplayName = definition.DisplayName;
                changed = true;
            }

            if (!subscription.IsTesting && subscription.PerkLevel == 0 && definition.PerkLevel > 0)
            {
                subscription.PerkLevel = definition.PerkLevel;
                changed = true;
            }
        }

        if (changed)
            await db.SaveChangesAsync();
    }

    public class CreateSubscriptionRequest
    {
        [Required] public string Identifier { get; set; } = null!;
        [Required] public string PaymentMethod { get; set; } = null!;
        [Required] public SnPaymentDetails PaymentDetails { get; set; } = null!;
        public string? Coupon { get; set; }
        public int? CycleDurationDays { get; set; }
        public bool IsFreeTrial { get; set; } = false;
        public bool IsAutoRenewal { get; set; } = true;
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult<SnWalletSubscription>> CreateSubscription(
        [FromBody] CreateSubscriptionRequest request,
        [FromHeader(Name = "X-Noop")] bool noop = false
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        Duration? cycleDuration = null;
        if (request.CycleDurationDays.HasValue)
            cycleDuration = Duration.FromDays(request.CycleDurationDays.Value);

        try
        {
            var subscription = await subscriptions.CreateSubscriptionAsync(
                currentUser,
                request.Identifier,
                request.PaymentMethod,
                request.PaymentDetails,
                cycleDuration,
                request.Coupon,
                request.IsFreeTrial,
                request.IsAutoRenewal,
                noop
            );

            als.CreateActionLog(
                Guid.Parse(currentUser.Id),
                "subscriptions.create",
                new Dictionary<string, object>
                {
                    { "subscription_identifier", request.Identifier },
                    { "payment_method", request.PaymentMethod },
                    { "is_free_trial", request.IsFreeTrial }
                },
                userAgent: Request.Headers.UserAgent,
                ipAddress: Request.GetClientIpAddress()
            );

            return subscription;
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(new ApiError { Code = "WALLET_SUBSCRIPTION_CREATE_FAILED", Message = ex.Message, Status = 400 });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiError { Code = "WALLET_SUBSCRIPTION_CREATE_FAILED", Message = ex.Message, Status = 400 });
        }
    }

    [HttpPost("{identifier}/cancel")]
    [Authorize]
    [AskPermission(PermissionKeys.SubscriptionsCancel)]
    public async Task<ActionResult<SnWalletSubscription>> CancelSubscription(string identifier)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        try
        {
            var subscription = await subscriptions.CancelSubscriptionAsync(Guid.Parse(currentUser.Id), identifier);

            als.CreateActionLog(
                Guid.Parse(currentUser.Id),
                "subscriptions.cancel",
                new Dictionary<string, object>
                {
                    { "subscription_identifier", identifier }
                },
                userAgent: Request.Headers.UserAgent,
                ipAddress: Request.GetClientIpAddress()
            );

            return subscription;
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiError { Code = "WALLET_SUBSCRIPTION_CANCEL_FAILED", Message = ex.Message, Status = 400 });
        }
    }

    [HttpPost("{identifier}/order")]
    [Authorize]
    [AskPermission(PermissionKeys.SubscriptionsOrderManage)]
    public async Task<ActionResult<SnWalletOrder>> CreateSubscriptionOrder(string identifier)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        try
        {
            var order = await subscriptions.CreateSubscriptionOrder(Guid.Parse(currentUser.Id), identifier);
            return order;
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiError { Code = "WALLET_SUBSCRIPTION_ORDER_CREATE_FAILED", Message = ex.Message, Status = 400 });
        }
    }

    public class RestorePurchaseRequest
    {
        [Required] public string OrderId { get; set; } = null!;
    }

    public class RestoreApplePurchaseRequest
    {
        [Required] public string SignedTransactionInfo { get; set; } = null!;
    }

    public class CreatePaddleCheckoutRequest
    {
        public string? ProviderReferenceId { get; set; }
    }

    public class PaddleCheckoutResponse
    {
        public string TransactionId { get; set; } = null!;
        public string CheckoutUrl { get; set; } = null!;
        public string ProviderReferenceId { get; set; } = null!;
    }

    public class AfdianCheckoutResponse
    {
        public string CheckoutUrl { get; set; } = null!;
        public string ProviderReferenceId { get; set; } = null!;
        public string PlanId { get; set; } = null!;
    }

    [HttpPost("order/restore/afdian")]
    [Authorize]
    public async Task<IActionResult> RestorePurchaseFromAfdian([FromBody] RestorePurchaseRequest request)
    {
        var order = await afdian.GetOrderAsync(request.OrderId);
        if (order is null) return NotFound(new ApiError { Code = "WALLET_ORDER_NOT_FOUND", Message = $"Order with ID {request.OrderId} was not found.", Status = 404 });

        return Ok(await ApplyRestoredProviderOrderAsync(order));
    }

    [HttpPost("order/restore/paddle")]
    [Authorize]
    public async Task<IActionResult> RestorePurchaseFromPaddle([FromBody] RestorePurchaseRequest request)
    {
        var order = await paddle.GetTransactionAsync(request.OrderId, HttpContext.RequestAborted);
        if (order is null) return NotFound(new ApiError { Code = "WALLET_TRANSACTION_NOT_FOUND", Message = $"Transaction with ID {request.OrderId} was not found.", Status = 404 });

        return Ok(await ApplyRestoredProviderOrderAsync(order));
    }

    [HttpPost("order/restore/apple")]
    [Authorize]
    public async Task<IActionResult> RestorePurchaseFromAppleStore([FromBody] RestoreApplePurchaseRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        AppleAppStoreTransaction transaction;
        try
        {
            transaction = appleStore.ParseSignedTransaction(request.SignedTransactionInfo);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException or CryptographicException)
        {
            return BadRequest(new ApiError { Code = "WALLET_APPLE_TRANSACTION_PARSE_FAILED", Message = ex.Message, Status = 400 });
        }

        if (!string.Equals(transaction.AccountId, currentUser.Id, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new ApiError { Code = "WALLET_APPLE_TRANSACTION_ACCOUNT_MISMATCH", Message = "Apple transaction account token does not match the current user.", Status = 400 });

        return Ok(await ApplyRestoredProviderOrderAsync(transaction));
    }

    [HttpPost("{identifier}/checkout/paddle")]
    [Authorize]
    [AskPermission(PermissionKeys.SubscriptionsCheckout)]
    public async Task<ActionResult<PaddleCheckoutResponse>> CreatePaddleCheckout(
        string identifier,
        [FromBody] CreatePaddleCheckoutRequest? request = null
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        try
        {
            var (definition, providerReference) = await subscriptions.PreparePaddleCheckoutAsync(
                currentUser,
                identifier,
                request?.ProviderReferenceId,
                HttpContext.RequestAborted
            );

            var session = await paddle.CreateCheckoutAsync(
                providerReference,
                new Dictionary<string, object>
                {
                    ["account_id"] = currentUser.Id,
                    ["subscription_identifier"] = definition.Identifier,
                    ["price_id"] = providerReference,
                    ["group_identifier"] = definition.GroupIdentifier ?? string.Empty
                },
                HttpContext.RequestAborted
            );

            return Ok(new PaddleCheckoutResponse
            {
                TransactionId = session.TransactionId,
                CheckoutUrl = session.CheckoutUrl,
                ProviderReferenceId = providerReference
            });
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(new ApiError { Code = "WALLET_SUBSCRIPTION_CHECKOUT_FAILED", Message = ex.Message, Status = 400 });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiError { Code = "WALLET_SUBSCRIPTION_CHECKOUT_FAILED", Message = ex.Message, Status = 400 });
        }
    }

    [HttpPost("{identifier}/checkout/afdian")]
    [Authorize]
    [AskPermission(PermissionKeys.SubscriptionsCheckout)]
    public async Task<ActionResult<AfdianCheckoutResponse>> CreateAfdianCheckout(
        string identifier,
        [FromBody] CreatePaddleCheckoutRequest? request = null
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        try
        {
            var (_, providerReference) = await subscriptions.PrepareAfdianCheckoutAsync(
                currentUser,
                identifier,
                request?.ProviderReferenceId,
                HttpContext.RequestAborted
            );

            var planId = afdian.GetCheckoutPlanId();
            var checkoutUrl = afdian.CreateCheckoutUrl(planId, providerReference);

            return Ok(new AfdianCheckoutResponse
            {
                CheckoutUrl = checkoutUrl,
                ProviderReferenceId = providerReference,
                PlanId = planId
            });
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(new ApiError { Code = "WALLET_SUBSCRIPTION_CHECKOUT_FAILED", Message = ex.Message, Status = 400 });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiError { Code = "WALLET_SUBSCRIPTION_CHECKOUT_FAILED", Message = ex.Message, Status = 400 });
        }
    }

    [HttpPost("order/handle/afdian")]
    public async Task<ActionResult<AfdianWebhookResponse>> AfdianWebhook()
    {
        var response = await afdian.HandleWebhook(Request, async webhookData =>
        {
            await ApplyProviderOrderAsync(webhookData.AfdianOrder);
        });

        return Ok(response);
    }

    [HttpPost("order/handle/paddle")]
    public async Task<IActionResult> PaddleWebhook()
    {
        var response = await paddle.HandleWebhook(Request, async transaction =>
        {
            await ApplyProviderOrderAsync(transaction);
        }, HttpContext.RequestAborted);

        return response.IsSuccess ? Ok() : Unauthorized();
    }

    [HttpPost("order/handle/apple")]
    public async Task<IActionResult> AppleStoreWebhook()
    {
        var response = await appleStore.HandleWebhook(Request, async transaction =>
        {
            await ApplyProviderOrderAsync(transaction);
        }, HttpContext.RequestAborted);

        return response.IsSuccess ? Ok() : Unauthorized();
    }

    private async Task<object> ApplyRestoredProviderOrderAsync(ISubscriptionOrder order)
    {
        if (walletProducts.IsGoldCurrencyPurchase(order))
            return await walletProducts.CreateOrApplyGoldsResupplyPackPurchaseAsync(order, HttpContext.RequestAborted);

        return await subscriptions.CreateSubscriptionFromOrder(order);
    }

    private async Task ApplyProviderOrderAsync(ISubscriptionOrder order)
    {
        _ = await ApplyRestoredProviderOrderAsync(order);
    }
}

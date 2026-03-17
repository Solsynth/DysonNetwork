using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using System.Security.Cryptography;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using DysonNetwork.Wallet.Payment.PaymentHandlers;
using DysonNetwork.Shared.Extensions;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;

namespace DysonNetwork.Wallet.Payment;

[ApiController]
[Route("/api/subscriptions")]
public class SubscriptionController(
    SubscriptionService subscriptions,
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
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

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
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        var definitions = await catalog.ListDefinitionsByGroupAsync(groupIdentifier, HttpContext.RequestAborted);
        if (definitions.Count == 0) return NotFound($"Subscription group {groupIdentifier} was not found.");

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

    [HttpGet("groups/{groupIdentifier}/active")]
    [Authorize]
    public async Task<ActionResult<SnWalletSubscription>> GetActiveSubscriptionInGroup(string groupIdentifier)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        var definitions = await catalog.ListDefinitionsByGroupAsync(groupIdentifier, HttpContext.RequestAborted);
        if (definitions.Count == 0) return NotFound($"Subscription group {groupIdentifier} was not found.");

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
        if (active is null) return NotFound();

        return Ok(active);
    }

    [HttpGet("{identifier}")]
    [Authorize]
    public async Task<ActionResult<SnWalletSubscription>> GetSubscription(string identifier)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        var subscription = await subscriptions.GetSubscriptionAsync(Guid.Parse(currentUser.Id), identifier);
        if (subscription is null) return NotFound($"Subscription with identifier {identifier} was not found.");

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
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

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
            return BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{identifier}/cancel")]
    [Authorize]
    public async Task<ActionResult<SnWalletSubscription>> CancelSubscription(string identifier)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

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
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{identifier}/order")]
    [Authorize]
    public async Task<ActionResult<SnWalletOrder>> CreateSubscriptionOrder(string identifier)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        try
        {
            var order = await subscriptions.CreateSubscriptionOrder(Guid.Parse(currentUser.Id), identifier);
            return order;
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
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
        if (order is null) return NotFound($"Order with ID {request.OrderId} was not found.");

        var subscription = await subscriptions.CreateSubscriptionFromOrder(order);
        return Ok(subscription);
    }

    [HttpPost("order/restore/paddle")]
    [Authorize]
    public async Task<IActionResult> RestorePurchaseFromPaddle([FromBody] RestorePurchaseRequest request)
    {
        var order = await paddle.GetTransactionAsync(request.OrderId, HttpContext.RequestAborted);
        if (order is null) return NotFound($"Transaction with ID {request.OrderId} was not found.");

        var subscription = await subscriptions.CreateSubscriptionFromOrder(order);
        return Ok(subscription);
    }

    [HttpPost("order/restore/apple")]
    [Authorize]
    public async Task<IActionResult> RestorePurchaseFromAppleStore([FromBody] RestoreApplePurchaseRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        AppleAppStoreTransaction transaction;
        try
        {
            transaction = appleStore.ParseSignedTransaction(request.SignedTransactionInfo);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException or CryptographicException)
        {
            return BadRequest(ex.Message);
        }

        if (!string.Equals(transaction.AccountId, currentUser.Id, StringComparison.OrdinalIgnoreCase))
            return BadRequest("Apple transaction account token does not match the current user.");

        var subscription = await subscriptions.CreateSubscriptionFromOrder(transaction);
        return Ok(subscription);
    }

    [HttpPost("{identifier}/checkout/paddle")]
    [Authorize]
    public async Task<ActionResult<PaddleCheckoutResponse>> CreatePaddleCheckout(
        string identifier,
        [FromBody] CreatePaddleCheckoutRequest? request = null
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

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
            return BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{identifier}/checkout/afdian")]
    [Authorize]
    public async Task<ActionResult<AfdianCheckoutResponse>> CreateAfdianCheckout(
        string identifier,
        [FromBody] CreatePaddleCheckoutRequest? request = null
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

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
            return BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("order/handle/afdian")]
    public async Task<ActionResult<AfdianWebhookResponse>> AfdianWebhook()
    {
        var response = await afdian.HandleWebhook(Request, async webhookData =>
        {
            var order = webhookData.AfdianOrder;
            await subscriptions.CreateSubscriptionFromOrder(order);
        });

        return Ok(response);
    }

    [HttpPost("order/handle/paddle")]
    public async Task<IActionResult> PaddleWebhook()
    {
        var response = await paddle.HandleWebhook(Request, async transaction =>
        {
            await subscriptions.CreateSubscriptionFromOrder(transaction);
        }, HttpContext.RequestAborted);

        return response.IsSuccess ? Ok() : Unauthorized();
    }

    [HttpPost("order/handle/apple")]
    public async Task<IActionResult> AppleStoreWebhook()
    {
        var response = await appleStore.HandleWebhook(Request, async transaction =>
        {
            await subscriptions.CreateSubscriptionFromOrder(transaction);
        }, HttpContext.RequestAborted);

        return response.IsSuccess ? Ok() : Unauthorized();
    }
}

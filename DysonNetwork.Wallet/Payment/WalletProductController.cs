using System.Security.Cryptography;
using System.Text.Json;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Wallet.Payment.PaymentHandlers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Wallet.Payment;

[ApiController]
[Route("/api/wallet-products")]
public class WalletProductController(
    WalletProductService walletProducts,
    AfdianPaymentHandler afdian,
    AppleStorePaymentHandler appleStore,
    PaddlePaymentHandler paddle
) : ControllerBase
{
    public class ProviderCheckoutRequest
    {
        public string? ProviderReferenceId { get; set; }
    }

    public class RestorePurchaseRequest
    {
        public string OrderId { get; set; } = null!;
    }

    public class RestoreApplePurchaseRequest
    {
        public string SignedTransactionInfo { get; set; } = null!;
    }

    public class PaddleCheckoutResponse
    {
        public string TransactionId { get; set; } = null!;
        public string CheckoutUrl { get; set; } = null!;
        public string ProviderReferenceId { get; set; } = null!;
        public decimal GoldAmount { get; set; }
    }

    public class AfdianCheckoutResponse
    {
        public string CheckoutUrl { get; set; } = null!;
        public string ProviderReferenceId { get; set; } = null!;
        public string PlanId { get; set; } = null!;
        public decimal GoldAmount { get; set; }
    }

    [HttpPost("golds-resupply-pack/checkout/paddle")]
    [Authorize]
    [AskPermission(PermissionKeys.OrdersCreate)]
    public async Task<ActionResult<PaddleCheckoutResponse>> CreatePaddleCheckout(
        [FromBody] ProviderCheckoutRequest? request = null
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        try
        {
            var (_, providerReference, amount) = walletProducts.PreparePaddleGoldsResupplyPack(request?.ProviderReferenceId);
            var session = await paddle.CreateCheckoutAsync(
                providerReference,
                new Dictionary<string, object>
                {
                    ["account_id"] = currentUser.Id,
                    ["wallet_product"] = WalletProductService.GoldsResupplyPackKey,
                    ["price_id"] = providerReference,
                    ["golds_amount"] = amount
                },
                HttpContext.RequestAborted
            );

            return Ok(new PaddleCheckoutResponse
            {
                TransactionId = session.TransactionId,
                CheckoutUrl = session.CheckoutUrl,
                ProviderReferenceId = providerReference,
                GoldAmount = amount
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("golds-resupply-pack/checkout/afdian")]
    [Authorize]
    [AskPermission(PermissionKeys.OrdersCreate)]
    public ActionResult<AfdianCheckoutResponse> CreateAfdianCheckout(
        [FromBody] ProviderCheckoutRequest? request = null
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        try
        {
            var (_, providerReference, amount) = walletProducts.PrepareAfdianGoldsResupplyPack(request?.ProviderReferenceId);
            var planId = afdian.GetCheckoutPlanId();
            var checkoutUrl = afdian.CreateCheckoutUrl(planId, providerReference, customOrderId: currentUser.Id);

            return Ok(new AfdianCheckoutResponse
            {
                CheckoutUrl = checkoutUrl,
                ProviderReferenceId = providerReference,
                PlanId = planId,
                GoldAmount = amount
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("order/restore/afdian")]
    [Authorize]
    public async Task<IActionResult> RestorePurchaseFromAfdian([FromBody] RestorePurchaseRequest request)
    {
        var order = await afdian.GetOrderAsync(request.OrderId);
        if (order is null) return NotFound($"Order with ID {request.OrderId} was not found.");

        var appliedOrder = await walletProducts.CreateOrApplyGoldsResupplyPackPurchaseAsync(order, HttpContext.RequestAborted);
        return Ok(appliedOrder);
    }

    [HttpPost("order/restore/paddle")]
    [Authorize]
    public async Task<IActionResult> RestorePurchaseFromPaddle([FromBody] RestorePurchaseRequest request)
    {
        var order = await paddle.GetTransactionAsync(request.OrderId, HttpContext.RequestAborted);
        if (order is null) return NotFound($"Transaction with ID {request.OrderId} was not found.");

        var appliedOrder = await walletProducts.CreateOrApplyGoldsResupplyPackPurchaseAsync(order, HttpContext.RequestAborted);
        return Ok(appliedOrder);
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

        var appliedOrder = await walletProducts.CreateOrApplyGoldsResupplyPackPurchaseAsync(transaction, HttpContext.RequestAborted);
        return Ok(appliedOrder);
    }

}

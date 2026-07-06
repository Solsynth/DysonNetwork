using DysonNetwork.Shared.Auth;
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
    PaddlePaymentHandler paddle
) : ControllerBase
{
    public class ProviderCheckoutRequest
    {
        public string? ProviderReferenceId { get; set; }
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

}

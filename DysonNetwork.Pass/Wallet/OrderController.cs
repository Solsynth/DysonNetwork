using DysonNetwork.Pass.Auth;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Pass.Wallet;

[ApiController]
[Route("/api/orders")]
public class OrderController(
    PaymentService payment,
    Pass.Auth.AuthService auth,
    AppDatabase db,
    CustomAppService.CustomAppServiceClient customApps
) : ControllerBase
{
    public class CreateOrderRequest
    {
        public string Currency { get; set; } = null!;
        public decimal Amount { get; set; }
        public string? Remarks { get; set; }
        public string? ProductIdentifier { get; set; }
        public Dictionary<string, object>? Meta { get; set; }
        public int DurationHours { get; set; } = 24;

        public string ClientId { get; set; } = null!;
        public string ClientSecret { get; set; } = null!;
    }

    [HttpPost]
    public async Task<ActionResult<SnWalletOrder>> CreateOrder([FromBody] CreateOrderRequest request)
    {
        var clientResp = await customApps.GetCustomAppAsync(new GetCustomAppRequest { Slug = request.ClientId });
        if (clientResp.App is null) return BadRequest("Client not found");
        var client = SnCustomApp.FromProtoValue(clientResp.App);

        var secret = await customApps.CheckCustomAppSecretAsync(new CheckCustomAppSecretRequest
        {
            AppId = client.Id.ToString(),
            Secret = request.ClientSecret,
        });
        if (!secret.Valid) return BadRequest("Invalid client secret");

        var order = await payment.CreateOrderAsync(
            default,
            request.Currency,
            request.Amount,
            NodaTime.Duration.FromHours(request.DurationHours),
            request.ClientId,
            request.ProductIdentifier,
            request.Remarks,
            request.Meta
        );

        return Ok(order);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SnWalletOrder>> GetOrderById(Guid id)
    {
        var order = await db.PaymentOrders.FindAsync(id);

        if (order == null)
            return NotFound();

        return Ok(order);
    }

    public class PayOrderRequest
    {
        public string PinCode { get; set; } = string.Empty;
    }

    [HttpPost("{id:guid}/pay")]
    [Authorize]
    public async Task<ActionResult<SnWalletOrder>> PayOrder(Guid id, [FromBody] PayOrderRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        // Validate PIN code
        if (!await auth.ValidatePinCode(currentUser.Id, request.PinCode))
            return StatusCode(403, "Invalid PIN Code");

        try
        {
            // Get the wallet for the current user
            var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.AccountId == currentUser.Id);
            if (wallet == null)
                return BadRequest("Wallet was not found.");

            // Pay the order
            var paidOrder = await payment.PayOrderAsync(id, wallet);
            return Ok(paidOrder);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}


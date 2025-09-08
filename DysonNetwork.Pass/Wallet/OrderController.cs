using DysonNetwork.Pass.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Pass.Wallet;

[ApiController]
[Route("/api/orders")]
public class OrderController(PaymentService payment, AuthService auth, AppDatabase db) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Order>> GetOrderById(Guid id)
    {
        var order = await db.PaymentOrders.FindAsync(id);
        
        if (order == null)
            return NotFound();
        
        return Ok(order);
    }
    
    [HttpPost("{id:guid}/pay")]
    [Authorize]
    public async Task<ActionResult<Order>> PayOrder(Guid id, [FromBody] PayOrderRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();
    
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

public class PayOrderRequest
{
    public string PinCode { get; set; } = string.Empty;
}
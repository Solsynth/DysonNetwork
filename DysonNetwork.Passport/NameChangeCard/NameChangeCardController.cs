using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Capabilities;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Passport.NameChangeCard;

[ApiController]
[Route("/api/accounts/me/name-change-card")]
[Authorize]
[ApiFeature("accounts.name-change-card", Revision = 1)]
public class NameChangeCardController(NameChangeCardService service) : ControllerBase
{
    public class PurchaseNameChangeCardResponse
    {
        public Guid PurchaseId { get; set; }
        public Guid OrderId { get; set; }
        public decimal Amount { get; set; }
    }

    public class UseNameChangeCardRequest
    {
        [Required] public string Target { get; set; } = null!;
        public Guid? TargetId { get; set; }
        [Required] public string NewName { get; set; } = null!;
    }

    [HttpPost("order")]
    public async Task<ActionResult<PurchaseNameChangeCardResponse>> PurchaseOrder()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });
        try
        {
            var purchase = await service.Purchase(currentUser.Id, HttpContext.RequestAborted);
            return Ok(new PurchaseNameChangeCardResponse { PurchaseId = purchase.Id, OrderId = purchase.OrderId, Amount = purchase.Amount });
        }
        catch (InvalidOperationException e)
        {
            return BadRequest(new ApiError { Code = "NAME_CHANGE_CARD_PURCHASE_DISALLOWED", Message = e.Message, Status = 400, TraceId = HttpContext.TraceIdentifier });
        }
    }

    [HttpGet]
    public async Task<ActionResult<List<SnNameChangeCardPurchase>>> ListPurchases()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });
        return Ok(await service.List(currentUser.Id, HttpContext.RequestAborted));
    }

    [HttpPost("use")]
    public async Task<ActionResult<SnNameChangeCardPurchase>> UseCard([FromBody] UseNameChangeCardRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });
        try
        {
            return Ok(await service.Use(currentUser.Id, request.Target, request.TargetId, request.NewName, HttpContext.RequestAborted));
        }
        catch (InvalidOperationException e)
        {
            return BadRequest(new ApiError { Code = "NAME_CHANGE_CARD_USE_FAILED", Message = e.Message, Status = 400, TraceId = HttpContext.TraceIdentifier });
        }
        catch (RpcException e)
        {
            return BadRequest(new ApiError { Code = "NAME_CHANGE_CARD_USE_FAILED", Message = e.Status.Detail, Status = 400, TraceId = HttpContext.TraceIdentifier });
        }
    }
}

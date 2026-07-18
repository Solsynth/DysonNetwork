using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Capabilities;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Wallet.Payment;

[ApiController]
[Route("/api/admin/wallet-products")]
[Authorize]
[ApiFeature("admin.wallet-products", Revision = 1)]
public class WalletProductAdminController(
    WalletProductService walletProducts
) : ControllerBase
{
    public class WalletProductAdminSummary
    {
        public string Key { get; set; } = null!;
        public string Identifier { get; set; } = null!;
        public string DisplayName { get; set; } = null!;
        public string Currency { get; set; } = null!;
        public Dictionary<string, Dictionary<string, decimal>> ProviderMappings { get; set; } = [];
    }

    [HttpGet("golds-resupply-pack")]
    [AskPermission(PermissionKeys.OrdersView)]
    public ActionResult<WalletProductAdminSummary> GetGoldsResupplyPack()
    {
        var definition = walletProducts.GetGoldsResupplyPackDefinition();
        return Ok(new WalletProductAdminSummary
        {
            Key = WalletProductService.GoldsResupplyPackKey,
            Identifier = definition.Identifier,
            DisplayName = definition.DisplayName,
            Currency = definition.Currency,
            ProviderMappings = definition.ProviderMappings
                .ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value.ToDictionary(inner => inner.Key, inner => inner.Value, StringComparer.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase
                )
        });
    }

    [HttpPost("orders/{orderId:guid}/apply")]
    [AskPermission(PermissionKeys.OrdersPay)]
    public async Task<ActionResult<SnWalletOrder>> ApplyPaidWalletProductOrder(Guid orderId)
    {
        try
        {
            var order = await walletProducts.ApplyPaidWalletProductOrderAsync(orderId, HttpContext.RequestAborted);
            return Ok(order);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiError { Code = "WALLET_ORDER_APPLY_FAILED", Message = ex.Message, Status = 400 });
        }
    }
}

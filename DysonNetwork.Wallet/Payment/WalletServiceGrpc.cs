using DysonNetwork.Shared.Proto;
using Grpc.Core;

namespace DysonNetwork.Wallet.Payment;

public class WalletServiceGrpc(WalletService walletService) : DyWalletService.DyWalletServiceBase
{
    public override async Task<DyWallet> GetWallet(DyGetWalletRequest request, ServerCallContext context)
    {
        var wallet = await walletService.GetAccountWalletAsync(Guid.Parse(request.AccountId));
        return wallet == null ? throw new RpcException(new Status(StatusCode.NotFound, "Wallet not found.")) : wallet.ToProtoValue();
    }

    public override async Task<DyWallet> CreateWallet(DyCreateWalletRequest request, ServerCallContext context)
    {
        var wallet = await walletService.CreateWalletAsync(Guid.Parse(request.AccountId));
        return wallet.ToProtoValue();
    }

    public override async Task<DyWalletPocket> GetOrCreateWalletPocket(DyGetOrCreateWalletPocketRequest request, ServerCallContext context)
    {
        var (pocket, _) = await walletService.GetOrCreateWalletPocketAsync(Guid.Parse(request.WalletId), request.Currency, request.HasInitialAmount ? decimal.Parse(request.InitialAmount) : null);
        return pocket.ToProtoValue();
    }
}

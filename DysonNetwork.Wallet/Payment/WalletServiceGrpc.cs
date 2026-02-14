using DysonNetwork.Shared.Proto;
using Grpc.Core;

namespace DysonNetwork.Wallet.Payment;

public class WalletServiceGrpc(WalletService walletService) : Shared.Proto.WalletService.WalletServiceBase
{
    public override async Task<Shared.Proto.Wallet> GetWallet(GetWalletRequest request, ServerCallContext context)
    {
        var wallet = await walletService.GetAccountWalletAsync(Guid.Parse(request.AccountId));
        return wallet == null ? throw new RpcException(new Status(StatusCode.NotFound, "Wallet not found.")) : wallet.ToProtoValue();
    }

    public override async Task<Shared.Proto.Wallet> CreateWallet(CreateWalletRequest request, ServerCallContext context)
    {
        var wallet = await walletService.CreateWalletAsync(Guid.Parse(request.AccountId));
        return wallet.ToProtoValue();
    }

    public override async Task<Shared.Proto.WalletPocket> GetOrCreateWalletPocket(GetOrCreateWalletPocketRequest request, ServerCallContext context)
    {
        var (pocket, _) = await walletService.GetOrCreateWalletPocketAsync(Guid.Parse(request.WalletId), request.Currency, request.HasInitialAmount ? decimal.Parse(request.InitialAmount) : null);
        return pocket.ToProtoValue();
    }
}

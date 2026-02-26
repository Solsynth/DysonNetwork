using DysonNetwork.Shared.Proto;

namespace DysonNetwork.Shared.Registry;

public class RemoteWalletService(DyWalletService.DyWalletServiceClient wallet)
{
    public async Task<DyWallet> GetWallet(Guid accountId)
    {
        var request = new DyGetWalletRequest { AccountId = accountId.ToString() };
        var response = await wallet.GetWalletAsync(request);
        return response;
    }

    public async Task<DyWallet> CreateWallet(Guid accountId)
    {
        var request = new DyCreateWalletRequest { AccountId = accountId.ToString() };
        var response = await wallet.CreateWalletAsync(request);
        return response;
    }

    public async Task<DyWalletPocket> GetOrCreateWalletPocket(Guid walletId, string currency, decimal? initialAmount = null)
    {
        var request = new DyGetOrCreateWalletPocketRequest
        {
            WalletId = walletId.ToString(),
            Currency = currency
        };

        if (initialAmount.HasValue)
        {
            request.InitialAmount = initialAmount.Value.ToString();
        }

        var response = await wallet.GetOrCreateWalletPocketAsync(request);
        return response;
    }
}

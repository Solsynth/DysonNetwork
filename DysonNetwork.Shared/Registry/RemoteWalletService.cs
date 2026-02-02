using DysonNetwork.Shared.Proto;

namespace DysonNetwork.Shared.Registry;

public class RemoteWalletService(WalletService.WalletServiceClient wallet)
{
    public async Task<Wallet> GetWallet(Guid accountId)
    {
        var request = new GetWalletRequest { AccountId = accountId.ToString() };
        var response = await wallet.GetWalletAsync(request);
        return response;
    }

    public async Task<Wallet> CreateWallet(Guid accountId)
    {
        var request = new CreateWalletRequest { AccountId = accountId.ToString() };
        var response = await wallet.CreateWalletAsync(request);
        return response;
    }

    public async Task<WalletPocket> GetOrCreateWalletPocket(Guid walletId, string currency, decimal? initialAmount = null)
    {
        var request = new GetOrCreateWalletPocketRequest
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

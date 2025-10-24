using DysonNetwork.Shared.Models;
using DysonNetwork.Pass.Wallet;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Pass.Lotteries;

public class LotteryService(AppDatabase db, PaymentService paymentService, WalletService walletService)
{
    private static bool ValidateNumbers(List<int> region1, int region2)
    {
        if (region1.Count != 5 || region1.Distinct().Count() != 5)
            return false;
        if (region1.Any(n => n < 0 || n > 99))
            return false;
        if (region2 < 0 || region2 > 99)
            return false;
        return true;
    }

    public async Task<SnLottery> CreateTicketAsync(Guid accountId, List<int> region1, int region2, int multiplier = 1)
    {
        if (!ValidateNumbers(region1, region2))
            throw new ArgumentException("Invalid lottery numbers");

        var lottery = new SnLottery
        {
            AccountId = accountId,
            RegionOneNumbers = region1,
            RegionTwoNumber = region2,
            Multiplier = multiplier
        };

        db.Lotteries.Add(lottery);
        await db.SaveChangesAsync();

        return lottery;
    }

    public async Task<List<SnLottery>> GetUserTicketsAsync(Guid accountId, int offset = 0, int limit = 20)
    {
        return await db.Lotteries
            .Where(l => l.AccountId == accountId)
            .OrderByDescending(l => l.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<SnLottery?> GetTicketAsync(Guid id)
    {
        return await db.Lotteries.FirstOrDefaultAsync(l => l.Id == id);
    }

    public async Task<int> GetUserTicketCountAsync(Guid accountId)
    {
        return await db.Lotteries.CountAsync(l => l.AccountId == accountId);
    }

    private static decimal CalculateLotteryPrice(int multiplier)
    {
        return 10 + (multiplier - 1) * 10;
    }

    public async Task<SnWalletOrder> CreateLotteryOrderAsync(Guid accountId, List<int> region1, int region2, int multiplier = 1)
    {
        if (!ValidateNumbers(region1, region2))
            throw new ArgumentException("Invalid lottery numbers");

        var now = SystemClock.Instance.GetCurrentInstant();
        var todayStart = new LocalDateTime(now.InUtc().Year, now.InUtc().Month, now.InUtc().Day, 0, 0).InUtc().ToInstant();
        var hasPurchasedToday = await db.Lotteries.AnyAsync(l => l.AccountId == accountId && l.CreatedAt >= todayStart);
        if (hasPurchasedToday)
            throw new InvalidOperationException("You can only purchase one lottery per day.");

        var price = CalculateLotteryPrice(multiplier);

        return await paymentService.CreateOrderAsync(
            null,
            WalletCurrency.SourcePoint,
            price,
            appIdentifier: "lottery",
            productIdentifier: "lottery",
            meta: new Dictionary<string, object>
            {
                ["account_id"] = accountId.ToString(),
                ["region_one_numbers"] = region1,
                ["region_two_number"] = region2,
                ["multiplier"] = multiplier
            });
    }

    public async Task HandleLotteryOrder(SnWalletOrder order)
    {
        if (order.Status != OrderStatus.Paid ||
            !order.Meta.TryGetValue("account_id", out var accountIdValue) ||
            !order.Meta.TryGetValue("region_one_numbers", out var region1Value) ||
            !order.Meta.TryGetValue("region_two_number", out var region2Value) ||
            !order.Meta.TryGetValue("multiplier", out var multiplierValue))
            throw new InvalidOperationException("Invalid order.");

        var accountId = Guid.Parse((string)accountIdValue!);
        var region1Json = (System.Text.Json.JsonElement)region1Value;
        var region1 = region1Json.EnumerateArray().Select(e => e.GetInt32()).ToList();
        var region2 = Convert.ToInt32((string)region2Value!);
        var multiplier = Convert.ToInt32((string)multiplierValue!);

        await CreateTicketAsync(accountId, region1, region2, multiplier);
    }

    private static int CalculateReward(int region1Matches, bool region2Match)
    {
        var reward = region1Matches switch
        {
            0 => 0,
            1 => 10,
            2 => 20,
            3 => 50,
            4 => 100,
            5 => 1000,
            _ => 0
        };
        if (region2Match) reward *= 10;
        return reward;
    }

    private static List<int> GenerateUniqueRandomNumbers(int count, int min, int max)
    {
        var numbers = new List<int>();
        var random = new Random();
        while (numbers.Count < count)
        {
            var num = random.Next(min, max + 1);
            if (!numbers.Contains(num)) numbers.Add(num);
        }
        return numbers.OrderBy(n => n).ToList();
    }

    private int CountMatches(List<int> playerNumbers, List<int> winningNumbers)
    {
        return playerNumbers.Intersect(winningNumbers).Count();
    }

    public async Task PerformDailyDrawAsync()
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var yesterdayStart = new LocalDateTime(now.InUtc().Year, now.InUtc().Month, now.InUtc().Day, 0, 0).InUtc().ToInstant().Minus(Duration.FromDays(1));
        var todayStart = new LocalDateTime(now.InUtc().Year, now.InUtc().Month, now.InUtc().Day, 0, 0).InUtc().ToInstant();

        // Tickets purchased yesterday that are still pending draw
        var tickets = await db.Lotteries
            .Where(l => l.CreatedAt >= yesterdayStart && l.CreatedAt < todayStart && l.DrawStatus == LotteryDrawStatus.Pending)
            .ToListAsync();

        if (!tickets.Any()) return;

        // Generate winning numbers
        var winningRegion1 = GenerateUniqueRandomNumbers(5, 0, 99);
        var winningRegion2 = GenerateUniqueRandomNumbers(1, 0, 99)[0];

        var drawDate = Instant.FromDateTimeUtc(DateTime.Today.AddDays(-1)); // Yesterday's date

        var totalPrizesAwarded = 0;
        long totalPrizeAmount = 0;

        // Process each ticket
        foreach (var ticket in tickets)
        {
            var region1Matches = CountMatches(ticket.RegionOneNumbers, winningRegion1);
            var region2Match = ticket.RegionTwoNumber == winningRegion2;
            var reward = CalculateReward(region1Matches, region2Match);

            if (reward > 0)
            {
                var wallet = await walletService.GetWalletAsync(ticket.AccountId);
                if (wallet != null)
                {
                    await paymentService.CreateTransactionAsync(
                        payerWalletId: null,
                        payeeWalletId: wallet.Id,
                        currency: "isp",
                        amount: reward,
                        remarks: $"Lottery prize: {region1Matches} matches{(region2Match ? " + special" : "")}"
                    );
                    totalPrizesAwarded++;
                    totalPrizeAmount += reward;
                }
            }

            ticket.DrawStatus = LotteryDrawStatus.Drawn;
            ticket.DrawDate = drawDate;
        }

        // Save the draw record
        var lotteryRecord = new SnLotteryRecord
        {
            DrawDate = drawDate,
            WinningRegionOneNumbers = winningRegion1,
            WinningRegionTwoNumber = winningRegion2,
            TotalTickets = tickets.Count,
            TotalPrizesAwarded = totalPrizesAwarded,
            TotalPrizeAmount = totalPrizeAmount
        };

        db.LotteryRecords.Add(lotteryRecord);
        await db.SaveChangesAsync();
    }
}

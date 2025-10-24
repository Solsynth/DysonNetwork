using DysonNetwork.Shared.Models;
using DysonNetwork.Pass.Wallet;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;
using System.Text.Json;

namespace DysonNetwork.Pass.Lotteries;

public class LotteryOrderMetaData
{
    public Guid AccountId { get; set; }
    public List<int> RegionOneNumbers { get; set; } = new();
    public int RegionTwoNumber { get; set; }
    public int Multiplier { get; set; } = 1;
}

public class LotteryService(
    AppDatabase db,
    PaymentService paymentService,
    WalletService walletService,
    ILogger<LotteryService> logger)
{
    private readonly ILogger<LotteryService> _logger = logger;

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

    public async Task<SnWalletOrder> CreateLotteryOrderAsync(Guid accountId, List<int> region1, int region2,
        int multiplier = 1)
    {
        if (!ValidateNumbers(region1, region2))
            throw new ArgumentException("Invalid lottery numbers");

        var now = SystemClock.Instance.GetCurrentInstant();
        var todayStart = new LocalDateTime(now.InUtc().Year, now.InUtc().Month, now.InUtc().Day, 0, 0).InUtc()
            .ToInstant();
        var hasPurchasedToday = await db.Lotteries.AnyAsync(l =>
            l.AccountId == accountId &&
            l.CreatedAt >= todayStart &&
            l.DrawStatus == LotteryDrawStatus.Pending
        );
        if (hasPurchasedToday)
            throw new InvalidOperationException("You can only purchase one lottery per day.");

        var price = CalculateLotteryPrice(multiplier);

        var lotteryData = new LotteryOrderMetaData
        {
            AccountId = accountId,
            RegionOneNumbers = region1,
            RegionTwoNumber = region2,
            Multiplier = multiplier
        };

        return await paymentService.CreateOrderAsync(
            null,
            WalletCurrency.SourcePoint,
            price,
            appIdentifier: "lottery",
            productIdentifier: "lottery",
            meta: new Dictionary<string, object>
            {
                ["data"] = JsonSerializer.Serialize(lotteryData)
            });
    }

    public async Task HandleLotteryOrder(SnWalletOrder order)
    {
        if (order.Status == OrderStatus.Finished)
            return; // Already processed

        if (order.Status != OrderStatus.Paid ||
            !order.Meta.TryGetValue("data", out var dataValue) ||
            dataValue is null ||
            dataValue is not JsonElement { ValueKind: JsonValueKind.String } jsonElem)
            throw new InvalidOperationException("Invalid order.");

        var jsonString = jsonElem.GetString();
        if (jsonString is null)
            throw new InvalidOperationException("Invalid order.");

        var data = JsonSerializer.Deserialize<LotteryOrderMetaData>(jsonString);
        if (data is null)
            throw new InvalidOperationException("Invalid order data.");

        await CreateTicketAsync(data.AccountId, data.RegionOneNumbers, data.RegionTwoNumber, data.Multiplier);

        order.Status = OrderStatus.Finished;
        await db.SaveChangesAsync();
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

    public async Task DrawLotteries()
    {
        try
        {
            _logger.LogInformation("Starting drawing lotteries...");

            var now = SystemClock.Instance.GetCurrentInstant();

            // All pending lottery tickets
            var tickets = await db.Lotteries
                .Where(l => l.DrawStatus == LotteryDrawStatus.Pending)
                .ToListAsync();

            if (tickets.Count == 0)
            {
                _logger.LogInformation("No pending lottery tickets");
                return;
            }

            _logger.LogInformation("Found {Count} pending lottery tickets for draw", tickets.Count);

            // Generate winning numbers
            var winningRegion1 = GenerateUniqueRandomNumbers(5, 0, 99);
            var winningRegion2 = GenerateUniqueRandomNumbers(1, 0, 99)[0];

            _logger.LogInformation("Winning numbers generated: Region1 [{Region1}], Region2 [{Region2}]",
                string.Join(",", winningRegion1), winningRegion2);

            var drawDate = Instant.FromDateTimeUtc(new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month,
                DateTime.UtcNow.Day, 0, 0, 0, DateTimeKind.Utc).AddDays(-1)); // Yesterday's date

            var totalPrizesAwarded = 0;
            long totalPrizeAmount = 0;

            // Process each ticket
            foreach (var ticket in tickets)
            {
                var region1Matches = CountMatches(ticket.RegionOneNumbers, winningRegion1);
                var region2Match = ticket.RegionTwoNumber == winningRegion2;
                var reward = CalculateReward(region1Matches, region2Match);

                // Record match results
                ticket.MatchedRegionOneNumbers = ticket.RegionOneNumbers.Intersect(winningRegion1).ToList();
                ticket.MatchedRegionTwoNumber = region2Match ? (int?)winningRegion2 : null;

                if (reward > 0)
                {
                    var wallet = await walletService.GetWalletAsync(ticket.AccountId);
                    if (wallet != null)
                    {
                        await paymentService.CreateTransactionAsync(
                            payerWalletId: null,
                            payeeWalletId: wallet.Id,
                            currency: WalletCurrency.SourcePoint,
                            amount: reward,
                            remarks: $"Lottery prize: {region1Matches} matches{(region2Match ? " + special" : "")}"
                        );
                        _logger.LogInformation(
                            "Awarded {Amount} to account {AccountId} for {Matches} matches{(Special ? \" + special\" : \"\")}",
                            reward, ticket.AccountId, region1Matches, region2Match ? " + special" : "");
                        totalPrizesAwarded++;
                        totalPrizeAmount += reward;
                    }
                    else
                    {
                        _logger.LogWarning("Wallet not found for account {AccountId}, skipping prize award",
                            ticket.AccountId);
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

            _logger.LogInformation("Daily lottery draw completed: {Prizes} prizes awarded, total amount {Amount}",
                totalPrizesAwarded, totalPrizeAmount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred during the daily lottery draw");
            throw;
        }
    }
}
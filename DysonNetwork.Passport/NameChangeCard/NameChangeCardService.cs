using System.Globalization;
using System.Text.RegularExpressions;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NodaTime;

namespace DysonNetwork.Passport.NameChangeCard;

public partial class NameChangeCardService(
    AppDatabase db,
    RemotePaymentService payments,
    DyAccountService.DyAccountServiceClient accountGrpc,
    DyPublisherService.DyPublisherServiceClient publisherGrpc,
    IOptions<NameChangeCardOptions> options,
    IClock clock
)
{
    private const string ProductIdentifier = "passport.name-change-card";
    public const string TargetAccount = "account";
    public const string TargetRealm = "realm";
    public const string TargetPublisher = "publisher";

    private readonly NameChangeCardOptions _options = options.Value;
    private static readonly Regex AccountNameRegex = new("^[A-Za-z0-9_-]+$", RegexOptions.Compiled);

    public async Task<SnNameChangeCardPurchase> Purchase(Guid accountId, CancellationToken ct = default)
    {
        if (!_options.Enabled) throw new InvalidOperationException("Name change card purchases are disabled.");
        var since = clock.GetCurrentInstant() - Duration.FromDays(_options.PurchasePeriodDays);
        var count = await db.NameChangeCardPurchases.CountAsync(
            x => x.AccountId == accountId && x.CreatedAt >= since, ct);
        if (count >= _options.MaxPurchases) throw new InvalidOperationException("The purchase limit has been reached for this period.");

        var purchase = new SnNameChangeCardPurchase { AccountId = accountId, Amount = _options.PricePoints };
        var order = await payments.CreateOrder(
            currency: "points",
            amount: _options.PricePoints.ToString(CultureInfo.InvariantCulture),
            productIdentifier: ProductIdentifier,
            remarks: "Purchase name change card",
            meta: DysonNetwork.Shared.Data.InfraObjectCoder.ConvertObjectToByteString(new Dictionary<string, object?>
            {
                ["account_id"] = accountId,
                ["purchase_id"] = purchase.Id
            }).ToByteArray());
        purchase.OrderId = Guid.Parse(order.Id);
        db.NameChangeCardPurchases.Add(purchase);
        await db.SaveChangesAsync(ct);
        return purchase;
    }

    public async Task<SnNameChangeCardPurchase?> Fulfill(Guid purchaseId, Guid orderId, CancellationToken ct = default)
    {
        var purchase = await db.NameChangeCardPurchases.FirstOrDefaultAsync(
            x => x.Id == purchaseId && x.OrderId == orderId, ct);
        if (purchase is null || purchase.FulfilledAt is not null) return null;
        purchase.FulfilledAt = clock.GetCurrentInstant();
        await db.SaveChangesAsync(ct);
        return purchase;
    }

    public async Task<List<SnNameChangeCardPurchase>> List(Guid accountId, CancellationToken ct = default)
        => await db.NameChangeCardPurchases
            .Where(x => x.AccountId == accountId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(ct);

    public async Task<SnNameChangeCardPurchase> Use(Guid accountId, string target, Guid? targetId, string newName, CancellationToken ct = default)
    {
        var purchase = await db.NameChangeCardPurchases
            .Where(x => x.AccountId == accountId && x.FulfilledAt != null && x.ConsumedAt == null)
            .OrderBy(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (purchase is null) throw new InvalidOperationException("No available name change card. Purchase one first.");

        string oldName;
        switch (target)
        {
            case TargetAccount:
            {
                var trimmed = newName.Trim();
                if (!AccountNameRegex.IsMatch(trimmed) || trimmed.Length is < 2 or > 256)
                    throw new InvalidOperationException("Name can only contain letters, numbers, underscores, and hyphens (2-256 characters).");
                var current = await accountGrpc.GetAccountAsync(new DyGetAccountRequest { Id = accountId.ToString() }, cancellationToken: ct);
                oldName = current.Name;
                await accountGrpc.RenameAccountAsync(new DyRenameAccountRequest { AccountId = accountId.ToString(), NewName = trimmed }, cancellationToken: ct);
                newName = trimmed;
                break;
            }
            case TargetRealm:
            {
                if (targetId is null) throw new InvalidOperationException("target_id is required.");
                var realm = await db.Realms.FirstOrDefaultAsync(r => r.Id == targetId && r.AccountId == accountId, ct);
                if (realm is null) throw new InvalidOperationException("Realm not found or not owned by you.");
                var slug = newName.Trim();
                if (string.IsNullOrWhiteSpace(slug)) throw new InvalidOperationException("Slug cannot be empty.");
                if (await db.Realms.AnyAsync(r => r.Slug.ToLower() == slug.ToLowerInvariant() && r.Id != realm.Id, ct))
                    throw new InvalidOperationException("A realm with this slug already exists.");
                oldName = realm.Slug;
                realm.Slug = slug;
                await db.SaveChangesAsync(ct);
                newName = slug;
                break;
            }
            case TargetPublisher:
            {
                if (targetId is null) throw new InvalidOperationException("target_id is required.");
                var publisher = await publisherGrpc.GetPublisherAsync(new DyGetPublisherRequest { Id = targetId.ToString() }, cancellationToken: ct);
                oldName = publisher.Publisher.Name;
                await publisherGrpc.RenamePublisherAsync(new DyRenamePublisherRequest
                {
                    PublisherId = targetId.ToString(),
                    AccountId = accountId.ToString(),
                    NewName = newName
                }, cancellationToken: ct);
                break;
            }
            default:
                throw new InvalidOperationException("Unsupported target type.");
        }

        purchase.ConsumedAt = clock.GetCurrentInstant();
        purchase.TargetType = target;
        purchase.TargetId = targetId;
        purchase.OldName = oldName;
        purchase.NewName = newName;
        await db.SaveChangesAsync(ct);
        return purchase;
    }
}

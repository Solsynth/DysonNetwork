namespace DysonNetwork.Passport.Affiliation;

public class AffiliationPurchaseOptions
{
    public bool Enabled { get; set; } = true;
    public decimal PricePoints { get; set; } = 100;
    public int MaxPurchases { get; set; } = 2;
    public int PurchasePeriodDays { get; set; } = 30;
}

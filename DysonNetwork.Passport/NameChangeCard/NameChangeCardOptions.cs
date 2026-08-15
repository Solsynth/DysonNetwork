namespace DysonNetwork.Passport.NameChangeCard;

public class NameChangeCardOptions
{
    public bool Enabled { get; set; } = true;
    public decimal PricePoints { get; set; } = 100;
    public int PurchasePeriodDays { get; set; } = 30;
    public int MaxPurchases { get; set; } = 1;
}

namespace DysonNetwork.Passport.Account;

public class AppleWalletOptions
{
    public string PassTypeIdentifier { get; set; } = string.Empty;
    public string TeamIdentifier { get; set; } = string.Empty;
    public string OrganizationName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string LogoText { get; set; } = string.Empty;
    public string ForegroundColor { get; set; } = "rgb(255, 255, 255)";
    public string BackgroundColor { get; set; } = "rgb(91, 93, 148)";
    public string LabelColor { get; set; } = "rgb(224, 242, 254)";
    public string TermsText { get; set; } = "Solsynth © All rights reserved.";
    public string BarcodeAltText { get; set; } = "MEMBER CODE";
    public string WebServiceUrl { get; set; } = string.Empty;
    public string SigningCertificatePath { get; set; } = string.Empty;
    public string SigningCertificatePassword { get; set; } = string.Empty;
    public string AppleWwdrCertificatePath { get; set; } = string.Empty;
    public string? IconPath { get; set; }
    public string? Icon2XPath { get; set; }
    public string? Icon3XPath { get; set; }
    public string? LogoPath { get; set; }
    public string? Logo2XPath { get; set; }
    public string? Logo3XPath { get; set; }
    public string? ThumbnailPath { get; set; }
    public string? Thumbnail2XPath { get; set; }
    public string? Thumbnail3XPath { get; set; }
    public string? SiteUrl { get; set; }
    public int? AssociatedStoreIdentifier { get; set; }
}

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Registry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NodaTime;
using Passbook.Generator;
using Passbook.Generator.Fields;

namespace DysonNetwork.Passport.Account;

public class ApplePassService(
    AppDatabase db,
    AccountService accounts,
    RemoteSubscriptionService remoteSubscription,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    IWebHostEnvironment environment,
    IOptions<AppleWalletOptions> options,
    ILogger<ApplePassService> logger
)
{
    private static readonly X509KeyStorageFlags CertificateFlags =
        X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable;

    private readonly AppleWalletOptions _options = options.Value;

    public async Task<byte[]> GenerateMemberPassAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        ValidateOptions();

        var account = await accounts.GetAccount(accountId)
            ?? throw new InvalidOperationException("Account was not found.");
        account.Profile ??= await accounts.GetOrCreateAccountProfileAsync(accountId);

        try
        {
            var subscription = await remoteSubscription.GetPerkSubscription(account.Id);
            if (subscription is not null)
                account.PerkSubscription = SnWalletSubscription.FromProtoValue(subscription).ToReference();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to hydrate perk subscription for Apple Wallet pass for {AccountId}", account.Id);
        }

        var passRecord = await GetOrCreatePassAsync(account.Id, cancellationToken);
        var currentTag = BuildLastUpdatedTag(account);
        if (!string.Equals(passRecord.LastUpdatedTag, currentTag, StringComparison.Ordinal))
        {
            passRecord.LastUpdatedTag = currentTag;
            db.Update(passRecord);
            await db.SaveChangesAsync(cancellationToken);
        }

        var request = await BuildPassRequestAsync(account, passRecord, cancellationToken);
        var generator = new PassGenerator();
        return generator.Generate(request);
    }

    public async Task<SnApplePass> GetOrCreatePassAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var pass = await db.ApplePasses
            .Include(p => p.Registrations)
            .FirstOrDefaultAsync(
                p => p.AccountId == accountId && p.PassTypeIdentifier == _options.PassTypeIdentifier,
                cancellationToken
            );

        if (pass is not null) return pass;

        pass = new SnApplePass
        {
            AccountId = accountId,
            PassTypeIdentifier = _options.PassTypeIdentifier,
            SerialNumber = accountId.ToString(),
            AuthenticationToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant(),
            LastUpdatedTag = string.Empty
        };

        db.ApplePasses.Add(pass);
        await db.SaveChangesAsync(cancellationToken);
        return pass;
    }

    public async Task<SnApplePass?> GetPassBySerialAsync(
        string passTypeIdentifier,
        string serialNumber,
        CancellationToken cancellationToken = default)
    {
        return await db.ApplePasses
            .Include(p => p.Registrations)
            .FirstOrDefaultAsync(
                p => p.PassTypeIdentifier == passTypeIdentifier && p.SerialNumber == serialNumber,
                cancellationToken
            );
    }

    public bool IsAuthorized(SnApplePass pass, string? authorizationHeader)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader)) return false;
        const string scheme = "ApplePass ";
        if (!authorizationHeader.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)) return false;

        var token = authorizationHeader[scheme.Length..].Trim();
        return string.Equals(token, pass.AuthenticationToken, StringComparison.Ordinal);
    }

    public async Task RegisterDeviceAsync(
        SnApplePass pass,
        string deviceLibraryIdentifier,
        string pushToken,
        CancellationToken cancellationToken = default)
    {
        var registration = await db.ApplePassRegistrations.FirstOrDefaultAsync(
            r => r.PassId == pass.Id && r.DeviceLibraryIdentifier == deviceLibraryIdentifier,
            cancellationToken
        );

        if (registration is null)
        {
            registration = new SnApplePassRegistration
            {
                PassId = pass.Id,
                DeviceLibraryIdentifier = deviceLibraryIdentifier,
                PushToken = pushToken
            };
            db.ApplePassRegistrations.Add(registration);
        }
        else if (!string.Equals(registration.PushToken, pushToken, StringComparison.Ordinal))
        {
            registration.PushToken = pushToken;
            db.Update(registration);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> UnregisterDeviceAsync(
        SnApplePass pass,
        string deviceLibraryIdentifier,
        CancellationToken cancellationToken = default)
    {
        var registration = await db.ApplePassRegistrations.FirstOrDefaultAsync(
            r => r.PassId == pass.Id && r.DeviceLibraryIdentifier == deviceLibraryIdentifier,
            cancellationToken
        );
        if (registration is null) return false;

        db.ApplePassRegistrations.Remove(registration);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<(List<string> SerialNumbers, string LastUpdated)> GetUpdatedSerialNumbersAsync(
        string deviceLibraryIdentifier,
        string passTypeIdentifier,
        string? passesUpdatedSince,
        CancellationToken cancellationToken = default)
    {
        var query = db.ApplePassRegistrations
            .Where(r => r.DeviceLibraryIdentifier == deviceLibraryIdentifier)
            .Where(r => r.Pass.PassTypeIdentifier == passTypeIdentifier)
            .Select(r => r.Pass)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(passesUpdatedSince))
            query = query.Where(p => string.CompareOrdinal(p.LastUpdatedTag, passesUpdatedSince) > 0);

        var passes = await query
            .OrderBy(p => p.LastUpdatedTag)
            .ToListAsync(cancellationToken);

        var lastUpdated = passes.Count > 0
            ? passes[^1].LastUpdatedTag
            : passesUpdatedSince ?? string.Empty;

        return (passes.Select(p => p.SerialNumber).ToList(), lastUpdated);
    }

    public string BuildLastUpdatedTag(SnAccount account)
    {
        var parts = new List<string>
        {
            account.UpdatedAt.ToUnixTimeTicks().ToString(),
            account.Profile?.UpdatedAt.ToUnixTimeTicks().ToString() ?? "0",
            account.PerkSubscription?.Identifier ?? string.Empty,
            account.PerkSubscription?.PerkLevel.ToString() ?? "0",
            account.Name,
            account.Nick
        };

        var raw = string.Join("|", parts);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task<PassGeneratorRequest> BuildPassRequestAsync(
        SnAccount account,
        SnApplePass passRecord,
        CancellationToken cancellationToken)
    {
        var request = new PassGeneratorRequest
        {
            PassTypeIdentifier = _options.PassTypeIdentifier,
            TeamIdentifier = _options.TeamIdentifier,
            SerialNumber = passRecord.SerialNumber,
            Description = _options.Description,
            OrganizationName = _options.OrganizationName,
            LogoText = _options.LogoText,
            ForegroundColor = _options.ForegroundColor,
            BackgroundColor = _options.BackgroundColor,
            LabelColor = _options.LabelColor,
            Style = PassStyle.Generic,
            PassbookCertificate = LoadCertificate(_options.SigningCertificatePath, _options.SigningCertificatePassword),
            AppleWWDRCACertificate = LoadCertificate(_options.AppleWwdrCertificatePath, password: null),
            AuthenticationToken = passRecord.AuthenticationToken,
            WebServiceUrl = _options.WebServiceUrl,
        };

        if (_options.AssociatedStoreIdentifier.HasValue)
            request.AssociatedStoreIdentifiers.Add(_options.AssociatedStoreIdentifier.Value);

        await AddImagesAsync(request, account, cancellationToken);

        request.AddHeaderField(new StandardField("h1", "USERNAME", $"@{account.Name}"));
        request.AddPrimaryField(new StandardField("p1", "NAME", GetDisplayName(account)));
        request.AddSecondaryField(new StandardField("s1", "MEMBER SINCE", GetMemberSince(account)));
        request.AddAuxiliaryField(new StandardField("a1", "STELLAR PROGRAM", GetProgramLabel(account)));
        request.AddBackField(new StandardField("b1", "Terms", _options.TermsText));
        request.AddBackField(new StandardField("b2", "Profile", BuildProfileUrl(account)));
        request.AddBackField(new StandardField("b3", "Account ID", account.Id.ToString()));

        var profileUrl = BuildProfileUrl(account);
        request.SetBarcode(BarcodeType.PKBarcodeFormatQR, profileUrl, "iso-8859-1", _options.BarcodeAltText);
        request.AddBarcode(BarcodeType.PKBarcodeFormatQR, profileUrl, "iso-8859-1", _options.BarcodeAltText);
        request.UserInfo.Add("id", account.Id.ToString());

        return request;
    }

    private async Task AddImagesAsync(PassGeneratorRequest request, SnAccount account, CancellationToken cancellationToken)
    {
        var profilePictureBytes = await TryLoadProfilePictureBytesAsync(account, cancellationToken);

        AddImageWithFallback(request, PassbookImage.Icon, profilePictureBytes, _options.IconPath, required: true);
        AddImageWithFallback(request, PassbookImage.Icon2X, profilePictureBytes, _options.Icon2XPath, required: true);
        AddImageWithFallback(request, PassbookImage.Icon3X, profilePictureBytes, _options.Icon3XPath, required: false);
        AddImageIfConfigured(request, PassbookImage.Logo, _options.LogoPath, required: false);
        AddImageIfConfigured(request, PassbookImage.Logo2X, _options.Logo2XPath, required: false);
        AddImageIfConfigured(request, PassbookImage.Logo3X, _options.Logo3XPath, required: false);
        AddImageWithFallback(request, PassbookImage.Thumbnail, profilePictureBytes, _options.ThumbnailPath, required: false);
        AddImageWithFallback(request, PassbookImage.Thumbnail2X, profilePictureBytes, _options.Thumbnail2XPath, required: false);
        AddImageWithFallback(request, PassbookImage.Thumbnail3X, profilePictureBytes, _options.Thumbnail3XPath, required: false);
    }

    private void AddImageWithFallback(
        PassGeneratorRequest request,
        PassbookImage image,
        byte[]? preferredBytes,
        string? fallbackPath,
        bool required)
    {
        if (preferredBytes is not null)
        {
            request.Images.Add(image, preferredBytes);
            return;
        }

        AddImageIfConfigured(request, image, fallbackPath, required);
    }

    private void AddImageIfConfigured(PassGeneratorRequest request, PassbookImage image, string? path, bool required)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            if (!required) return;
            throw new InvalidOperationException($"Apple Wallet asset path for {image} is not configured.");
        }

        var absolutePath = ResolvePath(path);
        if (!File.Exists(absolutePath))
            throw new InvalidOperationException($"Apple Wallet asset was not found: {absolutePath}");

        request.Images.Add(image, File.ReadAllBytes(absolutePath));
    }

    private X509Certificate2 LoadCertificate(string path, string? password)
    {
        var absolutePath = ResolvePath(path);
        if (!File.Exists(absolutePath))
            throw new InvalidOperationException($"Certificate file was not found: {absolutePath}");

        return new X509Certificate2(absolutePath, password, CertificateFlags);
    }

    private async Task<byte[]?> TryLoadProfilePictureBytesAsync(SnAccount account, CancellationToken cancellationToken)
    {
        var picture = account.Profile?.Picture;
        if (picture is null) return null;

        var imageUrl = picture.Url;
        if (string.IsNullOrWhiteSpace(imageUrl) && !string.IsNullOrWhiteSpace(picture.Id))
        {
            var fileUrl = configuration["FileUrl"];
            if (!string.IsNullOrWhiteSpace(fileUrl))
                imageUrl = $"{fileUrl.TrimEnd('/')}/{picture.Id}";
        }

        if (string.IsNullOrWhiteSpace(imageUrl)) return null;

        try
        {
            var client = httpClientFactory.CreateClient();
            using var response = await client.GetAsync(imageUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Failed to fetch account profile picture for Apple Wallet pass for {AccountId}: {StatusCode}",
                    account.Id,
                    (int)response.StatusCode
                );
                return null;
            }

            return await response.Content.ReadAsByteArrayAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch account profile picture for Apple Wallet pass for {AccountId}", account.Id);
            return null;
        }
    }

    private string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path)) return path;
        return Path.GetFullPath(Path.Combine(environment.ContentRootPath, path));
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.PassTypeIdentifier))
            throw new InvalidOperationException("AppleWallet:PassTypeIdentifier is required.");
        if (string.IsNullOrWhiteSpace(_options.TeamIdentifier))
            throw new InvalidOperationException("AppleWallet:TeamIdentifier is required.");
        if (string.IsNullOrWhiteSpace(_options.OrganizationName))
            throw new InvalidOperationException("AppleWallet:OrganizationName is required.");
        if (string.IsNullOrWhiteSpace(_options.Description))
            throw new InvalidOperationException("AppleWallet:Description is required.");
        if (string.IsNullOrWhiteSpace(_options.WebServiceUrl))
            throw new InvalidOperationException("AppleWallet:WebServiceUrl is required.");
        if (string.IsNullOrWhiteSpace(_options.SigningCertificatePath))
            throw new InvalidOperationException("AppleWallet:SigningCertificatePath is required.");
        if (string.IsNullOrWhiteSpace(_options.AppleWwdrCertificatePath))
            throw new InvalidOperationException("AppleWallet:AppleWwdrCertificatePath is required.");
    }

    private string BuildProfileUrl(SnAccount account)
    {
        var siteUrl = _options.SiteUrl ?? configuration["SiteUrl"] ?? string.Empty;
        return $"{siteUrl.TrimEnd('/')}/accounts/{account.Name}";
    }

    private static string GetDisplayName(SnAccount account)
    {
        var profile = account.Profile;
        var fullName = string.Join(
            " ",
            new[] { profile?.FirstName, profile?.MiddleName, profile?.LastName }
                .Where(v => !string.IsNullOrWhiteSpace(v))
        ).Trim();

        if (!string.IsNullOrWhiteSpace(fullName)) return fullName;
        if (!string.IsNullOrWhiteSpace(account.Nick)) return account.Nick;
        return account.Name;
    }

    private static string GetMemberSince(SnAccount account)
    {
        var instant = account.CreatedAt != default
            ? account.CreatedAt
            : account.Profile?.CreatedAt ?? SystemClock.Instance.GetCurrentInstant();
        return instant.InUtc().Year.ToString();
    }

    private static string GetProgramLabel(SnAccount account)
    {
        if (account.PerkSubscription is null) return "Standard";
        if (!string.IsNullOrWhiteSpace(account.PerkSubscription.DisplayName)) return account.PerkSubscription.DisplayName;
        if (account.PerkSubscription.PerkLevel > 0) return $"Level {account.PerkSubscription.PerkLevel}";
        return !string.IsNullOrWhiteSpace(account.PerkSubscription.Identifier)
            ? account.PerkSubscription.Identifier
            : "Standard";
    }
}

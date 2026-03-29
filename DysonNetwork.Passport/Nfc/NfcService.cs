using System.Security.Cryptography;
using System.Text;
using DysonNetwork.Passport.Account;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Passport.Nfc;

public class NfcResolveResult
{
    public SnAccount User { get; set; } = null!;
    public SnAccountProfile? Profile { get; set; }
    public bool IsFriend { get; set; }
    public List<string> Actions { get; set; } = [];
}

public class NfcService(
    AppDatabase db,
    AccountService accounts,
    RelationshipService relationships,
    IConfiguration configuration,
    ILogger<NfcService> logger
)
{
    private int ReplayWindow => Math.Max(0, configuration.GetValue<int?>("Nfc:ReplayWindow") ?? 0);

    /// <summary>
    /// Resolve SUN URL parameters (e, c, mac) to a user profile.
    /// Verifies MAC, checks replay protection, and returns user info.
    /// </summary>
    public async Task<NfcResolveResult?> ResolveTagAsync(
        string enc,
        string counterStr,
        string mac,
        Guid? observerUserId = null,
        CancellationToken cancellationToken = default)
    {
        byte[] encBytes;
        byte[] macBytes;
        int counter;

        try
        {
            encBytes = Convert.FromBase64String(enc);
            macBytes = Convert.FromBase64String(mac);
            counter = int.Parse(counterStr);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            logger.LogWarning("Invalid SUN URL parameter format");
            return null;
        }

        var activeTags = await db.NfcTags
            .Where(t => t.IsActive)
            .ToListAsync(cancellationToken);

        foreach (var tag in activeTags)
        {
            if (tag.SunKey.Length != 16) continue;

            var expectedMac = ComputeAesCmac(tag.SunKey, encBytes, counter);
            if (!CryptographicOperations.FixedTimeEquals(expectedMac, macBytes))
                continue;

            var uid = DecryptPiccData(tag.SunKey, encBytes);
            if (string.IsNullOrEmpty(uid) || uid != tag.Uid)
                continue;

            if (counter <= tag.Counter - ReplayWindow)
            {
                logger.LogWarning("Replay detected for tag {TagId}: counter {Counter} <= last {LastCounter}",
                    tag.Id, counter, tag.Counter);
                return null;
            }

            var now = SystemClock.Instance.GetCurrentInstant();
            tag.Counter = counter;
            tag.LastSeenAt = now;
            await db.SaveChangesAsync(cancellationToken);

            var account = await accounts.GetAccount(tag.UserId);
            if (account is null) return null;

            var profile = account.Profile;
            var isFriend = false;

            if (observerUserId.HasValue && observerUserId.Value != tag.UserId)
            {
                isFriend = await relationships.HasRelationshipWithStatus(
                    observerUserId.Value, tag.UserId);

                var blocked =
                    await relationships.HasRelationshipWithStatus(observerUserId.Value, tag.UserId,
                        RelationshipStatus.Blocked) ||
                    await relationships.HasRelationshipWithStatus(tag.UserId, observerUserId.Value,
                        RelationshipStatus.Blocked);
                if (blocked) return null;
            }

            var actions = new List<string> { "view_profile" };
            if (observerUserId.HasValue && observerUserId.Value != tag.UserId)
                actions.Add("add_friend");

            return new NfcResolveResult
            {
                User = account,
                Profile = profile,
                IsFriend = isFriend,
                Actions = actions
            };
        }

        return null;
    }

    /// <summary>
    /// Register a new NFC tag for a user.
    /// </summary>
    public async Task<SnNfcTag> RegisterTagAsync(
        Guid userId,
        string uid,
        byte[] sunKey,
        string? label = null,
        CancellationToken cancellationToken = default)
    {
        if (sunKey.Length != 16)
            throw new ArgumentException("SUN key must be exactly 16 bytes (AES-128).");

        var existing = await db.NfcTags
            .FirstOrDefaultAsync(t => t.Uid == uid, cancellationToken);

        if (existing is not null)
            throw new InvalidOperationException("This NFC tag is already registered.");

        var tag = new SnNfcTag
        {
            Uid = uid,
            UserId = userId,
            SunKey = sunKey,
            Label = label
        };

        db.NfcTags.Add(tag);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("NFC tag {TagId} registered for user {UserId}", tag.Id, userId);
        return tag;
    }

    /// <summary>
    /// Unregister (soft-delete) an NFC tag.
    /// </summary>
    public async Task<bool> UnregisterTagAsync(
        Guid userId,
        Guid tagId,
        CancellationToken cancellationToken = default)
    {
        var tag = await db.NfcTags
            .FirstOrDefaultAsync(t => t.Id == tagId && t.UserId == userId, cancellationToken);

        if (tag is null) return false;

        tag.IsActive = false;
        tag.DeletedAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("NFC tag {TagId} unregistered by user {UserId}", tagId, userId);
        return true;
    }

    /// <summary>
    /// List all active NFC tags for a user.
    /// </summary>
    public async Task<List<SnNfcTag>> ListTagsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await db.NfcTags
            .Where(t => t.UserId == userId && t.IsActive)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Update tag metadata (label, active status).
    /// </summary>
    public async Task<SnNfcTag?> UpdateTagAsync(
        Guid userId,
        Guid tagId,
        string? label = null,
        bool? isActive = null,
        CancellationToken cancellationToken = default)
    {
        var tag = await db.NfcTags
            .FirstOrDefaultAsync(t => t.Id == tagId && t.UserId == userId, cancellationToken);

        if (tag is null) return null;

        if (label is not null) tag.Label = label;
        if (isActive.HasValue) tag.IsActive = isActive.Value;

        await db.SaveChangesAsync(cancellationToken);
        return tag;
    }

    /// <summary>
    /// Lock a tag to prevent physical reprogramming.
    /// </summary>
    public async Task<SnNfcTag?> LockTagAsync(
        Guid userId,
        Guid tagId,
        CancellationToken cancellationToken = default)
    {
        var tag = await db.NfcTags
            .FirstOrDefaultAsync(t => t.Id == tagId && t.UserId == userId, cancellationToken);

        if (tag is null) return null;

        tag.LockedAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("NFC tag {TagId} locked by user {UserId}", tagId, userId);
        return tag;
    }

    /// <summary>
    /// Compute AES-CMAC for SUN MAC verification.
    /// NTAG424 uses AES-CMAC over the SDM MAC input data.
    /// </summary>
    private static byte[] ComputeAesCmac(byte[] key, byte[] encData, int counter)
    {
        // NTAG424 SUN MAC input: PICCDataTag (0x08 || UID) + SDMReadCtr
        // The enc bytes already contain the encrypted PICCData.
        // The MAC input is: encData concatenated with counter as 3-byte little-endian.
        var counterBytes = new byte[3];
        counterBytes[0] = (byte)(counter & 0xFF);
        counterBytes[1] = (byte)((counter >> 8) & 0xFF);
        counterBytes[2] = (byte)((counter >> 16) & 0xFF);

        var macInput = new byte[encData.Length + 3];
        Buffer.BlockCopy(encData, 0, macInput, 0, encData.Length);
        Buffer.BlockCopy(counterBytes, 0, macInput, encData.Length, 3);

        return Aes128Cmac(key, macInput);
    }

    /// <summary>
    /// Decrypt PICCData from SUN encrypted data to extract UID.
    /// NTAG424 encrypts PICCData (PICCDataTag + UID + SDMReadCtr) using AES-CBC.
    /// </summary>
    private static string? DecryptPiccData(byte[] key, byte[] encData)
    {
        try
        {
            // NTAG424 SUN uses AES-CBC with zero IV for PICCData encryption
            using var aes = Aes.Create();
            aes.Key = key;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None;
            aes.IV = new byte[16]; // Zero IV

            using var decryptor = aes.CreateDecryptor();
            var decrypted = decryptor.TransformFinalBlock(encData, 0, encData.Length);

            // PICCData format: [PICCDataTag (1 byte)] [UID (7 bytes)] [SDMReadCtr (3 bytes)]
            // Total: 11 bytes, padded to 16 with PKCS7
            if (decrypted.Length < 11) return null;

            // First byte should be 0x08 (PICCDataTag for UID)
            if (decrypted[0] != 0x08) return null;

            // Extract 7-byte UID
            var uidBytes = decrypted.AsSpan(1, 7);
            return Convert.ToHexString(uidBytes);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// AES-128-CMAC implementation per NIST SP 800-38B / RFC 4493.
    /// </summary>
    private static byte[] Aes128Cmac(byte[] key, byte[] message)
    {
        const int blockSize = 16;

        // Generate subkeys
        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;

        using var encryptor = aes.CreateEncryptor();
        var zeroBlock = new byte[blockSize];
        var lBlock = encryptor.TransformFinalBlock(zeroBlock, 0, blockSize);

        var k1 = GenerateSubkey(lBlock);
        var k2 = GenerateSubkey(k1);

        // Determine number of blocks
        int n = Math.Max(1, (message.Length + blockSize - 1) / blockSize);
        bool isComplete = message.Length % blockSize == 0 && message.Length > 0;

        // Prepare last block
        var lastBlock = new byte[blockSize];
        int lastBlockStart = (n - 1) * blockSize;
        int lastBlockLen = message.Length - lastBlockStart;

        if (isComplete)
        {
            // XOR with K1
            Buffer.BlockCopy(message, lastBlockStart, lastBlock, 0, blockSize);
            for (int i = 0; i < blockSize; i++)
                lastBlock[i] ^= k1[i];
        }
        else
        {
            // Pad with 10...0 and XOR with K2
            Buffer.BlockCopy(message, lastBlockStart, lastBlock, 0, lastBlockLen);
            lastBlock[lastBlockLen] = 0x80;
            // Remaining bytes are already zero from initialization
            for (int i = 0; i < blockSize; i++)
                lastBlock[i] ^= k2[i];
        }

        // CBC-MAC
        var xBlock = new byte[blockSize];
        for (int i = 0; i < n - 1; i++)
        {
            for (int j = 0; j < blockSize; j++)
                xBlock[j] ^= message[i * blockSize + j];
            xBlock = encryptor.TransformFinalBlock(xBlock, 0, blockSize);
        }

        // Final block
        for (int j = 0; j < blockSize; j++)
            xBlock[j] ^= lastBlock[j];
        xBlock = encryptor.TransformFinalBlock(xBlock, 0, blockSize);

        return xBlock;
    }

    /// <summary>
    /// Generate CMAC subkey by left-shifting and conditional XOR.
    /// </summary>
    private static byte[] GenerateSubkey(byte[] input)
    {
        const int blockSize = 16;
        const byte rb = 0x87; // Rb for 128-bit block size

        var output = new byte[blockSize];
        byte overflow = 0;

        for (int i = blockSize - 1; i >= 0; i--)
        {
            output[i] = (byte)((input[i] << 1) | overflow);
            overflow = (byte)((input[i] & 0x80) >> 7);
        }

        if ((input[0] & 0x80) != 0)
            output[blockSize - 1] ^= rb;

        return output;
    }
}

using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace DysonNetwork.Passport.Nfc;

/// <summary>
/// SDM (Secure Dynamic Messaging) crypto utilities for NFC tag validation.
///
/// The SDMFileReadKey is used directly as the AES key (no KDF).
/// Supports both AES-128 (16-byte key) and AES-256 (32-byte key).
/// </summary>
public static class NfcCrypto
{
    /// <summary>
    /// Decrypt SDM encrypted PICCData using the SDMFileReadKey directly.
    /// Returns uid and counter on successful decryption, null if the key is wrong.
    /// </summary>
    public static SunValidationResult? DecryptPiccData(
        byte[] sdMFileReadKey,
        byte[] encryptedPiccData,
        ILogger? logger = null)
    {
        if (encryptedPiccData.Length < 16)
        {
            logger?.LogWarning("PICCData too short: {Len} bytes", encryptedPiccData.Length);
            return null;
        }

        if (sdMFileReadKey.Length != 16 && sdMFileReadKey.Length != 32)
        {
            logger?.LogWarning("Invalid key length: {Len} bytes", sdMFileReadKey.Length);
            return null;
        }

        var keyHex = Convert.ToHexString(sdMFileReadKey.Take(4).ToArray());
        logger?.LogInformation(
            "SUN decrypt: key={KeyLen}B(first4: {KeyHex}), picc={PiccHex}",
            sdMFileReadKey.Length, keyHex, Convert.ToHexString(encryptedPiccData));

        try
        {
            using var aes = Aes.Create();
            aes.Key = sdMFileReadKey;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None;
            aes.IV = new byte[16];

            using var decryptor = aes.CreateDecryptor();
            var decrypted = decryptor.TransformFinalBlock(encryptedPiccData, 0, 16);
            var decHex = Convert.ToHexString(decrypted);

            logger?.LogInformation("SUN decrypt: result={DecHex}", decHex);

            var piccTag = decrypted[0];
            logger?.LogInformation("SUN decrypt: PICCDataTag=0x{Tag:X2} (expect 0xC7)", piccTag);

            if (piccTag != 0xC7)
            {
                logger?.LogWarning("SUN decrypt: PICCDataTag != 0xC7, wrong key");
                return null;
            }

            var uid = new byte[7];
            Array.Copy(decrypted, 1, uid, 0, 7);
            var uidHex = Convert.ToHexString(uid);
            var counter = decrypted[8] | (decrypted[9] << 8) | (decrypted[10] << 16);

            if (uid[0] != 0x04)
            {
                logger?.LogWarning("SUN decrypt: UID={Uid} doesn't start with 04, likely wrong key", uidHex);
                return null;
            }

            logger?.LogInformation("SUN decrypt: ✓ key matches! UID={Uid}, counter={Ctr}", uidHex, counter);

            return new SunValidationResult(uid, counter);
        }
        catch (CryptographicException ex)
        {
            logger?.LogWarning("SUN decrypt: AES error: {Msg}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Parse the UID parameter from the NFC scan URL.
    /// </summary>
    public static byte[]? ParseFullUidData(string uidHex, ILogger? logger = null)
    {
        if (string.IsNullOrEmpty(uidHex) || uidHex.Length < 32)
        {
            logger?.LogWarning("SUN parse: uidHex too short ({Len} chars, need >= 32)", uidHex?.Length ?? 0);
            return null;
        }

        var hexLength = uidHex.Length - (uidHex.Length % 2);

        try
        {
            var data = Convert.FromHexString(uidHex.AsSpan(0, hexLength));
            logger?.LogInformation("SUN parse: {Len} bytes from uidHex, first 16={First16}",
                data.Length, Convert.ToHexString(data.Take(16).ToArray()));
            return data;
        }
        catch (FormatException ex)
        {
            logger?.LogWarning("SUN parse: hex decode failed: {Msg}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Extract just the encrypted PICCData (first 16 bytes) from the full SDM data.
    /// </summary>
    public static byte[]? ExtractPiccDataBlock(byte[] fullData, ILogger? logger = null)
    {
        if (fullData.Length < 16)
        {
            logger?.LogWarning("SUN parse: data too short for PICCData ({Len} bytes)", fullData.Length);
            return null;
        }

        var piccData = new byte[16];
        Array.Copy(fullData, 0, piccData, 0, 16);
        logger?.LogInformation("SUN parse: extracted 16-byte PICCData block: {Hex}",
            Convert.ToHexString(piccData));
        return piccData;
    }

    /// <summary>
    /// Convert a 7-byte UID to an uppercase hex string.
    /// </summary>
    public static string FormatUid(byte[] uid) => Convert.ToHexString(uid);

    /// <summary>
    /// Result of a successful SUN validation.
    /// </summary>
    public record SunValidationResult(byte[] Uid, int ReadCtr);
}

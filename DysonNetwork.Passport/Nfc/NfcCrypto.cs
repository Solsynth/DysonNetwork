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
    /// <param name="sdMFileReadKey">Per-tag SDMFileReadKey (16 bytes for AES-128, 32 bytes for AES-256).</param>
    /// <param name="encryptedPiccData">Encrypted PICCData bytes (first block only, 16 bytes).</param>
    /// <param name="logger">Logger for debug output.</param>
    /// <returns>Validation result with UID and counter, or null if key is wrong.</returns>
    public static SunValidationResult? DecryptPiccData(
        byte[] sdMFileReadKey,
        byte[] encryptedPiccData,
        ILogger? logger = null)
    {
        if (encryptedPiccData.Length < 16)
        {
            logger?.LogDebug("PICCData too short: {Len} bytes", encryptedPiccData.Length);
            return null;
        }

        if (sdMFileReadKey.Length != 16 && sdMFileReadKey.Length != 32)
        {
            logger?.LogDebug("Invalid key length: {Len} bytes", sdMFileReadKey.Length);
            return null;
        }

        try
        {
            // Use SDMFileReadKey directly — no KDF
            using var aes = Aes.Create();
            aes.Key = sdMFileReadKey;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None;
            aes.IV = new byte[16]; // zero IV

            using var decryptor = aes.CreateDecryptor();
            // Only decrypt the first 16 bytes (PICCData block)
            var decrypted = decryptor.TransformFinalBlock(encryptedPiccData, 0, 16);

            logger?.LogDebug("Decrypted PICCData (first 16 bytes): {Hex}", Convert.ToHexString(decrypted));
            logger?.LogDebug("PICCDataTag byte: 0x{Tag:X2}", decrypted[0]);

            // Check PICCDataTag
            if (decrypted[0] != 0xC7)
            {
                logger?.LogDebug("PICCDataTag != 0xC7 (got 0x{Tag:X2}), wrong key?", decrypted[0]);
                return null;
            }

            var uid = new byte[7];
            Array.Copy(decrypted, 1, uid, 0, 7);
            var uidHex = Convert.ToHexString(uid);
            logger?.LogDebug("Decrypted UID: {Uid} (starts with 04: {Starts04})", uidHex, uid[0] == 0x04);

            // UID should start with 04 (ISO 14443-3A manufacturing code)
            if (uid[0] != 0x04)
            {
                logger?.LogDebug("UID doesn't start with 04, likely wrong key");
                return null;
            }

            var counter = decrypted[8] | (decrypted[9] << 8) | (decrypted[10] << 16);
            logger?.LogDebug("Decrypted counter: {Counter}", counter);

            return new SunValidationResult(uid, counter);
        }
        catch (CryptographicException ex)
        {
            logger?.LogDebug("AES decryption failed: {Message}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Parse the UID parameter from the NFC scan URL.
    /// Uses the full hex string — decrypts the first 16 bytes as PICCData,
    /// but also passes the full data for potential future use.
    /// </summary>
    /// <param name="uidHex">Full hex-encoded SDM data from the URL.</param>
    /// <param name="logger">Logger for debug output.</param>
    /// <returns>The full decoded bytes, or null if input is invalid.</returns>
    public static byte[]? ParseFullUidData(string uidHex, ILogger? logger = null)
    {
        if (string.IsNullOrEmpty(uidHex) || uidHex.Length < 32)
        {
            logger?.LogDebug("uidHex too short: {Len} chars (need >= 32)", uidHex?.Length ?? 0);
            return null;
        }

        // Ensure even length for hex decoding
        var hexLength = uidHex.Length - (uidHex.Length % 2);

        try
        {
            var data = Convert.FromHexString(uidHex.AsSpan(0, hexLength));
            logger?.LogDebug("Parsed {Len} bytes from {HexLen} hex chars", data.Length, hexLength);
            logger?.LogDebug("Full decoded data: {Hex}", Convert.ToHexString(data));
            return data;
        }
        catch (FormatException ex)
        {
            logger?.LogDebug("Hex decode failed: {Message}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Extract just the encrypted PICCData (first 16 bytes) from the full SDM data.
    /// </summary>
    public static byte[]? ExtractPiccDataBlock(byte[] fullData, ILogger? logger = null)
    {
        if (fullData.Length < 16)
            return null;

        var piccData = new byte[16];
        Array.Copy(fullData, 0, piccData, 0, 16);
        return piccData;
    }

    /// <summary>
    /// Convert a 7-byte UID to an uppercase hex string (e.g., "04A1B2C3D4E5F6").
    /// </summary>
    public static string FormatUid(byte[] uid)
    {
        return Convert.ToHexString(uid);
    }

    /// <summary>
    /// Result of a successful SUN validation.
    /// </summary>
    public record SunValidationResult(byte[] Uid, int ReadCtr);
}

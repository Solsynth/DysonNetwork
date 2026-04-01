using System.Security.Cryptography;

namespace DysonNetwork.Passport.Nfc;

/// <summary>
/// SDM (Secure Dynamic Messaging) crypto utilities for NFC tag validation.
///
/// The SDMFileReadKey is used **directly** as the AES-128 CBC key (no KDF).
/// Encrypted PICCData is decrypted to extract UID and counter for tag matching and replay protection.
/// Validation is by PICCData tag byte (0xC7) and UID format check (starts with 04).
/// </summary>
public static class NfcCrypto
{
    private const byte PaddedPrefix = 0x80;

    /// <summary>
    /// Decrypt SDM encrypted PICCData using the SDMFileReadKey directly.
    /// Returns uid and counter on successful decryption, null if the key is wrong.
    /// </summary>
    /// <param name="sdMFileReadKey">16-byte per-tag SDMFileReadKey (used directly as AES key).</param>
    /// <param name="encryptedPiccData">16 bytes of encrypted PICCData.</param>
    /// <returns>Validation result with UID and counter, or null if key is wrong.</returns>
    public static SunValidationResult? DecryptPiccData(byte[] sdMFileReadKey, byte[] encryptedPiccData)
    {
        if (encryptedPiccData.Length != 16)
            return null;

        if (sdMFileReadKey.Length != 16)
            return null;

        try
        {
            // Use SDMFileReadKey directly — no KDF
            using var aes = Aes.Create();
            aes.Key = sdMFileReadKey;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None;
            aes.IV = new byte[16]; // zero IV

            using var decryptor = aes.CreateDecryptor();
            var decrypted = decryptor.TransformFinalBlock(encryptedPiccData, 0, 16);

            // Validate PICCData structure
            var piccDataTag = decrypted[0];
            if (piccDataTag != 0xC7)
                return null; // wrong key or invalid data

            var uid = new byte[7];
            Array.Copy(decrypted, 1, uid, 0, 7);

            // UID should start with 04 (ISO 14443-3A manufacturing code)
            if (uid[0] != 0x04)
                return null; // suspicious UID, likely wrong key

            var counter = decrypted[8] | (decrypted[9] << 8) | (decrypted[10] << 16);

            return new SunValidationResult(uid, counter);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    /// <summary>
    /// Parse the UID parameter from the NFC scan URL.
    /// The uid hex string contains the encrypted PICCData (first 32 hex chars = 16 bytes).
    /// Trailing bytes (e.g., counter, MAC) are ignored.
    /// </summary>
    /// <param name="uidHex">Hex-encoded SDM data (at least 32 hex chars).</param>
    /// <returns>The 16-byte encrypted PICCData, or null if input is invalid.</returns>
    public static byte[]? ParsePiccData(string uidHex)
    {
        if (string.IsNullOrEmpty(uidHex) || uidHex.Length < 32)
            return null;

        try
        {
            // Take only the first 32 hex chars (16 bytes)
            return Convert.FromHexString(uidHex.AsSpan(0, 32));
        }
        catch (FormatException)
        {
            return null;
        }
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

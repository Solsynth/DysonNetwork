using System.Security.Cryptography;
using System.Text;

namespace DysonNetwork.Passport.Nfc;

/// <summary>
/// NTAG424 SUN (Secure Unique NFC) / SDM (Secure Dynamic Messaging) crypto utilities.
///
/// Key hierarchy:
///   SDMFileReadKey (stored in DB) → KDF → SUN_ENC_KEY (for PICCData decryption)
///                                       → SUN_MAC_KEY (for CMAC verification)
///
/// KDF: AES-CMAC with the SDMFileReadKey on a prefix || UID || ReadCtr || 0x80 input.
/// </summary>
public static class NfcCrypto
{
    private const byte PaddedPrefix = 0x80;

    /// <summary>
    /// Derive a session key from the SDMFileReadKey using a prefix byte, UID, and read counter.
    /// Used to derive both SUN_ENC_KEY (prefix=0xC7) and SUN_MAC_KEY (prefix=0x01).
    /// </summary>
    public static byte[] DeriveSessionKey(byte[] sdMFileReadKey, byte[] uid, int readCtr, byte prefix)
    {
        // Input: prefix (1) || UID (7) || ReadCtr (3, little-endian) || 0x80 (1) = 12 bytes
        var input = new byte[12];
        input[0] = prefix;
        Array.Copy(uid, 0, input, 1, 7);
        input[8] = (byte)(readCtr & 0xFF);
        input[9] = (byte)((readCtr >> 8) & 0xFF);
        input[10] = (byte)((readCtr >> 16) & 0xFF);
        input[11] = PaddedPrefix;

        return AesCmac(sdMFileReadKey, input);
    }

    /// <summary>
    /// Verify the CMAC of an NTAG424 SUN message.
    /// The CMAC is computed over (e_bytes || readCtr_bytes) using the SUN_MAC_KEY.
    /// </summary>
    /// <returns>True if the MAC is valid.</returns>
    public static bool VerifyCmac(byte[] sdMFileReadKey, byte[] uid, int readCtr, byte[] eBytes, byte[] receivedMac)
    {
        var sunMacKey = DeriveSessionKey(sdMFileReadKey, uid, readCtr, 0x01);

        // MAC input: e_bytes (variable length) || readCtr (3 bytes LE)
        var macInput = new byte[eBytes.Length + 3];
        Array.Copy(eBytes, 0, macInput, 0, eBytes.Length);
        macInput[eBytes.Length] = (byte)(readCtr & 0xFF);
        macInput[eBytes.Length + 1] = (byte)((readCtr >> 8) & 0xFF);
        macInput[eBytes.Length + 2] = (byte)((readCtr >> 16) & 0xFF);

        var computedMac = AesCmac(sunMacKey, macInput);

        return CryptographicOperations.FixedTimeEquals(computedMac, receivedMac);
    }

    /// <summary>
    /// Decrypt NTAG424 SUN encrypted PICCData using the SDMFileReadKey.
    /// Returns the UID and read counter extracted from the decrypted PICCData.
    /// </summary>
    /// <returns>(uid, readCtr) on success.</returns>
    public static (byte[] uid, int readCtr) DecryptPiccData(byte[] sdMFileReadKey, byte[] encryptedPiccData, int readCtr)
    {
        if (encryptedPiccData.Length != 16)
            throw new ArgumentException("Encrypted PICCData must be exactly 16 bytes (one AES block).");

        // Derive the SUN_ENC_KEY using an all-zeros UID (the UID is unknown until we decrypt).
        // For decryption, NXP specifies using a zeroed UID placeholder for the KDF input.
        var zeroUid = new byte[7];
        var sunEncKey = DeriveSessionKey(sdMFileReadKey, zeroUid, readCtr, 0xC7);

        // AES-CBC decrypt with zero IV
        using var aes = Aes.Create();
        aes.Key = sunEncKey;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        aes.IV = new byte[16];

        using var decryptor = aes.CreateDecryptor();
        var decrypted = decryptor.TransformFinalBlock(encryptedPiccData, 0, 16);

        // Parse PICCData layout:
        // Byte 0:     PICCDataTag (0xC7 for AES-128)
        // Bytes 1-7:  UID (7 bytes)
        // Bytes 8-10: SDMReadCtr (3 bytes, little-endian)
        var piccDataTag = decrypted[0];
        if (piccDataTag != 0xC7)
            throw new CryptographicException($"Invalid PICCDataTag: expected 0xC7, got 0x{piccDataTag:X2}.");

        var uid = new byte[7];
        Array.Copy(decrypted, 1, uid, 0, 7);

        var sdmReadCtr = decrypted[8] | (decrypted[9] << 8) | (decrypted[10] << 16);

        return (uid, sdmReadCtr);
    }

    /// <summary>
    /// Full validation of an NTAG424 SUN message.
    /// Verifies the CMAC, decrypts the PICCData, and returns the UID and read counter.
    /// </summary>
    /// <param name="sdMFileReadKey">16-byte per-tag SDMFileReadKey.</param>
    /// <param name="eBase64">Base64-encoded encrypted PICCData.</param>
    /// <param name="readCtr">The read counter from the SUN URL parameter 'c'.</param>
    /// <param name="macBase64">Base64-encoded CMAC.</param>
    /// <returns>True if the SUN message is cryptographically valid.</returns>
    /// <exception cref="CryptographicException">Thrown if verification or decryption fails.</exception>
    public static SunValidationResult Validate(
        byte[] sdMFileReadKey,
        string eBase64,
        int readCtr,
        string macBase64)
    {
        var eBytes = Convert.FromBase64String(eBase64);
        var macBytes = Convert.FromBase64String(macBase64);

        // Step 1: Decrypt to extract the UID
        var (uid, extractedCtr) = DecryptPiccData(sdMFileReadKey, eBytes, readCtr);

        // Step 2: Verify the counter matches
        if (extractedCtr != readCtr)
            throw new CryptographicException(
                $"Counter mismatch: URL says {readCtr}, decrypted PICCData says {extractedCtr}.");

        // Step 3: Verify CMAC using the extracted UID
        if (!VerifyCmac(sdMFileReadKey, uid, readCtr, eBytes, macBytes))
            throw new CryptographicException("CMAC verification failed.");

        return new SunValidationResult(uid, extractedCtr);
    }

    /// <summary>
    /// AES-CMAC implementation per NIST SP 800-38B / RFC 4493.
    /// </summary>
    internal static byte[] AesCmac(byte[] key, byte[] message)
    {
        const int blockSize = 16;

        // Generate sub-keys (K1, K2)
        var zeroBlock = new byte[blockSize];
        byte[] l;

        using (var aes = Aes.Create())
        {
            aes.Key = key;
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.None;
            using var encryptor = aes.CreateEncryptor();
            l = encryptor.TransformFinalBlock(zeroBlock, 0, blockSize);
        }

        var k1 = GenerateSubKey(l);
        var k2 = GenerateSubKey(k1);

        // Process message
        var n = (message.Length + blockSize - 1) / blockSize; // ceil(len / 16)

        byte[] lastBlock;
        if (n == 0)
        {
            n = 1;
            lastBlock = PadBlock([], 0);
        }
        else if (message.Length % blockSize == 0)
        {
            // Last block is the XOR of the last block with K1
            lastBlock = XorBlock(message, (n - 1) * blockSize, k1);
        }
        else
        {
            // Last block is padded message XORed with K2
            var padded = PadBlock(message, message.Length % blockSize);
            lastBlock = XorBlock(padded, (n - 1) * blockSize, k2);
        }

        // CBC-MAC
        using var aes2 = Aes.Create();
        aes2.Key = key;
        aes2.Mode = CipherMode.ECB;
        aes2.Padding = PaddingMode.None;
        using var encryptor2 = aes2.CreateEncryptor();

        var x = new byte[blockSize];
        for (var i = 0; i < n - 1; i++)
        {
            var block = new byte[blockSize];
            Array.Copy(message, i * blockSize, block, 0, blockSize);
            x = XorBlock(x, block);
            x = encryptor2.TransformFinalBlock(x, 0, blockSize);
        }

        x = XorBlock(x, lastBlock);
        return encryptor2.TransformFinalBlock(x, 0, blockSize);
    }

    private static byte[] GenerateSubKey(byte[] block)
    {
        const int blockSize = 16;
        var r = new byte[blockSize];
        var msb = (block[0] >> 7) & 1;

        // Shift left by 1 bit
        for (var i = blockSize - 1; i > 0; i--)
        {
            r[i] = (byte)((block[i] << 1) | ((block[i - 1] >> 7) & 1));
        }
        r[0] = (byte)(block[0] << 1);

        // If MSB was 1, XOR with Rb (0x00...0087)
        if (msb == 1)
        {
            r[blockSize - 1] ^= 0x87;
        }

        return r;
    }

    private static byte[] PadBlock(byte[] data, int length)
    {
        var padded = new byte[16];
        if (length > 0)
            Array.Copy(data, padded, Math.Min(length, 16));
        padded[length] = 0x80;
        return padded;
    }

    private static byte[] XorBlock(byte[] a, byte[] b)
    {
        var result = new byte[16];
        for (var i = 0; i < 16; i++)
            result[i] = (byte)(a[i] ^ b[i]);
        return result;
    }

    private static byte[] XorBlock(byte[] a, int offset, byte[] b)
    {
        var result = new byte[16];
        for (var i = 0; i < 16; i++)
            result[i] = (byte)(a[offset + i] ^ b[i]);
        return result;
    }

    /// <summary>
    /// Result of a successful SUN validation.
    /// </summary>
    public record SunValidationResult(byte[] Uid, int ReadCtr);
}

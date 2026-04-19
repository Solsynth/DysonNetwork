using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace DysonNetwork.Passport.Nfc;

/// <summary>
/// SDM (Secure Dynamic Messaging) crypto utilities for NFC tag validation.
/// NTAG 424 DNA SDM decoder / verifier following NXP AN12196.
///
/// Key differences from old implementation:
/// - PICCData is decrypted with SDMMetaReadKey using AES-CBC with zero IV
/// - Session keys are derived from SDMFileReadKey + UID + SDMReadCtr using AES-CMAC
/// - File data is decrypted with session encryption key
/// - CMAC is verified with session MAC key
/// </summary>
public static class NfcCrypto
{
    private static readonly byte[] ZeroIv = new byte[16];

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

    // ========== NTAG 424 DNA SDM Methods ==========

    /// <summary>
    /// Decrypt PICCData using SDMMetaReadKey with AES-CBC and zero IV.
    /// Per AN12196: PICCData is decrypted with AES-CBC and zero IV.
    /// </summary>
    public static byte[]? DecryptPiccDataWithMetaKey(
        byte[] metaKey,
        byte[] encryptedPiccData,
        ILogger? logger = null)
    {
        if (encryptedPiccData.Length != 16)
        {
            logger?.LogWarning("PICCData must be exactly 16 bytes, got {Len}", encryptedPiccData.Length);
            return null;
        }

        if (metaKey.Length != 16)
        {
            logger?.LogWarning("Meta key must be 16 bytes, got {Len}", metaKey.Length);
            return null;
        }

        try
        {
            using var aes = Aes.Create();
            aes.Key = metaKey;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None;
            aes.IV = ZeroIv;

            using var decryptor = aes.CreateDecryptor();
            var decrypted = decryptor.TransformFinalBlock(encryptedPiccData, 0, 16);

            logger?.LogInformation("PICCData decrypted: {Decrypted}", Convert.ToHexString(decrypted));
            return decrypted;
        }
        catch (CryptographicException ex)
        {
            logger?.LogWarning("PICCData decryption failed: {Msg}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Parse decrypted PICCData structure from AN12196.
    /// Layout:
    /// - byte 0: PICCDataTag (bits: UID mirroring, CTR mirroring, UID length nibble)
    /// - next N: UID (if mirrored)
    /// - next 3: SDMReadCtr (if mirrored, LSB first)
    /// - rest: random padding
    /// </summary>
    public static PiccData? ParsePiccData(byte[] plain, ILogger? logger = null)
    {
        if (plain.Length != 16)
        {
            logger?.LogWarning("PICCData must be exactly 16 bytes");
            return null;
        }

        var tag = plain[0];
        var uidMirroring = (tag & 0x80) != 0;
        var ctrMirroring = (tag & 0x40) != 0;
        var uidLengthNibble = tag & 0x0F;

        // NXP example uses nibble 0x7 => 7-byte UID
        var uidLen = uidMirroring ? uidLengthNibble : 0;

        var pos = 1;
        byte[]? uid = null;

        if (uidMirroring)
        {
            if (uidLen <= 0 || pos + uidLen > plain.Length)
            {
                logger?.LogWarning("Invalid UID length nibble {Nibble}", uidLengthNibble);
                return null;
            }
            uid = new byte[uidLen];
            Array.Copy(plain, pos, uid, 0, uidLen);
            pos += uidLen;
        }

        byte[]? readCtrLsb = null;
        int? readCtrInt = null;

        if (ctrMirroring)
        {
            if (pos + 3 > plain.Length)
            {
                logger?.LogWarning("PICCData too short to contain SDMReadCtr");
                return null;
            }
            readCtrLsb = new byte[3];
            Array.Copy(plain, pos, readCtrLsb, 0, 3);
            // SDMReadCtr is represented LSB first
            readCtrInt = readCtrLsb[0] | (readCtrLsb[1] << 8) | (readCtrLsb[2] << 16);
            pos += 3;
        }

        var randomPadding = new byte[16 - pos];
        Array.Copy(plain, pos, randomPadding, 0, 16 - pos);

        return new PiccData(
            Raw: plain,
            Tag: tag,
            Uid: uid,
            ReadCtrLsb: readCtrLsb,
            ReadCtrInt: readCtrInt,
            RandomPadding: randomPadding,
            UidMirroring: uidMirroring,
            CtrMirroring: ctrMirroring,
            UidLengthNibble: uidLengthNibble
        );
    }

    /// <summary>
    /// Build SV1/SV2 for session key derivation per AN12196.
    /// Format:
    /// - prefix (2 bytes): C33C for SV1, 3CC3 for SV2
    /// - 0001 (2 bytes)
    /// - 0080 (2 bytes)
    /// - UID (7 bytes)
    /// - SDMReadCtr (3 bytes, LSB first)
    /// - zero padding to 16-byte block boundary
    /// </summary>
    public static byte[] BuildSv(byte[] prefix, byte[] uid, byte[] readCtrLsb, ILogger? logger = null)
    {
        if (prefix.Length != 2)
            throw new ArgumentException("Prefix must be 2 bytes");
        if (uid.Length != 7)
            throw new ArgumentException($"UID must be 7 bytes, got {uid.Length}");
        if (readCtrLsb.Length != 3)
            throw new ArgumentException("SDMReadCtr must be 3 bytes");

        var sv = new byte[16];
        Array.Copy(prefix, 0, sv, 0, 2);
        sv[2] = 0x00;
        sv[3] = 0x01;
        sv[4] = 0x00;
        sv[5] = 0x80;
        Array.Copy(uid, 0, sv, 6, 7);
        Array.Copy(readCtrLsb, 0, sv, 13, 3);
        // Last 16-16=0 bytes, so no padding needed for 7-byte UID + 3-byte counter

        return sv;
    }

    /// <summary>
    /// Derive session keys using AES-CMAC per AN12196.
    /// KSesSDMFileReadENC = AES-CMAC(FileReadKey, SV1)
    /// KSesSDMFileReadMAC = AES-CMAC(FileReadKey, SV2)
    /// </summary>
    public static (byte[] KsesEnc, byte[] KsesMac) DeriveSessionKeys(
        byte[] fileReadKey,
        byte[] uid,
        byte[] readCtrLsb,
        ILogger? logger = null)
    {
        var sv1 = BuildSv(Convert.FromHexString("C33C"), uid, readCtrLsb, logger);
        var sv2 = BuildSv(Convert.FromHexString("3CC3"), uid, readCtrLsb, logger);

        var ksesEnc = AesCmac(fileReadKey, sv1);
        var ksesMac = AesCmac(fileReadKey, sv2);

        logger?.LogInformation("Session keys derived: ENC={Enc}, MAC={Mac}",
            Convert.ToHexString(ksesEnc), Convert.ToHexString(ksesMac));

        return (ksesEnc, ksesMac);
    }

    /// <summary>
    /// Build IVe for file data decryption per AN12196.
    /// IVe = AES-128(KSesSDMFileReadENC; SDMReadCtr || 13 zero bytes)
    /// </summary>
    public static byte[] BuildIve(byte[] ksesEnc, byte[] readCtrLsb, ILogger? logger = null)
    {
        if (readCtrLsb.Length != 3)
            throw new ArgumentException("SDMReadCtr must be 3 bytes");

        var seed = new byte[16];
        Array.Copy(readCtrLsb, 0, seed, 0, 3);
        // Rest is already zero-initialized

        using var aes = Aes.Create();
        aes.Key = ksesEnc;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        aes.IV = ZeroIv;

        using var encryptor = aes.CreateEncryptor();
        var ive = encryptor.TransformFinalBlock(seed, 0, 16);

        logger?.LogInformation("IVe built: {Ive}", Convert.ToHexString(ive));
        return ive;
    }

    /// <summary>
    /// Decrypt encrypted file data using session encryption key.
    /// </summary>
    public static byte[]? DecryptFileData(
        byte[] ksesEnc,
        byte[] encryptedData,
        byte[] ive,
        ILogger? logger = null)
    {
        if (encryptedData.Length % 16 != 0)
        {
            logger?.LogWarning("Encrypted data length must be multiple of 16");
            return null;
        }

        try
        {
            using var aes = Aes.Create();
            aes.Key = ksesEnc;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None;
            aes.IV = ive;

            using var decryptor = aes.CreateDecryptor();
            var decrypted = decryptor.TransformFinalBlock(encryptedData, 0, encryptedData.Length);

            logger?.LogInformation("File data decrypted: {Decrypted}", Convert.ToHexString(decrypted));
            return decrypted;
        }
        catch (CryptographicException ex)
        {
            logger?.LogWarning("File data decryption failed: {Msg}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Compute AES-CMAC.
    /// </summary>
    public static byte[] AesCmac(byte[] key, byte[] message)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.Zeros;

        // CMAC implementation using AES-CBC and final block processing
        // Per RFC 4493: https://tools.ietf.org/html/rfc4493

        var blockSize = 16;
        var n = (message.Length + blockSize - 1) / blockSize;
        var lastBlockComplete = message.Length % blockSize == 0 && message.Length > 0;

        if (n == 0)
        {
            n = 1;
            lastBlockComplete = false;
        }

        // Generate subkeys K1 and K2
        var zeroBlock = new byte[blockSize];
        var k0 = aes.EncryptEcb(zeroBlock, PaddingMode.None);

        var k1 = LeftShiftOneBit(k0);
        if ((k0[0] & 0x80) != 0)
            k1[blockSize - 1] ^= 0x87;

        var k2 = LeftShiftOneBit(k1);
        if ((k1[0] & 0x80) != 0)
            k2[blockSize - 1] ^= 0x87;

        // Process blocks
        var x = new byte[blockSize];
        for (var i = 0; i < n - 1; i++)
        {
            var block = new byte[blockSize];
            Array.Copy(message, i * blockSize, block, 0, blockSize);
            XorBlock(x, block);
            x = aes.EncryptEcb(x, PaddingMode.None);
        }

        // Last block
        var lastBlock = new byte[blockSize];
        var lastBlockLen = message.Length - (n - 1) * blockSize;
        if (lastBlockLen > 0)
        {
            Array.Copy(message, (n - 1) * blockSize, lastBlock, 0, lastBlockLen);
        }

        if (!lastBlockComplete)
        {
            // Padding: append 0x80 followed by zeros
            lastBlock[lastBlockLen] = 0x80;
            XorBlock(lastBlock, k2);
        }
        else
        {
            XorBlock(lastBlock, k1);
        }

        XorBlock(x, lastBlock);
        var cmac = aes.EncryptEcb(x, PaddingMode.None);

        return cmac;
    }

    private static byte[] LeftShiftOneBit(byte[] input)
    {
        var output = new byte[input.Length];
        byte overflow = 0;
        for (var i = input.Length - 1; i >= 0; i--)
        {
            output[i] = (byte)((input[i] << 1) | overflow);
            overflow = (byte)((input[i] & 0x80) != 0 ? 1 : 0);
        }
        return output;
    }

    private static void XorBlock(byte[] a, byte[] b)
    {
        for (var i = 0; i < a.Length; i++)
            a[i] ^= b[i];
    }

    /// <summary>
    /// Truncate SDM CMAC by taking bytes with odd indices (NXP convention).
    /// Returns bytes at positions 1, 3, 5, ..., 15 (8 bytes total).
    /// </summary>
    public static byte[] TruncateSdmCmac(byte[] fullCmac, ILogger? logger = null)
    {
        if (fullCmac.Length != 16)
            throw new ArgumentException("Full CMAC must be 16 bytes");

        var truncated = new byte[8];
        for (var i = 0; i < 8; i++)
            truncated[i] = fullCmac[i * 2 + 1];

        logger?.LogInformation("CMAC truncated: {Truncated}", Convert.ToHexString(truncated));
        return truncated;
    }

    /// <summary>
    /// Verify SDM CMAC.
    /// </summary>
    public static bool VerifyCmac(
        byte[] ksesMac,
        byte[] macInput,
        byte[] givenCmac,
        ILogger? logger = null)
    {
        if (givenCmac.Length != 8)
        {
            logger?.LogWarning("Given CMAC must be 8 bytes, got {Len}", givenCmac.Length);
            return false;
        }

        var fullCmac = AesCmac(ksesMac, macInput);
        var truncated = TruncateSdmCmac(fullCmac, logger);

        var match = truncated.SequenceEqual(givenCmac);
        logger?.LogInformation("CMAC verification: computed={Computed}, given={Given}, match={Match}",
            Convert.ToHexString(truncated), Convert.ToHexString(givenCmac), match);

        return match;
    }

    /// <summary>
    /// Result of a successful SUN validation.
    /// </summary>
    public record SunValidationResult(byte[] Uid, int ReadCtr);

    /// <summary>
    /// Parsed PICCData structure.
    /// </summary>
    public record PiccData(
        byte[] Raw,
        byte Tag,
        byte[]? Uid,
        byte[]? ReadCtrLsb,
        int? ReadCtrInt,
        byte[] RandomPadding,
        bool UidMirroring,
        bool CtrMirroring,
        int UidLengthNibble
    );

    /// <summary>
    /// Result of NTAG 424 DNA SDM validation.
    /// </summary>
    public record Ntag424ValidationResult(
        byte[] Uid,
        int ReadCtr,
        byte[]? DecryptedFileData,
        bool CmacValid
    );
}

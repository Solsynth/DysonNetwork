using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DysonNetwork.Drive.Storage;

public sealed class FileE2eeEncryptionMetadata
{
    public string EncryptionScheme { get; init; } = "file.aesgcm.v1";
    public byte[]? EncryptionHeader { get; init; }
    public byte[]? EncryptionSignature { get; init; }
}

public static class FileEncryptor
{
    private static readonly byte[] EnvelopeMagic = "DYE2EE1\0"u8.ToArray();
    private static readonly byte[] LegacyMagic = "DYSON1"u8.ToArray();

    private sealed class FileE2eeEnvelopeHeader
    {
        public string EncryptionScheme { get; set; } = "file.aesgcm.v1";
        public string? EncryptionHeader { get; set; }
        public string? EncryptionSignature { get; set; }
        public string Kdf { get; set; } = "hkdf-sha256";
    }

    public static FileE2eeEncryptionMetadata EncryptFileWithE2eeKey(
        string inputPath,
        string outputPath,
        byte[] key,
        string encryptionScheme = "file.aesgcm.v1",
        byte[]? encryptionHeader = null,
        byte[]? encryptionSignature = null
    )
    {
        return EncryptFileWithKey(
            inputPath,
            outputPath,
            key,
            "hkdf-sha256",
            RandomNumberGenerator.GetBytes(16),
            encryptionScheme,
            encryptionHeader,
            encryptionSignature
        );
    }

    public static void DecryptFileWithE2eeKey(string inputPath, string outputPath, byte[] key)
    {
        var input = File.ReadAllBytes(inputPath);
        if (!TryReadEnvelopeHeader(input, out _))
            throw new CryptographicException("The file is not in E2EE envelope format.");
        DecryptEnvelopeFileWithKey(input, outputPath, key);
    }

    private static FileE2eeEncryptionMetadata EncryptFileWithKey(
        string inputPath,
        string outputPath,
        byte[] key,
        string kdf,
        byte[]? salt,
        string encryptionScheme,
        byte[]? encryptionHeader,
        byte[]? encryptionSignature
    )
    {
        if (key.Length != 32)
            throw new CryptographicException("E2EE file key must be 32 bytes.");
        if (salt is null || salt.Length == 0)
            throw new CryptographicException("Salt is required for HKDF key derivation.");

        var derivedKey = DeriveAesKeyWithHkdfSha256(key, salt);
        var nonce = RandomNumberGenerator.GetBytes(12);
        using var aes = new AesGcm(derivedKey, 16);

        var plaintext = File.ReadAllBytes(inputPath);
        var contentWithMagic = new byte[LegacyMagic.Length + plaintext.Length];
        Buffer.BlockCopy(LegacyMagic, 0, contentWithMagic, 0, LegacyMagic.Length);
        Buffer.BlockCopy(plaintext, 0, contentWithMagic, LegacyMagic.Length, plaintext.Length);

        var ciphertext = new byte[contentWithMagic.Length];
        var tag = new byte[16];
        var envelope = new FileE2eeEnvelopeHeader
        {
            EncryptionScheme = encryptionScheme,
            EncryptionHeader = encryptionHeader is null ? null : Convert.ToBase64String(encryptionHeader),
            EncryptionSignature = encryptionSignature is null ? null : Convert.ToBase64String(encryptionSignature),
            Kdf = kdf
        };
        var envelopeBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope));

        using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        fs.Write(EnvelopeMagic);
        fs.WriteByte(1); // Version
        fs.WriteByte((byte)(salt?.Length ?? 0));
        if (salt is { Length: > 0 })
            fs.Write(salt);
        fs.Write(nonce);
        fs.Write(BitConverter.GetBytes(envelopeBytes.Length));
        fs.Write(envelopeBytes);
        aes.Encrypt(nonce, contentWithMagic, ciphertext, tag, envelopeBytes);
        fs.Write(ciphertext);
        fs.Write(tag);

        return new FileE2eeEncryptionMetadata
        {
            EncryptionScheme = encryptionScheme,
            EncryptionHeader = encryptionHeader,
            EncryptionSignature = encryptionSignature
        };
    }

    private static bool TryReadEnvelopeHeader(byte[] input, out int payloadStart)
    {
        payloadStart = 0;
        if (input.Length < EnvelopeMagic.Length + 2)
            return false;
        if (!input.AsSpan(0, EnvelopeMagic.Length).SequenceEqual(EnvelopeMagic))
            return false;

        var offset = EnvelopeMagic.Length;
        var version = input[offset++];
        if (version != 1)
            throw new CryptographicException($"Unsupported E2EE file envelope version: {version}");
        var saltLength = input[offset++];
        if (input.Length < offset + saltLength + 12 + 16 + 4)
            throw new CryptographicException("Corrupted E2EE file envelope.");

        offset += saltLength + 12;
        var headerLength = BitConverter.ToInt32(input, offset);
        offset += 4;
        if (headerLength < 0 || input.Length < offset + headerLength)
            throw new CryptographicException("Corrupted E2EE file envelope header.");

        payloadStart = offset;
        return true;
    }

    private static void DecryptEnvelopeFileWithKey(byte[] input, string outputPath, byte[] key)
    {
        var offset = EnvelopeMagic.Length + 1; // magic + version
        var saltLength = input[offset++];
        var salt = input.AsSpan(offset, saltLength).ToArray();
        offset += saltLength;
        var nonce = input.AsSpan(offset, 12).ToArray();
        offset += 12;
        var headerLength = BitConverter.ToInt32(input, offset);
        offset += 4;
        var headerBytes = input.AsSpan(offset, headerLength).ToArray();
        offset += headerLength;
        if (input.Length < offset + 16)
            throw new CryptographicException("Corrupted E2EE file envelope payload.");
        var ciphertextLength = input.Length - offset - 16;
        var ciphertext = input.AsSpan(offset, ciphertextLength).ToArray();
        var tag = input.AsSpan(offset + ciphertextLength, 16).ToArray();

        if (key.Length != 32)
            throw new CryptographicException("E2EE file key must be 32 bytes.");
        if (salt.Length == 0)
            throw new CryptographicException("Missing HKDF salt in file envelope.");
        var derivedKey = DeriveAesKeyWithHkdfSha256(key, salt);
        DecryptEnvelopeCiphertext(outputPath, derivedKey, nonce, tag, ciphertext, headerBytes);
    }

    private static void DecryptEnvelopeCiphertext(
        string outputPath,
        byte[] key,
        byte[] nonce,
        byte[] tag,
        byte[] ciphertext,
        byte[] aad
    )
    {
        var decrypted = new byte[ciphertext.Length];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(nonce, ciphertext, tag, decrypted, aad);

        if (decrypted.Length < LegacyMagic.Length || LegacyMagic.Where((t, i) => decrypted[i] != t).Any())
            throw new CryptographicException("Incorrect key or corrupted file.");

        var plaintext = decrypted[LegacyMagic.Length..];
        File.WriteAllBytes(outputPath, plaintext);
    }

    private static byte[] DeriveAesKeyWithHkdfSha256(byte[] ikm, byte[] salt)
    {
        var prk = HkdfExtractSha256(salt, ikm);
        return HkdfExpandSha256(prk, "dyson.drive.file.aesgcm.v1"u8.ToArray(), 32);
    }

    private static byte[] HkdfExtractSha256(byte[] salt, byte[] ikm)
    {
        using var hmac = new HMACSHA256(salt);
        return hmac.ComputeHash(ikm);
    }

    private static byte[] HkdfExpandSha256(byte[] prk, byte[] info, int length)
    {
        var okm = new byte[length];
        var generated = 0;
        var previousBlock = Array.Empty<byte>();
        byte counter = 1;
        while (generated < length)
        {
            using var hmac = new HMACSHA256(prk);
            var input = new byte[previousBlock.Length + info.Length + 1];
            Buffer.BlockCopy(previousBlock, 0, input, 0, previousBlock.Length);
            Buffer.BlockCopy(info, 0, input, previousBlock.Length, info.Length);
            input[^1] = counter;
            previousBlock = hmac.ComputeHash(input);
            var remaining = length - generated;
            var copyLength = Math.Min(remaining, previousBlock.Length);
            Buffer.BlockCopy(previousBlock, 0, okm, generated, copyLength);
            generated += copyLength;
            counter++;
        }

        return okm;
    }
}

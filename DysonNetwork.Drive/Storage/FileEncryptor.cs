using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DysonNetwork.Drive.Storage;

public sealed class FileE2eeEncryptionMetadata
{
    public string EncryptionScheme { get; init; } = "pass.e2ee.file.password.v1";
    public long? EncryptionEpoch { get; init; }
    public byte[]? EncryptionHeader { get; init; }
    public byte[]? EncryptionSignature { get; init; }
}

public static class FileEncryptor
{
    private static readonly byte[] EnvelopeMagic = "DYE2EE1\0"u8.ToArray();
    private static readonly byte[] LegacyMagic = "DYSON1"u8.ToArray();

    private sealed class FileE2eeEnvelopeHeader
    {
        public string EncryptionScheme { get; set; } = "pass.e2ee.file.password.v1";
        public long? EncryptionEpoch { get; set; }
        public string? EncryptionHeader { get; set; }
        public string? EncryptionSignature { get; set; }
        public string Kdf { get; set; } = "pbkdf2-sha256";
    }

    public static FileE2eeEncryptionMetadata EncryptFileWithE2eeKey(
        string inputPath,
        string outputPath,
        byte[] key,
        string encryptionScheme = "pass.e2ee.file.raw-key.v1",
        long? encryptionEpoch = null,
        byte[]? encryptionHeader = null,
        byte[]? encryptionSignature = null
    )
    {
        return EncryptFileWithKey(
            inputPath,
            outputPath,
            key,
            "none",
            null,
            encryptionScheme,
            encryptionEpoch,
            encryptionHeader,
            encryptionSignature
        );
    }

    public static void DecryptFileWithE2eeKey(string inputPath, string outputPath, byte[] key)
    {
        var input = File.ReadAllBytes(inputPath);
        if (!TryReadEnvelopeHeader(input, out var envelopeStart))
            throw new CryptographicException("The file is not in E2EE envelope format.");
        DecryptEnvelopeFileWithKey(input, envelopeStart, outputPath, key);
    }

    private static FileE2eeEncryptionMetadata EncryptFileWithKey(
        string inputPath,
        string outputPath,
        byte[] key,
        string kdf,
        byte[]? salt,
        string encryptionScheme,
        long? encryptionEpoch,
        byte[]? encryptionHeader,
        byte[]? encryptionSignature
    )
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        using var aes = new AesGcm(key, 16);

        var plaintext = File.ReadAllBytes(inputPath);
        var contentWithMagic = new byte[LegacyMagic.Length + plaintext.Length];
        Buffer.BlockCopy(LegacyMagic, 0, contentWithMagic, 0, LegacyMagic.Length);
        Buffer.BlockCopy(plaintext, 0, contentWithMagic, LegacyMagic.Length, plaintext.Length);

        var ciphertext = new byte[contentWithMagic.Length];
        var tag = new byte[16];
        aes.Encrypt(nonce, contentWithMagic, ciphertext, tag);

        var envelope = new FileE2eeEnvelopeHeader
        {
            EncryptionScheme = encryptionScheme,
            EncryptionEpoch = encryptionEpoch,
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
        fs.Write(tag);
        fs.Write(BitConverter.GetBytes(envelopeBytes.Length));
        fs.Write(envelopeBytes);
        fs.Write(ciphertext);

        return new FileE2eeEncryptionMetadata
        {
            EncryptionScheme = encryptionScheme,
            EncryptionEpoch = encryptionEpoch,
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

        offset += saltLength + 12 + 16;
        var headerLength = BitConverter.ToInt32(input, offset);
        offset += 4;
        if (headerLength < 0 || input.Length < offset + headerLength)
            throw new CryptographicException("Corrupted E2EE file envelope header.");

        payloadStart = offset;
        return true;
    }

    private static void DecryptEnvelopeFileWithKey(byte[] input, int headerStart, string outputPath, byte[] key)
    {
        var offset = EnvelopeMagic.Length + 1; // magic + version
        var saltLength = input[offset++];
        offset += saltLength;
        var nonce = input.AsSpan(offset, 12).ToArray();
        offset += 12;
        var tag = input.AsSpan(offset, 16).ToArray();
        offset += 16;
        var headerLength = BitConverter.ToInt32(input, offset);
        offset += 4;
        offset += headerLength;
        var ciphertext = input.AsSpan(offset).ToArray();

        DecryptEnvelopeCiphertext(outputPath, key, nonce, tag, ciphertext);
    }

    private static void DecryptEnvelopeCiphertext(
        string outputPath,
        byte[] key,
        byte[] nonce,
        byte[] tag,
        byte[] ciphertext
    )
    {
        var decrypted = new byte[ciphertext.Length];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(nonce, ciphertext, tag, decrypted);

        if (decrypted.Length < LegacyMagic.Length || LegacyMagic.Where((t, i) => decrypted[i] != t).Any())
            throw new CryptographicException("Incorrect key/password or corrupted file.");

        var plaintext = decrypted[LegacyMagic.Length..];
        File.WriteAllBytes(outputPath, plaintext);
    }
}

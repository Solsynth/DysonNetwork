using System.Security.Cryptography;

namespace DysonNetwork.Drive.Storage;

public static class FileEncryptor
{
    public static void EncryptFile(string inputPath, string outputPath, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var key = DeriveKey(password, salt, 32);
        var nonce = RandomNumberGenerator.GetBytes(12); // For AES-GCM

        using var aes = new AesGcm(key, 16); // Specify 16-byte tag size explicitly
        var plaintext = File.ReadAllBytes(inputPath);
        var magic = "DYSON1"u8.ToArray();
        var contentWithMagic = new byte[magic.Length + plaintext.Length];
        Buffer.BlockCopy(magic, 0, contentWithMagic, 0, magic.Length);
        Buffer.BlockCopy(plaintext, 0, contentWithMagic, magic.Length, plaintext.Length);

        var ciphertext = new byte[contentWithMagic.Length];
        var tag = new byte[16];
        aes.Encrypt(nonce, contentWithMagic, ciphertext, tag);

        // Save as: [salt (16)][nonce (12)][tag (16)][ciphertext]
        using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        fs.Write(salt);
        fs.Write(nonce);
        fs.Write(tag);
        fs.Write(ciphertext);
    }

    public static void DecryptFile(string inputPath, string outputPath, string password)
    {
        var input = File.ReadAllBytes(inputPath);

        var salt = input[..16];
        var nonce = input[16..28];
        var tag = input[28..44];
        var ciphertext = input[44..];

        var key = DeriveKey(password, salt, 32);
        var decrypted = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, 16); // Specify 16-byte tag size explicitly
        aes.Decrypt(nonce, ciphertext, tag, decrypted);

        var magic = "DYSON1"u8.ToArray();
        if (magic.Where((t, i) => decrypted[i] != t).Any())
            throw new CryptographicException("Incorrect password or corrupted file.");

        var plaintext = decrypted[magic.Length..];
        File.WriteAllBytes(outputPath, plaintext);
    }

    private static byte[] DeriveKey(string password, byte[] salt, int keyBytes)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 100_000, HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(keyBytes);
    }
}
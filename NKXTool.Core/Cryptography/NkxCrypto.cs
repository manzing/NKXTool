namespace NKXTool.Core.Cryptography;

using System.Security.Cryptography;

/// <summary>
/// Cryptographic utilities for Kontakt protected containers.
/// Employs hardware-accelerated AES (AES-NI) in ECB mode.
/// </summary>
public static class NkxCrypto
{
    /// <summary>
    /// Decrypts a buffer in-place using AES-ECB with the specified key.
    /// </summary>
    public static void DecryptAesEcbInPlace(byte[] key, Span<byte> data)
    {
        if (key == null || key.Length == 0)
            throw new ArgumentNullException(nameof(key));

        using var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        aes.Key = key;

        using var decryptor = aes.CreateDecryptor();

        // Process full 16-byte blocks
        int fullBlocksLen = (data.Length / 16) * 16;
        if (fullBlocksLen > 0)
        {
            byte[] temp = data[..fullBlocksLen].ToArray();
            int bytesWritten = decryptor.TransformBlock(temp, 0, fullBlocksLen, temp, 0);
            temp.AsSpan(0, bytesWritten).CopyTo(data);
        }
    }

    /// <summary>
    /// Encrypts a buffer in-place using AES-ECB with the specified key.
    /// </summary>
    public static void EncryptAesEcbInPlace(byte[] key, Span<byte> data)
    {
        if (key == null || key.Length == 0)
            throw new ArgumentNullException(nameof(key));

        using var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        aes.Key = key;

        using var encryptor = aes.CreateEncryptor();

        int fullBlocksLen = (data.Length / 16) * 16;
        if (fullBlocksLen > 0)
        {
            byte[] temp = data[..fullBlocksLen].ToArray();
            int bytesWritten = encryptor.TransformBlock(temp, 0, fullBlocksLen, temp, 0);
            temp.AsSpan(0, bytesWritten).CopyTo(data);
        }
    }
}

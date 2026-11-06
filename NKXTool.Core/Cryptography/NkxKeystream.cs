namespace NKXTool.Core.Cryptography;

using System.Security.Cryptography;

/// <summary>
/// Native high-performance generator and applier for Kontakt AES-256 CTR / LCG keystreams.
/// Generates the repeating 65,536-byte (64 KB) XOR keystream used by modern protected Kontakt containers.
/// </summary>
public static class NkxKeystream
{
    public const int KeystreamSize = 0x10000; // 65,536 bytes (64 KB)

    /// <summary>Number of static keys available to legacy (version 0x0100) containers.</summary>
    public const int LegacyKeyCount = 32;

    /// <summary>Seed of the legacy static key table.</summary>
    public const uint LegacyKeySeed = 0x6EE38FE0;

    /// <summary>
    /// Generates the 64 KB keystream using JDX (32-byte AES key) and HU (16-byte initial counter).
    /// </summary>
    /// <param name="jdxKey">32-byte AES-256 key.</param>
    /// <param name="huCounter">16-byte initial 128-bit big-endian counter.</param>
    /// <returns>A 65,536-byte XOR keystream buffer.</returns>
    public static byte[] CreateKeystream(byte[] jdxKey, byte[] huCounter)
    {
        if (jdxKey == null || jdxKey.Length != 32)
            throw new ArgumentException("JDX key must be exactly 32 bytes.", nameof(jdxKey));
        if (huCounter == null || huCounter.Length != 16)
            throw new ArgumentException("HU counter must be exactly 16 bytes.", nameof(huCounter));

        byte[] keystream = new byte[KeystreamSize];

        // 1. Initialize keystream using MSVC CRT LCG pseudo-random stream
        uint seed = 0x608DA0A2;
        for (int i = 0; i < KeystreamSize; i++)
        {
            seed = unchecked(seed * 0x343FD + 0x269EC3);
            keystream[i] = (byte)(seed >> 16);
        }

        // 2. AES-256 ECB keystream blocks XORed into LCG stream
        using var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        aes.Key = jdxKey;

        using var encryptor = aes.CreateEncryptor();

        byte[] counter = new byte[16];
        Buffer.BlockCopy(huCounter, 0, counter, 0, 16);

        byte[] block = new byte[16];
        encryptor.TransformBlock(counter, 0, 16, block, 0);

        for (int i = 0; i < KeystreamSize; i++)
        {
            if (i != 0 && (i % 16) == 0)
            {
                IncrementCounter(counter);
                encryptor.TransformBlock(counter, 0, 16, block, 0);
            }

            keystream[i] ^= block[i % 16];
        }

        return keystream;
    }

    /// <summary>
    /// Generates one of the 32 static 16-byte keys used by legacy (version 0x0100) containers.
    /// The whole table is derived from a single MSVC CRT LCG stream, so a key can only be produced
    /// by walking the stream up to its index.
    /// </summary>
    /// <param name="keyIndex">Key index stored in the chunk header (0 to 0x1F).</param>
    public static byte[] CreateLegacyKey(int keyIndex)
    {
        if (keyIndex < 0 || keyIndex > LegacyKeyCount - 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(keyIndex), keyIndex, $"Legacy key index must be between 0 and {LegacyKeyCount - 1}.");
        }

        byte[] key = new byte[16];
        uint seed = LegacyKeySeed;

        for (int index = 0; index <= keyIndex; index++)
        {
            for (int n = 0; n < key.Length; n++)
            {
                seed = unchecked(seed * 0x343FD + 0x269EC3);
                if (index == keyIndex)
                    key[n] = (byte)(seed >> 16);
            }
        }

        return key;
    }

    /// <summary>
    /// Increments a 128-bit big-endian counter in-place.
    /// </summary>
    private static void IncrementCounter(Span<byte> counter)
    {
        for (int i = counter.Length - 1; i >= 0; i--)
        {
            counter[i]++;
            if (counter[i] != 0)
                break;
        }
    }

    /// <summary>
    /// Applies the 64 KB keystream in-place to a data buffer at a given relative stream offset.
    /// </summary>
    /// <param name="keystream">64 KB keystream buffer.</param>
    /// <param name="streamOffset">Relative byte offset inside the payload stream.</param>
    /// <param name="buffer">Data buffer to decrypt / XOR in place.</param>
    public static void ApplyKeystream(byte[] keystream, long streamOffset, Span<byte> buffer)
    {
        int keyLen = keystream.Length;
        int keyIndex = (int)(streamOffset % keyLen);

        // Vectorized / batch XOR when possible, or fast scalar loop
        int remaining = buffer.Length;
        int bufOffset = 0;

        while (remaining > 0)
        {
            int chunkLen = Math.Min(remaining, keyLen - keyIndex);
            var srcKey = keystream.AsSpan(keyIndex, chunkLen);
            var dstBuf = buffer.Slice(bufOffset, chunkLen);

            for (int i = 0; i < chunkLen; i++)
            {
                dstBuf[i] ^= srcKey[i];
            }

            bufOffset += chunkLen;
            remaining -= chunkLen;
            keyIndex = 0;
        }
    }
}

namespace NKXTool.Core;

using System.Buffers.Binary;
using System.Text;
using NKXTool.Core.Common;
using NKXTool.Core.Containers.Nicnt;
using NKXTool.Core.Containers.Nkx;
using NKXTool.Core.Cryptography;

/// <summary>
/// Unified high-level factory and entry point for opening and packing Kontakt container archives.
/// Designed for simple, direct integration into CLI tools and future UI frontends (NKXToolUI).
/// </summary>
public static class NkxArchive
{
    /// <summary>
    /// Detects the container format and opens an archive reader instance.
    /// </summary>
    /// <param name="filePath">Path to .nkx, .nkr, .nicnt, or .nks file.</param>
    /// <param name="keyDatabase">Optional custom key database.</param>
    /// <returns>An IArchiveReader instance ready for listing or extraction.</returns>
    public static IArchiveReader Open(string filePath, NkxKeyDatabase? keyDatabase = null)
    {
        string fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Archive file not found: {fullPath}", fullPath);

        using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64);
        byte[] probe = new byte[32];
        int read = fs.Read(probe, 0, 32);
        if (read < 16)
            throw new InvalidDataException("File is too small to be a valid Kontakt archive container.");

        // Check 1: NKX / NKR / NKS chunk container (0x5e70ac54)
        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(probe.AsSpan(0, 4));
        if (magic == NkxArchiveReader.ChunkContainerMagic)
        {
            return new NkxArchiveReader(fullPath, keyDatabase);
        }

        // Check 2: NICNT monolith container (/\ NI FC MTD /\)
        string magicStr = Encoding.ASCII.GetString(probe, 0, 16);
        if (magicStr.StartsWith("/\\ NI FC MTD"))
        {
            return new NicntArchiveReader(fullPath);
        }

        // Check 3: NKX monolith container prefixed by a proprietary sample header (0x7FA89012)
        if (NkxArchiveReader.IsSupportedContainer(fullPath))
        {
            return new NkxArchiveReader(fullPath, keyDatabase);
        }

        throw new NotSupportedException($"Unrecognized archive container format in '{Path.GetFileName(filePath)}'.");
    }
}

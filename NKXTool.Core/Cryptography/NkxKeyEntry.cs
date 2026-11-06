namespace NKXTool.Core.Cryptography;

/// <summary>
/// Represents a cryptographic key entry associated with a Kontakt library.
/// </summary>
public sealed class NkxKeyEntry
{
    public string Snpid { get; init; } = string.Empty;
    public string JdxHex { get; init; } = string.Empty;
    public string HuHex { get; init; } = string.Empty;
    public string RegKey { get; init; } = string.Empty;
    public string Company { get; init; } = string.Empty;

    private byte[]? _jdxBytes;
    private byte[]? _huBytes;

    /// <summary>
    /// Decoded 32-byte JDX key.
    /// </summary>
    public byte[] JdxBytes => _jdxBytes ??= Convert.FromHexString(JdxHex);

    /// <summary>
    /// Decoded 16-byte HU key.
    /// </summary>
    public byte[] HuBytes => _huBytes ??= Convert.FromHexString(HuHex);

    public override string ToString() => $"[{Snpid}] {RegKey} ({Company})";
}

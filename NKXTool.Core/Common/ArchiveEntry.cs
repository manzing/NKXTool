namespace NKXTool.Core.Common;

/// <summary>
/// Represents a single file or directory entry inside a container archive.
/// </summary>
public sealed class ArchiveEntry
{
    /// <summary>
    /// Unique internal record or file ID in the container.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// Normalized relative path inside the archive (e.g. "Samples\Kick.wav").
    /// </summary>
    public string RelativePath { get; init; } = string.Empty;

    /// <summary>
    /// Decompressed size in bytes.
    /// </summary>
    public long Size { get; init; }

    /// <summary>
    /// Stored/packed size in bytes.
    /// </summary>
    public long PackedSize { get; init; }

    /// <summary>
    /// Offset in the container file where file data begins.
    /// </summary>
    public long DataOffset { get; init; }

    /// <summary>
    /// Whether this entry represents a directory node.
    /// </summary>
    public bool IsDirectory { get; init; }

    /// <summary>
    /// Whether the payload in this entry is encrypted.
    /// </summary>
    public bool IsEncrypted { get; init; }

    /// <summary>
    /// Key index stored in the chunk header: <c>0xFF</c> when the payload is stored in clear,
    /// <c>0x00</c>-<c>0x1F</c> for the static legacy (0x0100) keys, and <c>0x100</c> for the
    /// modern per-library keystream. <c>-1</c> when the entry carries no header information.
    /// </summary>
    public int KeyIndex { get; init; } = -1;

    /// <summary>
    /// Raw file type flag from the container header (e.g. 1=dir, 2=file).
    /// </summary>
    public uint RawType { get; init; }

    /// <summary>
    /// Optional timestamp if provided by container metadata.
    /// </summary>
    public DateTime? LastModifiedUtc { get; init; }

    public override string ToString() => $"{RelativePath} ({Size} bytes)";
}

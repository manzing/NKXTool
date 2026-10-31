namespace NKXTool.Core.Common;

/// <summary>
/// Common interface for inspecting and extracting container archives.
/// </summary>
public interface IArchiveReader : IDisposable
{
    /// <summary>
    /// Type of container archive.
    /// </summary>
    ArchiveType ArchiveType { get; }

    /// <summary>
    /// Full path to the underlying archive file.
    /// </summary>
    string FilePath { get; }

    /// <summary>
    /// List of file and directory entries indexed in the container.
    /// </summary>
    IReadOnlyList<ArchiveEntry> Entries { get; }

    /// <summary>
    /// Opens a readable stream for a specific file entry.
    /// </summary>
    Stream OpenEntryStream(ArchiveEntry entry);

    /// <summary>
    /// Extracts all (or selected) entries to disk asynchronously.
    /// </summary>
    Task<ExtractionResult> ExtractAllAsync(
        ExtractionOptions options,
        IProgress<ExtractionProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

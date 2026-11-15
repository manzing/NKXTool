namespace NKXTool.Core.Common;

/// <summary>
/// Configuration options for archive extraction operations.
/// </summary>
public sealed class ExtractionOptions
{
    /// <summary>
    /// Root directory on disk where extracted files will be written.
    /// </summary>
    public string DestinationDirectory { get; set; } = string.Empty;

    /// <summary>
    /// When true, overwrites existing destination files instead of skipping.
    /// </summary>
    public bool Overwrite { get; set; }

    /// <summary>
    /// Maximum number of concurrent file extractions. Defaults to CPU core count.
    /// </summary>
    public int MaxDegreeOfParallelism { get; set; } = Math.Max(1, Environment.ProcessorCount);

    /// <summary>
    /// Optional whitelist of relative file paths to extract. If null, extracts all files.
    /// </summary>
    public HashSet<string>? SelectedFiles { get; set; }

    /// <summary>
    /// Buffer size in bytes used for I/O streaming (default 1 MB).
    /// </summary>
    public int StreamBufferSize { get; set; } = 1024 * 1024;

    /// <summary>
    /// When true (the default), an entry whose name holds a character Windows forbids - a "*" in a
    /// library built on macOS, for instance - is written under the name produced by
    /// <see cref="ArchivePathSanitizer"/>, because no Windows API can create the original. When
    /// false, such an entry fails and is reported in <see cref="ExtractionResult.Errors"/>.
    /// </summary>
    public bool SanitizeWindowsFileNames { get; set; } = true;

    /// <summary>
    /// When true (the default), the extraction root receives an <see cref="ArchiveNameMap"/> file
    /// (<c>nkxtool-names.json</c>) listing every entry written under a name other than the one
    /// stored in the archive. Packing that folder restores the original names inside the container,
    /// so the instrument patches keep finding their samples.
    /// </summary>
    public bool WriteNameMap { get; set; } = true;
}

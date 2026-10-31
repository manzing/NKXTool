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
}

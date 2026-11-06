namespace NKXTool.Core.Common;

/// <summary>
/// Detailed diagnostic record for any file that encounters an error during extraction.
/// </summary>
public sealed class ExtractionError
{
    /// <summary>
    /// Unique internal file ID from the archive container.
    /// </summary>
    public int FileId { get; init; }

    /// <summary>
    /// Archive relative path of the file that failed.
    /// </summary>
    public string ArchivePath { get; init; } = string.Empty;

    /// <summary>
    /// Target destination file path on disk.
    /// </summary>
    public string DestinationPath { get; init; } = string.Empty;

    /// <summary>
    /// Byte offset in archive container where file begins.
    /// </summary>
    public long DataOffset { get; init; }

    /// <summary>
    /// Expected file length in bytes.
    /// </summary>
    public long ExpectedSize { get; init; }

    /// <summary>
    /// Descriptive error message in English.
    /// </summary>
    public string ErrorMessage { get; init; } = string.Empty;

    /// <summary>
    /// Original exception if thrown during extraction.
    /// </summary>
    public Exception? Exception { get; init; }

    public override string ToString() => $"[Error in #{FileId} '{ArchivePath}'] {ErrorMessage}";
}

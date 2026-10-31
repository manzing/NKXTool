namespace NKXTool.Core.Common;

/// <summary>
/// Real-time progress report emitted during extraction.
/// </summary>
public sealed class ExtractionProgress
{
    public int TotalFiles { get; init; }
    public int ProcessedFiles { get; init; }
    public int SuccessFiles { get; init; }
    public int FailedFiles { get; init; }
    public int SkippedFiles { get; init; }

    public long TotalBytes { get; init; }
    public long ProcessedBytes { get; init; }

    public string? CurrentFile { get; init; }

    public double PercentComplete => TotalFiles > 0 ? (double)ProcessedFiles / TotalFiles * 100.0 : 0.0;
}

namespace NKXTool.Core.Common;

/// <summary>
/// Summary report returned upon completion of an extraction operation.
/// </summary>
public sealed class ExtractionResult
{
    public bool IsSuccess => Errors.Count == 0;

    public int TotalCount { get; init; }
    public int ExtractedCount { get; init; }
    public int SkippedCount { get; init; }
    public int FailedCount => Errors.Count;

    public TimeSpan Elapsed { get; init; }

    public IReadOnlyList<ExtractionError> Errors { get; init; } = Array.Empty<ExtractionError>();
}
